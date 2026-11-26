using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ArenaUnifiedUiTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchLaunchConfig.Reset();
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = true;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.name == GameSceneNames.CannonBrawlArena)
            {
                Scene teardown = SceneManager.CreateScene("ArenaUiTeardown_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(teardown);
                yield return SceneManager.UnloadSceneAsync(active);
            }

            yield return null;
        }

        private static IEnumerator LoadLocalPvPArenaWithPresets(ushort[] p1Deck = null, ushort[] p2Deck = null)
        {
            if (!Application.CanStreamedLevelBeLoaded(GameSceneNames.CannonBrawlArena))
            {
                Assert.Ignore("CannonBrawlArena Build Settings içinde bulunamadı.");
            }

            MatchLaunchConfig.Reset();
            MatchLaunchConfig.SelectMode(MatchMode.LocalPvP);
            MatchLaunchConfig.SetPresetLoadout(0, p1Deck ?? new ushort[] { 1, 20, 10, 11, 12 });
            MatchLaunchConfig.SetPresetLoadout(1, p2Deck ?? new ushort[] { 1, 10, 20, 11, 12 });

            yield return SceneManager.LoadSceneAsync(GameSceneNames.CannonBrawlArena, LoadSceneMode.Single);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalPvPPresetsEnterPlayingDirectlyPopulateBattleHudTogglePauseShowVictoryAndRematch()
        {
            yield return LoadLocalPvPArenaWithPresets();

            ArenaSessionBootstrap bootstrap = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
            ArenaBattleUiController uiController = Object.FindFirstObjectByType<ArenaBattleUiController>();
            Assert.That(bootstrap, Is.Not.Null, "ArenaSessionBootstrap missing in CannonBrawlArena.");
            Assert.That(uiController, Is.Not.Null, "ArenaBattleUiController missing in CannonBrawlArena. Run Setup MainMenu & CannonBrawlArena.");
            Assert.That(uiController.HudView, Is.Not.Null, "ArenaBattleHudView reference missing on ArenaBattleUiController.");

            yield return Until(
                () => bootstrap.IsMatchInitialized && bootstrap.CurrentPhase == MatchPhase.Playing,
                10f,
                "Arena with preset loadouts did not auto-submit and transition straight to Playing.");

            yield return null;

            Assert.That(uiController.HudView.LeftGoldRoot.activeSelf, Is.True);
            Assert.That(uiController.HudView.RightGoldRoot.activeSelf, Is.True);
            Assert.That(uiController.HudView.LeftGoldLabel.text, Is.Not.Empty);
            Assert.That(uiController.HudView.RightGoldLabel.text, Is.Not.Empty);
            Assert.That(uiController.HudView.BlueHqLabel.text, Does.Contain("BLUE HQ"));
            Assert.That(uiController.HudView.RedHqLabel.text, Does.Contain("RED HQ"));

            // EventSystem navigation must be suppressed during live play so WASD/Arrows/Enter/Space cannot hijack HUD buttons.
            if (EventSystem.current != null)
            {
                Assert.That(EventSystem.current.sendNavigationEvents, Is.False);
            }

            // Pause opens cleanly when idle.
            Assert.That(uiController.TryTogglePauseFromUserInput(), Is.True);
            yield return null;
            Assert.That(uiController.IsPaused, Is.True);
            Assert.That(uiController.PauseView.gameObject.activeSelf, Is.True);

            Assert.That(uiController.TryTogglePauseFromUserInput(), Is.True);
            yield return null;
            Assert.That(uiController.IsPaused, Is.False);
            Assert.That(uiController.PauseView.gameObject.activeSelf, Is.False);

            // Pause must be blocked while carrying a structure so Escape cancels carry first.
            Assert.That(bootstrap.Context.TryGetAirship(0, out AirshipController ship0), Is.True);
            ship0.transform.position = new Vector3(-11.5f, 0.2f, 0f);
            yield return null;
            bootstrap.RequestToggleDock(0);
            yield return Until(() => ship0.Docking.IsDockedAtBase, 5f, "Seat 0 did not dock at base.");
            bootstrap.RequestConfirmDockedSelection(0);
            yield return Until(() => ship0.IsCarrying, 5f, "Seat 0 did not begin carrying.");

            Assert.That(uiController.TryTogglePauseFromUserInput(), Is.False);
            Assert.That(uiController.IsPaused, Is.False);

            // Destroying Team B HQ opens Tulpar Victory screen with replay enabled for Local PvP.
            Assert.That(bootstrap.Context.TryGetHq(TeamId.TeamB, out IStructure hqB), Is.True);
            hqB.ApplyDamageServer(new DamageInfo(9999f, TeamId.TeamA, 0, Vector2.zero));

            yield return Until(
                () => bootstrap.CurrentPhase == MatchPhase.Ended
                      && uiController.VictoryView != null
                      && uiController.VictoryView.gameObject.activeSelf,
                5f,
                "Victory overlay did not appear on HQ destruction.");

            Assert.That(uiController.VictoryView.Item("stars").activeSelf, Is.False);
            Assert.That(uiController.VictoryView.Item("reward").activeSelf, Is.False);
            Assert.That(uiController.VictoryView.Item("replay").activeSelf, Is.True);

            // Rematch preserves presets and launches straight back into Playing without DraftLoadoutUI.
            bootstrap.ReloadArenaForRematch();
            yield return Until(
                () =>
                {
                    ArenaSessionBootstrap reloaded = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
                    return reloaded != null && reloaded != bootstrap && reloaded.IsMatchInitialized && reloaded.CurrentPhase == MatchPhase.Playing;
                },
                10f,
                "Rematch did not preserve presets and auto-enter Playing.");
        }

        [UnityTest]
        public IEnumerator Player2DockOpensOnlyRightShopHidesWorldBaseUiAndPurchasesWithoutAffectingPlayer1()
        {
            yield return LoadLocalPvPArenaWithPresets(
                p1Deck: new ushort[] { 1, 10, 11, 12, 20 },
                p2Deck: new ushort[] { 1, 20, 10, 11, 12 });

            ArenaSessionBootstrap bootstrap = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
            ArenaBattleUiController uiController = Object.FindFirstObjectByType<ArenaBattleUiController>();
            DockedStructureWorldUI worldUi = Object.FindFirstObjectByType<DockedStructureWorldUI>();
            Assert.That(worldUi, Is.Not.Null);

            yield return Until(
                () => bootstrap.IsMatchInitialized && bootstrap.CurrentPhase == MatchPhase.Playing,
                10f,
                "Arena did not enter Playing.");

            Assert.That(bootstrap.Context.TryGetAirship(0, out AirshipController ship0), Is.True);
            Assert.That(bootstrap.Context.TryGetAirship(1, out AirshipController ship1), Is.True);

            // Dock ONLY P2 at Red HQ.
            ship1.transform.position = new Vector3(11.5f, 0.2f, 0f);
            yield return null;
            bootstrap.RequestToggleDock(1);
            yield return Until(() => ship1.Docking.IsDockedAtBase, 5f, "Seat 1 did not dock at Red HQ.");
            yield return null;

            Assert.That(uiController.HudView.RightShopPanel.gameObject.activeSelf, Is.True);
            Assert.That(uiController.HudView.LeftShopPanel.gameObject.activeSelf, Is.False);
            Assert.That(worldUi.IsPromptVisibleForSeat(1), Is.False, "DockedStructureWorldUI must not duplicate Base shop.");

            RectTransform rightRect = (RectTransform)uiController.HudView.RightShopPanel.transform;
            Assert.That(rightRect.position.x, Is.GreaterThan(Screen.width * 0.5f), "P2 shop must be on the right side of the screen.");

            // Select slot 1 (Gold Mine = 80g) and buy via P2's RightShopPanel UI buttons directly.
            uiController.HudView.RightShopPanel.Slots[1].button.onClick.Invoke();
            yield return Until(() => ship1.Docking.State.SelectedDeckSlot == 1, 4f, "P2 did not select slot 1 via UI.");
            yield return null;

            Assert.That(bootstrap.TryGetSeatEconomyInfo(0, out _, out _, out _, out int p1GoldBefore, out _), Is.True);
            Assert.That(bootstrap.TryGetSeatEconomyInfo(1, out _, out _, out _, out int p2GoldBefore, out _), Is.True);

            uiController.HudView.RightShopPanel.BuyButton.onClick.Invoke();
            yield return Until(() => ship1.IsCarrying && ship1.CarryState.CardId == 20, 5f, "P2 did not purchase Gold Mine via UI.");

            Assert.That(bootstrap.TryGetSeatEconomyInfo(0, out _, out _, out _, out int p1GoldAfter, out _), Is.True);
            Assert.That(bootstrap.TryGetSeatEconomyInfo(1, out _, out _, out _, out int p2GoldAfter, out _), Is.True);
            // Allow minor passive base income ticks across WaitUntil frames.
            Assert.That(p2GoldAfter, Is.InRange(p2GoldBefore - 80, p2GoldBefore - 80 + 10), "P2 gold should decrease by Gold Mine cost (80g).");
            Assert.That(p1GoldAfter, Is.InRange(p1GoldBefore, p1GoldBefore + 10), "P1 gold must remain untouched by P2 purchase.");
            Assert.That(ship0.IsCarrying, Is.False, "P1 airship must not carry P2's purchased card.");
            Assert.That(ship0.Docking.IsDocked, Is.False);
        }

        [UnityTest]
        public IEnumerator SimultaneousDualBaseDockingAllowsIndependentCardSelectionAndPurchase()
        {
            yield return LoadLocalPvPArenaWithPresets(
                p1Deck: new ushort[] { 1, 10, 11, 12, 20 },
                p2Deck: new ushort[] { 1, 20, 10, 11, 12 });

            ArenaSessionBootstrap bootstrap = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
            ArenaBattleUiController uiController = Object.FindFirstObjectByType<ArenaBattleUiController>();

            yield return Until(
                () => bootstrap.IsMatchInitialized && bootstrap.CurrentPhase == MatchPhase.Playing,
                10f,
                "Arena did not enter Playing.");

            Assert.That(bootstrap.Context.TryGetAirship(0, out AirshipController ship0), Is.True);
            Assert.That(bootstrap.Context.TryGetAirship(1, out AirshipController ship1), Is.True);

            ship0.transform.position = new Vector3(-11.5f, 0.2f, 0f);
            ship1.transform.position = new Vector3(11.5f, 0.2f, 0f);
            yield return null;

            bootstrap.RequestToggleDock(0);
            bootstrap.RequestToggleDock(1);
            yield return Until(() => ship0.Docking.IsDockedAtBase && ship1.Docking.IsDockedAtBase, 5f, "Both players did not dock simultaneously.");
            yield return null;

            Assert.That(uiController.HudView.LeftShopPanel.gameObject.activeSelf, Is.True);
            Assert.That(uiController.HudView.RightShopPanel.gameObject.activeSelf, Is.True);

            // P1 clicks Slot 2 (Shield Generator, 120g) in LeftShopPanel; P2 clicks Slot 1 (Gold Mine, 80g) in RightShopPanel.
            uiController.HudView.LeftShopPanel.Slots[2].button.onClick.Invoke();
            uiController.HudView.RightShopPanel.Slots[1].button.onClick.Invoke();
            yield return Until(
                () => ship0.Docking.State.SelectedDeckSlot == 2 && ship1.Docking.State.SelectedDeckSlot == 1,
                5f,
                "Independent slot selection failed.");

            Assert.That(bootstrap.TryGetSeatEconomyInfo(0, out _, out _, out _, out int p1GoldBefore, out _), Is.True);
            Assert.That(bootstrap.TryGetSeatEconomyInfo(1, out _, out _, out _, out int p2GoldBefore, out _), Is.True);

            uiController.HudView.LeftShopPanel.BuyButton.onClick.Invoke();
            uiController.HudView.RightShopPanel.BuyButton.onClick.Invoke();

            yield return Until(
                () => ship0.IsCarrying && ship0.CarryState.CardId == 11
                      && ship1.IsCarrying && ship1.CarryState.CardId == 20,
                5f,
                "Simultaneous independent purchases failed.");

            Assert.That(bootstrap.TryGetSeatEconomyInfo(0, out _, out _, out _, out int p1GoldAfter, out _), Is.True);
            Assert.That(bootstrap.TryGetSeatEconomyInfo(1, out _, out _, out _, out int p2GoldAfter, out _), Is.True);
            // Allow minor passive base income ticks across WaitUntil frames.
            Assert.That(p1GoldAfter, Is.InRange(p1GoldBefore - 120, p1GoldBefore - 120 + 10));
            Assert.That(p2GoldAfter, Is.InRange(p2GoldBefore - 80, p2GoldBefore - 80 + 10));
        }

        [UnityTest]
        public IEnumerator TopHudLayoutPositionsLeftGoldCenterPauseRightGoldWithoutDuplicateHqHealthBar()
        {
            yield return LoadLocalPvPArenaWithPresets();

            ArenaSessionBootstrap bootstrap = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
            ArenaBattleUiController uiController = Object.FindFirstObjectByType<ArenaBattleUiController>();

            yield return Until(
                () => bootstrap.IsMatchInitialized && bootstrap.CurrentPhase == MatchPhase.Playing,
                10f,
                "Arena did not enter Playing.");
            yield return null;

            Canvas.ForceUpdateCanvases();

            var leftGoldRect = (RectTransform)uiController.HudView.LeftGoldRoot.transform;
            var pauseRect = (RectTransform)uiController.HudView.PauseButton.transform;
            var rightGoldRect = (RectTransform)uiController.HudView.RightGoldRoot.transform;

            Assert.That(leftGoldRect.position.x, Is.LessThan(pauseRect.position.x));
            Assert.That(pauseRect.position.x, Is.LessThan(rightGoldRect.position.x));
            Assert.That(Mathf.Abs(pauseRect.position.x - (Screen.width * 0.5f)), Is.LessThan(4f), "Pause button must be top-centered.");

            Assert.That(uiController.HudView.BlueHqBarRoot.activeSelf, Is.True);
            Assert.That(uiController.HudView.RedHqBarRoot.activeSelf, Is.True);

            // Core HQs must NOT render a duplicate WorldHealthBarVisual over the HQ.
            Assert.That(bootstrap.Context.TryGetHq(TeamId.TeamA, out IStructure hqA), Is.True);
            Assert.That(bootstrap.Context.TryGetHq(TeamId.TeamB, out IStructure hqB), Is.True);
            var hqAComp = hqA as MonoBehaviour;
            var hqBComp = hqB as MonoBehaviour;
            Assert.That(hqAComp.transform.Find("StructureHealthBar"), Is.Null);
            Assert.That(hqBComp.transform.Find("StructureHealthBar"), Is.Null);
        }

        private static IEnumerator Until(Func<bool> condition, float timeoutSeconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, message);
        }
    }
}
