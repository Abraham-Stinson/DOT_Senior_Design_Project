using System.Collections.Generic;
using Game.Core;
using Game.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ArenaBattleUiTests
    {
        private sealed class FakeCatalogLookup : ICardCatalogLookup
        {
            private readonly Dictionary<ushort, CardRuleMetadata> _rules = new()
            {
                { 1, new CardRuleMetadata(1, true, false, true) },
                { 10, new CardRuleMetadata(10, false, true, true) },
                { 11, new CardRuleMetadata(11, false, true, true) },
                { 12, new CardRuleMetadata(12, false, true, true) },
                { 13, new CardRuleMetadata(13, false, true, true) },
                { 20, new CardRuleMetadata(20, false, true, false) },
                { 21, new CardRuleMetadata(21, false, true, false) },
                { 22, new CardRuleMetadata(22, false, true, false) }
            };

            public ushort TerritoryBalloonCardId => 1;

            public bool TryGetCardRules(ushort cardId, out CardRuleMetadata metadata)
            {
                return _rules.TryGetValue(cardId, out metadata);
            }
        }

        [TearDown]
        public void TearDown()
        {
            MatchLaunchConfig.Reset();
        }

        [Test]
        public void PreviewCardMappingAndNormalizationProduceValidFiveCardDeck()
        {
            int[] tulparDefault = { 0, 1, 2, -1, -1 };
            ushort[] mapped = MatchLoadoutHandoffResolver.MapPreviewLoadoutToGameplayIds(tulparDefault);
            Assert.That(mapped, Is.EqualTo(new ushort[] { 1, 20, 10, 0, 0 }));

            var catalog = new FakeCatalogLookup();
            ushort[] fallback = { 10, 11, 12, 13, 20 };
            ushort[] normalized = MatchLoadoutHandoffResolver.NormalizeToValidDeck(mapped, catalog, fallback);

            LoadoutValidationResult validation = LoadoutValidator.Validate(normalized, catalog);
            Assert.That(validation.IsValid, Is.True, $"Validation failed: {validation.Error}");
            Assert.That(normalized[0], Is.EqualTo(1));
            Assert.That(normalized[1], Is.EqualTo(20));
            Assert.That(normalized[2], Is.EqualTo(10));
            Assert.That(normalized[3], Is.EqualTo(11));
            Assert.That(normalized[4], Is.EqualTo(12));
        }

        [Test]
        public void MatchLaunchConfigStoresPerSeatAndOnlineLocalPresetsAndClearsOnReset()
        {
            MatchLaunchConfig.SelectMode(MatchMode.LocalPvP);
            MatchLaunchConfig.SetPresetLoadout(0, new ushort[] { 1, 20, 10, 11, 12 });
            MatchLaunchConfig.SetPresetLoadout(1, new ushort[] { 1, 10, 12, 13, 22 });
            MatchLaunchConfig.SetLocalOnlinePresetLoadout(new ushort[] { 1, 20, 10, 0, 0 });

            Assert.That(MatchLaunchConfig.HasAnyPresetLoadout, Is.True);
            Assert.That(MatchLaunchConfig.TryGetPresetLoadout(0, out ushort[] p0), Is.True);
            Assert.That(p0, Is.EqualTo(new ushort[] { 1, 20, 10, 11, 12 }));
            Assert.That(MatchLaunchConfig.TryGetPresetLoadout(1, out ushort[] p1), Is.True);
            Assert.That(p1, Is.EqualTo(new ushort[] { 1, 10, 12, 13, 22 }));
            Assert.That(MatchLaunchConfig.TryGetLocalOnlinePresetLoadout(out ushort[] online), Is.True);
            Assert.That(online, Is.EqualTo(new ushort[] { 1, 20, 10, 0, 0 }));

            MatchLaunchConfig.Reset();
            Assert.That(MatchLaunchConfig.HasAnyPresetLoadout, Is.False);
            Assert.That(MatchLaunchConfig.TryGetPresetLoadout(0, out _), Is.False);
            Assert.That(MatchLaunchConfig.TryGetLocalOnlinePresetLoadout(out _), Is.False);
        }

        [Test]
        public void HudBuilderIsolatesPerSeatShopsDualGoldHintsAndOnlineVisibility()
        {
            ArenaHudCardEntry[] deckA =
            {
                new(1, "Territory Balloon", 75),
                new(10, "Cannon", 100),
                new(11, "Shield Generator", 120),
                new(12, "Flak Tower", 110),
                new(20, "Gold Mine", 80)
            };
            ArenaHudCardEntry[] deckB =
            {
                new(1, "Territory Balloon", 75),
                new(20, "Gold Mine", 80),
                new(21, "Heavy Mortar", 175),
                new(22, "Laser Tower", 200),
                new(10, "Cannon", 100)
            };

            var snapshot = new ArenaHudSnapshot
            {
                Phase = MatchPhase.Playing,
                BlueHqCurrent = 350f,
                BlueHqMax = 500f,
                RedHqCurrent = 420f,
                RedHqMax = 500f
            };
            snapshot.LeftSeat.Exists = true;
            snapshot.LeftSeat.IsLocallyOwned = true;
            snapshot.LeftSeat.Gold = 250;
            snapshot.LeftSeat.IncomePerSecond = 10f;
            snapshot.LeftSeat.DeckCards = deckA;
            snapshot.LeftSeat.IsDockedAtBase = false;

            snapshot.RightSeat.Exists = true;
            snapshot.RightSeat.IsLocallyOwned = true;
            snapshot.RightSeat.Gold = 90;
            snapshot.RightSeat.IncomePerSecond = 12f;
            snapshot.RightSeat.DeckCards = deckB;
            snapshot.RightSeat.IsDockedAtBase = true;
            snapshot.RightSeat.SelectedDeckSlot = 1; // Gold Mine (80g)

            // Case 1: Only P2 docked -> only Right shop opens with P2 bindings.
            ArenaBattleHudState p2Only = ArenaBattleHudStateBuilder.Build(snapshot);
            Assert.That(p2Only.BlueHqCurrent, Is.EqualTo(350));
            Assert.That(p2Only.RedHqCurrent, Is.EqualTo(420));
            Assert.That(p2Only.LeftSeat.ShowGoldPanel, Is.True);
            Assert.That(p2Only.LeftSeat.GoldText, Is.EqualTo("250"));
            Assert.That(p2Only.LeftSeat.IncomeText, Is.EqualTo("+600/m"));
            Assert.That(p2Only.LeftSeat.ShowShopPanel, Is.False);
            Assert.That(p2Only.RightSeat.ShowGoldPanel, Is.True);
            Assert.That(p2Only.RightSeat.GoldText, Is.EqualTo("90"));
            Assert.That(p2Only.RightSeat.ShowShopPanel, Is.True);
            Assert.That(p2Only.RightSeat.CanBuySelected, Is.True);
            Assert.That(p2Only.RightSeat.SelectHint, Is.EqualTo("←/→ Select"));
            Assert.That(p2Only.RightSeat.BuyHint, Is.EqualTo("RCtrl  BUY"));
            Assert.That(p2Only.RightSeat.LeaveHint, Is.EqualTo("Enter  LEAVE"));
            Assert.That(p2Only.RightSeat.Cards[1].Title, Is.EqualTo("Gold Mine"));

            // Case 2: Both docked simultaneously -> independent slots and affordability.
            snapshot.LeftSeat.IsDockedAtBase = true;
            snapshot.LeftSeat.SelectedDeckSlot = 2; // Shield Generator (120g)
            snapshot.RightSeat.SelectedDeckSlot = 2; // Heavy Mortar (175g > 90g)
            ArenaBattleHudState dualDocked = ArenaBattleHudStateBuilder.Build(snapshot);
            Assert.That(dualDocked.LeftSeat.ShowShopPanel, Is.True);
            Assert.That(dualDocked.LeftSeat.CanBuySelected, Is.True);
            Assert.That(dualDocked.LeftSeat.BuyHint, Is.EqualTo("Space  BUY"));
            Assert.That(dualDocked.LeftSeat.Cards[2].Title, Is.EqualTo("Shield Generator"));
            Assert.That(dualDocked.RightSeat.ShowShopPanel, Is.True);
            Assert.That(dualDocked.RightSeat.CanBuySelected, Is.False);
            Assert.That(dualDocked.RightSeat.Cards[2].Title, Is.EqualTo("Heavy Mortar"));

            // Case 3: Online PvP where only Right seat (Red) is locally owned -> hides Left gold, uses single-player hints.
            snapshot.LeftSeat.IsLocallyOwned = false;
            snapshot.LeftSeat.IsDockedAtBase = false;
            snapshot.RightSeat.IsDockedAtBase = false;
            snapshot.RightSeat.IsCarrying = true;
            snapshot.RightSeat.CarriedCardName = "Cannon";
            snapshot.RightSeat.CarriedReservedGold = 100;
            ArenaBattleHudState onlineRed = ArenaBattleHudStateBuilder.Build(snapshot);
            Assert.That(onlineRed.LeftSeat.ShowGoldPanel, Is.False);
            Assert.That(onlineRed.RightSeat.ShowGoldPanel, Is.True);
            Assert.That(onlineRed.RightSeat.ShowCarryPanel, Is.True);
            Assert.That(onlineRed.RightSeat.DeployLabel, Is.EqualTo("Space  DEPLOY CANNON"));
            Assert.That(onlineRed.RightSeat.CancelLabel, Is.EqualTo("Q  CANCEL (+100g)"));

            // Case 4: Victory vs Defeat for single-local-seat Red player.
            snapshot.Phase = MatchPhase.Ended;
            snapshot.WinnerTeam = TeamId.TeamA;
            snapshot.OutcomeReason = "HQ Destroyed";
            Assert.That(ArenaBattleHudStateBuilder.ShouldShowVictoryScreen(snapshot), Is.False);
            snapshot.WinnerTeam = TeamId.TeamB;
            Assert.That(ArenaBattleHudStateBuilder.ShouldShowVictoryScreen(snapshot), Is.True);
            Assert.That(ArenaBattleHudStateBuilder.FormatResultHeader(snapshot), Is.EqualTo("RED TEAM VICTORY — HQ Destroyed"));
        }
    }
}
