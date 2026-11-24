using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Game.Core;
using Game.Data;
using Game.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode
{
    public sealed class ProgressionAndDraftTests
    {
        private const ushort BalloonId = 1;
        private const ushort CannonId = 10;
        private const ushort ShieldId = 11;
        private const ushort FlakId = 12;
        private const ushort BomberSpawnerId = 13;
        private const ushort GoldMineId = 20;
        private const ushort HeavyMortarId = 21;
        private const ushort HqId = 99;

        private readonly List<Object> _createdAssets = new();
        private CardCatalogSO _catalog;
        private string _tempDir;
        private string _savePath;

        [SetUp]
        public void SetUp()
        {
            StructureCardSO balloon = CreateCard(BalloonId, "Territory Balloon", StructureCategory.Territory, true, false, true, 0);
            StructureCardSO cannon = CreateCard(CannonId, "Cannon", StructureCategory.Offense, false, true, true, 0);
            StructureCardSO shield = CreateCard(ShieldId, "Shield Generator", StructureCategory.Defense, false, true, true, 0);
            StructureCardSO flak = CreateCard(FlakId, "Flak Tower", StructureCategory.Offense, false, true, true, 0);
            StructureCardSO spawner = CreateCard(BomberSpawnerId, "Bomb Bot Spawner", StructureCategory.Spawner, false, true, true, 0);
            StructureCardSO goldMine = CreateCard(GoldMineId, "Gold Mine", StructureCategory.Economy, false, true, false, 150);
            StructureCardSO mortar = CreateCard(HeavyMortarId, "Heavy Mortar", StructureCategory.Offense, false, true, false, 200);
            StructureCardSO hq = CreateCard(HqId, "Headquarters", StructureCategory.Core, false, false, true, 0);

            _catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            _createdAssets.Add(_catalog);
            _catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, spawner, goldMine, mortar, hq });

            _tempDir = Path.Combine(Path.GetTempPath(), "DotProgressionTests_" + Guid.NewGuid().ToString("N"));
            _savePath = Path.Combine(_tempDir, "profile.json");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdAssets.Count; i++)
            {
                if (_createdAssets[i] != null)
                {
                    Object.DestroyImmediate(_createdAssets[i]);
                }
            }
            _createdAssets.Clear();

            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private StructureCardSO CreateCard(
            ushort id,
            string name,
            StructureCategory category,
            bool lockedSlotZero,
            bool selectable,
            bool unlockedByDefault,
            int unlockCost)
        {
            var card = ScriptableObject.CreateInstance<StructureCardSO>();
            card.ConfigureForTest(id, name, category, lockedSlotZero, selectable, unlockedByDefault, 100, unlockCost);
            _createdAssets.Add(card);
            return card;
        }

        [Test]
        public void ProgressionSeedsStartersUnlocksCardsAndPersistsRoundTrip()
        {
            var saveService = new JsonFileSaveService(_savePath);
            var progression = new ProgressionService(saveService, _catalog, initialSoftCurrency: 300);

            progression.Load();

            Assert.That(progression.IsLoaded, Is.True);
            Assert.That(progression.SoftCurrency, Is.EqualTo(300));
            Assert.That(progression.IsCardUnlocked(BalloonId), Is.True);
            Assert.That(progression.IsCardUnlocked(CannonId), Is.True);
            Assert.That(progression.IsCardUnlocked(GoldMineId), Is.False);

            CardUnlockResult unlockResult = progression.TryUnlockCard(GoldMineId);
            Assert.That(unlockResult, Is.EqualTo(CardUnlockResult.Success));
            Assert.That(progression.SoftCurrency, Is.EqualTo(150));
            Assert.That(progression.IsCardUnlocked(GoldMineId), Is.True);

            ushort[] savedDeck = { BalloonId, GoldMineId, CannonId, ShieldId, FlakId };
            Assert.That(progression.TrySaveLastLoadout(savedDeck), Is.True);
            progression.Save();

            var reloaded = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 999);
            reloaded.Load();

            Assert.That(reloaded.SoftCurrency, Is.EqualTo(150));
            Assert.That(reloaded.IsCardUnlocked(GoldMineId), Is.True);
            Assert.That(reloaded.GetLastValidLoadoutOrDefault(), Is.EqualTo(savedDeck));
        }

        [UnityTest]
        public IEnumerator ProgressionAsyncLoadUnlockAndSaveCompletesWithoutDeadlock()
        {
            var progression = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 300);
            yield return WaitTask(progression.LoadAsync(), "LoadAsync");

            Assert.That(progression.IsLoaded, Is.True);
            Assert.That(progression.SoftCurrency, Is.EqualTo(300));

            CardUnlockResult unlockOutcome = CardUnlockResult.UnknownCard;
            Task unlockTask = RunUnlockAsync(progression, GoldMineId, r => unlockOutcome = r);
            yield return WaitTask(unlockTask, "TryUnlockCardAsync");

            Assert.That(unlockOutcome, Is.EqualTo(CardUnlockResult.Success));
            Assert.That(progression.SoftCurrency, Is.EqualTo(150));
            Assert.That(progression.IsCardUnlocked(GoldMineId), Is.True);

            var reloaded = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 999);
            yield return WaitTask(reloaded.LoadAsync(), "ReloadAsync");

            Assert.That(reloaded.SoftCurrency, Is.EqualTo(150));
            Assert.That(reloaded.IsCardUnlocked(GoldMineId), Is.True);
        }

        [Test]
        public void ProgressionRejectsInsufficientCurrencyAlreadyUnlockedAndNonUnlockableCards()
        {
            var progression = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 100);
            progression.Load();

            Assert.That(progression.TryUnlockCard(GoldMineId), Is.EqualTo(CardUnlockResult.InsufficientCurrency));
            Assert.That(progression.TryUnlockCard(CannonId), Is.EqualTo(CardUnlockResult.AlreadyUnlocked));
            Assert.That(progression.TryUnlockCard(BalloonId), Is.EqualTo(CardUnlockResult.NotUnlockable));
            Assert.That(progression.TryUnlockCard(HqId), Is.EqualTo(CardUnlockResult.NotUnlockable));
            Assert.That(progression.TryUnlockCard(999), Is.EqualTo(CardUnlockResult.UnknownCard));

            progression.AddSoftCurrency(50);
            Assert.That(progression.SoftCurrency, Is.EqualTo(150));
            Assert.That(progression.TryUnlockCard(GoldMineId), Is.EqualTo(CardUnlockResult.Success));
            Assert.That(progression.TryUnlockCard(GoldMineId), Is.EqualTo(CardUnlockResult.AlreadyUnlocked));
        }

        [Test]
        public void ProgressionRejectsSavingInvalidLastLoadoutAndFallsBackToStarterDeck()
        {
            var progression = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 300);
            progression.Load();

            ushort[] wrongSlotZero = { CannonId, ShieldId, FlakId, BomberSpawnerId, CannonId };
            ushort[] lockedGoldMine = { BalloonId, GoldMineId, CannonId, ShieldId, FlakId };

            Assert.That(progression.TrySaveLastLoadout(wrongSlotZero), Is.False);
            Assert.That(progression.TrySaveLastLoadout(lockedGoldMine), Is.False);

            ushort[] fallback = progression.GetLastValidLoadoutOrDefault();
            Assert.That(fallback.Length, Is.EqualTo(DeckRules.DeckSize));
            Assert.That(fallback[0], Is.EqualTo(BalloonId));
            Assert.That(LoadoutValidator.Validate(fallback, _catalog, progression).IsValid, Is.True);
        }

        [Test]
        public void DraftSubmissionValidatorEnforcesPhaseSeatOwnershipAndWrongSlotZeroRules()
        {
            var progression = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 300);
            progression.Load();

            ushort[] validStarterDeck = { BalloonId, CannonId, ShieldId, FlakId, BomberSpawnerId };
            ushort[] wrongSlotZeroDeck = { CannonId, ShieldId, FlakId, BomberSpawnerId, BalloonId };
            ushort[] duplicateDeck = { BalloonId, CannonId, CannonId, ShieldId, FlakId };
            ushort[] tooShortDeck = { BalloonId, CannonId, ShieldId, FlakId };

            DraftValidationOutcome wrongPhase = DraftSubmissionValidator.Validate(
                MatchPhase.Playing, MatchMode.LocalPvP, true, true, validStarterDeck, _catalog, progression, true);
            Assert.That(wrongPhase.IsValid, Is.False);
            Assert.That(wrongPhase.RejectReason, Is.EqualTo(DraftRejectReason.NotInDraftPhase));

            DraftValidationOutcome badSeat = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, false, true, validStarterDeck, _catalog, progression, true);
            Assert.That(badSeat.IsValid, Is.False);
            Assert.That(badSeat.RejectReason, Is.EqualTo(DraftRejectReason.InvalidSeat));

            DraftValidationOutcome unauthorized = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.OnlinePvP, true, false, validStarterDeck, _catalog, progression, false);
            Assert.That(unauthorized.IsValid, Is.False);
            Assert.That(unauthorized.RejectReason, Is.EqualTo(DraftRejectReason.UnauthorizedSender));

            DraftValidationOutcome wrongSlotZero = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, wrongSlotZeroDeck, _catalog, progression, true);
            Assert.That(wrongSlotZero.IsValid, Is.False);
            Assert.That(wrongSlotZero.RejectReason, Is.EqualTo(DraftRejectReason.LoadoutInvalid));
            Assert.That(wrongSlotZero.LoadoutError, Is.EqualTo(LoadoutValidationError.SlotZeroMustBeTerritoryBalloon));
            Assert.That(wrongSlotZero.FailingSlotIndex, Is.EqualTo(0));

            DraftValidationOutcome duplicate = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, duplicateDeck, _catalog, progression, true);
            Assert.That(duplicate.IsValid, Is.False);
            Assert.That(duplicate.LoadoutError, Is.EqualTo(LoadoutValidationError.DuplicateCard));

            DraftValidationOutcome tooShort = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, tooShortDeck, _catalog, progression, true);
            Assert.That(tooShort.IsValid, Is.False);
            Assert.That(tooShort.LoadoutError, Is.EqualTo(LoadoutValidationError.InvalidDeckSize));
        }

        [Test]
        public void DraftSubmissionValidatorEnforcesLocalOwnerPoolForBothSeatsAndAllowsRemoteOnlineDeck()
        {
            var progression = new ProgressionService(new JsonFileSaveService(_savePath), _catalog, initialSoftCurrency: 300);
            progression.Load();

            ushort[] deckWithoutGoldMine = { BalloonId, CannonId, ShieldId, FlakId, BomberSpawnerId };
            ushort[] deckWithGoldMine = { BalloonId, GoldMineId, CannonId, ShieldId, FlakId };

            // Local PvP: Seat 0 and Seat 1 share owner's pool; deck without Gold Mine succeeds, locked Gold Mine fails for both.
            Assert.That(
                DraftSubmissionValidator.Validate(MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, deckWithoutGoldMine, _catalog, progression, true).IsValid,
                Is.True);

            DraftValidationOutcome p1Locked = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, deckWithGoldMine, _catalog, progression, true);
            DraftValidationOutcome p2Locked = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, deckWithGoldMine, _catalog, progression, true);

            Assert.That(p1Locked.IsValid, Is.False);
            Assert.That(p1Locked.LoadoutError, Is.EqualTo(LoadoutValidationError.CardLocked));
            Assert.That(p2Locked.IsValid, Is.False);
            Assert.That(p2Locked.LoadoutError, Is.EqualTo(LoadoutValidationError.CardLocked));

            // Online PvP remote client: host validates structural legality against CardCatalogSO (client may have Gold Mine unlocked).
            DraftValidationOutcome onlineRemote = DraftSubmissionValidator.Validate(
                MatchPhase.PreMatchDraft, MatchMode.OnlinePvP, true, true, deckWithGoldMine, _catalog, progression, isSenderLocalHost: false);
            Assert.That(onlineRemote.IsValid, Is.True);

            // Once owner unlocks Gold Mine, Local PvP allows both Seat 0 and Seat 1 to draft it.
            Assert.That(progression.TryUnlockCard(GoldMineId), Is.EqualTo(CardUnlockResult.Success));
            Assert.That(
                DraftSubmissionValidator.Validate(MatchPhase.PreMatchDraft, MatchMode.LocalPvP, true, true, deckWithGoldMine, _catalog, progression, true).IsValid,
                Is.True);
        }

        private static async Task RunUnlockAsync(ProgressionService progression, ushort cardId, Action<CardUnlockResult> onCompleted)
        {
            CardUnlockResult result = await progression.TryUnlockCardAsync(cardId).ConfigureAwait(false);
            onCompleted?.Invoke(result);
        }

        private static IEnumerator WaitTask(Task task, string label)
        {
            var sw = Stopwatch.StartNew();
            while (!task.IsCompleted && sw.Elapsed.TotalSeconds < 5.0)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, $"{label} timed out after 5s.");
            Assert.That(task.IsFaulted, Is.False, $"{label} faulted: {task.Exception}");
            Assert.That(task.IsCanceled, Is.False, $"{label} was canceled.");
        }
    }
}
