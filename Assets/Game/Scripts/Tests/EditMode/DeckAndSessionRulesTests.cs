using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Data;
using Game.Persistence;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode
{
    public sealed class DeckAndSessionRulesTests
    {
        private sealed class TestUnlockedPool : IUnlockedCardProvider
        {
            private readonly HashSet<ushort> _unlocked;

            public TestUnlockedPool(params ushort[] unlockedIds)
            {
                _unlocked = new HashSet<ushort>(unlockedIds);
            }

            public IReadOnlyCollection<ushort> UnlockedCardIds => _unlocked;

            public bool IsCardUnlocked(ushort cardId)
            {
                return _unlocked.Contains(cardId);
            }

            public void Unlock(ushort cardId)
            {
                _unlocked.Add(cardId);
            }
        }

        private const ushort BalloonId = 1;
        private const ushort CannonId = 10;
        private const ushort ShieldId = 11;
        private const ushort FlakId = 12;
        private const ushort BomberSpawnerId = 13;
        private const ushort GoldMineId = 20;
        private const ushort HqId = 99;

        private readonly List<Object> _createdAssets = new();
        private CardCatalogSO _catalog;

        [SetUp]
        public void SetUp()
        {
            StructureCardSO balloon = CreateCard(BalloonId, "Territory Balloon", StructureCategory.Territory, true, false, true);
            StructureCardSO cannon = CreateCard(CannonId, "Cannon", StructureCategory.Offense, false, true, true);
            StructureCardSO shield = CreateCard(ShieldId, "Shield Generator", StructureCategory.Defense, false, true, true);
            StructureCardSO flak = CreateCard(FlakId, "Flak Tower", StructureCategory.Offense, false, true, true);
            StructureCardSO spawner = CreateCard(BomberSpawnerId, "Bomb Bot Spawner", StructureCategory.Spawner, false, true, true);
            StructureCardSO goldMine = CreateCard(GoldMineId, "Gold Mine", StructureCategory.Economy, false, true, false);
            StructureCardSO hq = CreateCard(HqId, "Headquarters", StructureCategory.Core, false, false, true);

            _catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            _createdAssets.Add(_catalog);
            _catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, spawner, goldMine, hq });
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
        }

        private StructureCardSO CreateCard(
            ushort id,
            string name,
            StructureCategory category,
            bool lockedSlotZero,
            bool selectable,
            bool unlockedByDefault)
        {
            var card = ScriptableObject.CreateInstance<StructureCardSO>();
            card.ConfigureForTest(id, name, category, lockedSlotZero, selectable, unlockedByDefault);
            _createdAssets.Add(card);
            return card;
        }

        [Test]
        public void ValidDeckWithoutGoldMinePassesWhenSlotZeroIsTerritoryBalloon()
        {
            ushort[] deck = { BalloonId, CannonId, ShieldId, FlakId, BomberSpawnerId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.None));
        }

        [Test]
        public void ValidDeckWithUnlockedGoldMineInFreeSlotPasses()
        {
            var ownerPool = new TestUnlockedPool(CannonId, ShieldId, FlakId, GoldMineId);
            ushort[] deck = { BalloonId, GoldMineId, CannonId, ShieldId, FlakId };

            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog, ownerPool);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.None));
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(6)]
        public void InvalidDeckSizeIsRejected(int size)
        {
            var deck = new ushort[size];
            if (size > 0)
            {
                deck[0] = BalloonId;
            }

            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.InvalidDeckSize));
        }

        [Test]
        public void NullDeckIsRejected()
        {
            LoadoutValidationResult result = LoadoutValidator.Validate(null, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.InvalidDeckSize));
        }

        [Test]
        public void SlotZeroNotTerritoryBalloonIsRejected()
        {
            ushort[] deck = { CannonId, ShieldId, FlakId, BomberSpawnerId, GoldMineId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.SlotZeroMustBeTerritoryBalloon));
            Assert.That(result.FailingSlotIndex, Is.EqualTo(DeckRules.LockedBalloonSlotIndex));
        }

        [Test]
        public void DuplicateBalloonInFreeSlotIsRejected()
        {
            ushort[] deck = { BalloonId, BalloonId, CannonId, ShieldId, FlakId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.DuplicateCard));
            Assert.That(result.FailingSlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateRegularCardIsRejected()
        {
            ushort[] deck = { BalloonId, CannonId, ShieldId, CannonId, FlakId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.DuplicateCard));
            Assert.That(result.FailingSlotIndex, Is.EqualTo(3));
        }

        [Test]
        public void NonSelectableCardInFreeSlotIsRejected()
        {
            ushort[] deck = { BalloonId, CannonId, HqId, ShieldId, FlakId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.CardNotSelectable));
            Assert.That(result.FailingSlotIndex, Is.EqualTo(2));
        }

        [Test]
        public void UnknownCardInFreeSlotIsRejected()
        {
            ushort[] deck = { BalloonId, CannonId, 999, ShieldId, FlakId };
            LoadoutValidationResult result = LoadoutValidator.Validate(deck, _catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.EqualTo(LoadoutValidationError.UnknownCard));
            Assert.That(result.FailingSlotIndex, Is.EqualTo(2));
        }

        [Test]
        public void LocalPvPUsesOwnerUnlockedPoolForBothPlayers()
        {
            // Owner has starter cards unlocked, GoldMine (20) is NOT unlocked yet.
            var localOwnerPool = new TestUnlockedPool(CannonId, ShieldId, FlakId, BomberSpawnerId);
            ushort[] p1DeckWithLockedGoldMine = { BalloonId, GoldMineId, CannonId, ShieldId, FlakId };
            ushort[] p2DeckWithLockedGoldMine = { BalloonId, CannonId, GoldMineId, ShieldId, BomberSpawnerId };

            LoadoutValidationResult p1Before = LoadoutValidator.Validate(p1DeckWithLockedGoldMine, _catalog, localOwnerPool);
            LoadoutValidationResult p2Before = LoadoutValidator.Validate(p2DeckWithLockedGoldMine, _catalog, localOwnerPool);

            Assert.That(p1Before.IsValid, Is.False);
            Assert.That(p1Before.Error, Is.EqualTo(LoadoutValidationError.CardLocked));
            Assert.That(p2Before.IsValid, Is.False);
            Assert.That(p2Before.Error, Is.EqualTo(LoadoutValidationError.CardLocked));

            // Once the local owner unlocks Gold Mine, both P1 and P2 can draft it in Local PvP.
            localOwnerPool.Unlock(GoldMineId);

            LoadoutValidationResult p1After = LoadoutValidator.Validate(p1DeckWithLockedGoldMine, _catalog, localOwnerPool);
            LoadoutValidationResult p2After = LoadoutValidator.Validate(p2DeckWithLockedGoldMine, _catalog, localOwnerPool);

            Assert.That(p1After.IsValid, Is.True);
            Assert.That(p2After.IsValid, Is.True);
        }

        [TestCase("127.0.0.1", true)]
        [TestCase("::1", true)]
        [TestCase("localhost", true)]
        [TestCase("192.168.1.50", false)]
        [TestCase("76561198000000000", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void LoopbackAddressValidationRecognizesOnlyLocalAddresses(string address, bool expected)
        {
            Assert.That(SessionRules.IsLoopbackAddress(address), Is.EqualTo(expected));
        }

        [Test]
        public void LocalLoopbackAdmissionAllowsOnlyFirstLoopbackConnection()
        {
            Assert.That(SessionRules.CanAdmitLocalLoopback("127.0.0.1", 0), Is.True);
            Assert.That(SessionRules.CanAdmitLocalLoopback("::1", 0), Is.True);
            Assert.That(SessionRules.CanAdmitLocalLoopback("127.0.0.1", 1), Is.False);
            Assert.That(SessionRules.CanAdmitLocalLoopback("192.168.1.10", 0), Is.False);
        }

        [Test]
        public void JsonFileSaveServiceRoundTripsProfileData()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DotSeniorDesignTests_" + Guid.NewGuid().ToString("N"));
            string tempFile = Path.Combine(tempDir, "profile.json");

            try
            {
                var service = new JsonFileSaveService(tempFile);
                var original = new PlayerProfileData
                {
                    SchemaVersion = 1,
                    SoftCurrency = 750,
                    UnlockedCardIds = new List<ushort> { CannonId, ShieldId, FlakId, BomberSpawnerId, GoldMineId },
                    LastUsedLoadout = new List<ushort> { BalloonId, GoldMineId, CannonId, ShieldId, FlakId }
                };

                service.Save(original);
                PlayerProfileData loaded = service.Load();

                Assert.That(loaded.SchemaVersion, Is.EqualTo(1));
                Assert.That(loaded.SoftCurrency, Is.EqualTo(750));
                Assert.That(loaded.UnlockedCardIds, Is.EqualTo(original.UnlockedCardIds));
                Assert.That(loaded.LastUsedLoadout, Is.EqualTo(original.LastUsedLoadout));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
