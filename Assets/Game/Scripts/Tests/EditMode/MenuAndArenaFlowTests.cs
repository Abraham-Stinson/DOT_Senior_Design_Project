using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class MenuAndArenaFlowTests
    {
        [TearDown]
        public void TearDown()
        {
            MatchLaunchConfig.Reset();
        }

        [Test]
        public void MainMenuRouterDirectsLocalPvPToCannonBrawlArena()
        {
            MenuRouteDecision decision = MainMenuRouter.Resolve(MatchMode.LocalPvP);

            Assert.That(decision.IsEnabled, Is.True);
            Assert.That(decision.Action, Is.EqualTo(MenuNavigationAction.LoadArenaLocalPvP));
            Assert.That(decision.TargetScene, Is.EqualTo(GameSceneNames.CannonBrawlArena));
            Assert.That(MainMenuRouter.CanLaunch(MatchMode.LocalPvP), Is.True);
        }

        [Test]
        public void MainMenuRouterDirectsOnlinePvPToSteamNetworkTestScene()
        {
            MenuRouteDecision decision = MainMenuRouter.Resolve(MatchMode.OnlinePvP);

            Assert.That(decision.IsEnabled, Is.True);
            Assert.That(decision.Action, Is.EqualTo(MenuNavigationAction.LoadSteamOnlineLobby));
            Assert.That(decision.TargetScene, Is.EqualTo(GameSceneNames.SteamOnlineLobby));
            Assert.That(MainMenuRouter.CanLaunch(MatchMode.OnlinePvP), Is.True);
        }

        [Test]
        public void MainMenuRouterBlocksStoryModeAsWorkInProgress()
        {
            MenuRouteDecision decision = MainMenuRouter.Resolve(MatchMode.Story);

            Assert.That(decision.IsEnabled, Is.False);
            Assert.That(decision.Action, Is.EqualTo(MenuNavigationAction.BlockedWorkInProgress));
            Assert.That(decision.TargetScene, Is.Empty);
            Assert.That(MainMenuRouter.CanLaunch(MatchMode.Story), Is.False);
        }

        [Test]
        public void MatchLaunchConfigTracksSelectionAndResetsCleanly()
        {
            Assert.That(MatchLaunchConfig.HasExplicitSelection, Is.False);
            Assert.That(MatchLaunchConfig.SelectedMode, Is.EqualTo(MatchMode.LocalPvP));

            MatchLaunchConfig.SelectMode(MatchMode.OnlinePvP);
            Assert.That(MatchLaunchConfig.HasExplicitSelection, Is.True);
            Assert.That(MatchLaunchConfig.SelectedMode, Is.EqualTo(MatchMode.OnlinePvP));

            MatchLaunchConfig.Reset();
            Assert.That(MatchLaunchConfig.HasExplicitSelection, Is.False);
            Assert.That(MatchLaunchConfig.SelectedMode, Is.EqualTo(MatchMode.LocalPvP));
        }

        [Test]
        public void ArenaLayoutConfigResolvesTeamSpawnAnchorsAndValidatesLayout()
        {
            var root = new GameObject("ArenaLayoutTestRoot");
            try
            {
                var layout = root.AddComponent<ArenaLayoutConfig>();
                Assert.That(layout.IsValidLayout(), Is.False);

                Transform aHq = CreateChild(root.transform, "AHq", new Vector3(-12f, 0f, 0f));
                Transform aShip = CreateChild(root.transform, "AShip", new Vector3(-11f, 4f, 0f));
                Transform aTerr = CreateChild(root.transform, "ATerr", new Vector3(-12f, 0f, 0f));
                Transform bHq = CreateChild(root.transform, "BHq", new Vector3(12f, 0f, 0f));
                Transform bShip = CreateChild(root.transform, "BShip", new Vector3(11f, 4f, 0f));
                Transform bTerr = CreateChild(root.transform, "BTerr", new Vector3(12f, 0f, 0f));
                Transform center = CreateChild(root.transform, "Center", Vector3.zero);

                layout.ConfigureForSetup(
                    new Rect(-18f, -2f, 36f, 16f),
                    aHq, aShip, aTerr,
                    bHq, bShip, bTerr,
                    center);

                Assert.That(layout.IsValidLayout(), Is.True);
                Assert.That(layout.GetHqSpawn(TeamId.TeamA), Is.EqualTo(aHq));
                Assert.That(layout.GetAirshipSpawn(TeamId.TeamA), Is.EqualTo(aShip));
                Assert.That(layout.GetTerritoryOrigin(TeamId.TeamA), Is.EqualTo(aTerr));
                Assert.That(layout.GetHqSpawn(TeamId.TeamB), Is.EqualTo(bHq));
                Assert.That(layout.GetAirshipSpawn(TeamId.TeamB), Is.EqualTo(bShip));
                Assert.That(layout.GetTerritoryOrigin(TeamId.TeamB), Is.EqualTo(bTerr));
                Assert.That(layout.GetHqSpawn(TeamId.Neutral), Is.Null);
                Assert.That(layout.CenterNeutralNode, Is.EqualTo(center));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform CreateChild(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go.transform;
        }
    }
}
