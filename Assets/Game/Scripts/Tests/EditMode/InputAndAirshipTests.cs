using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Input;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class InputAndAirshipTests
    {
        private sealed class FakeDockable : MonoBehaviour, IDockable
        {
            public TeamId TeamValue;
            public Vector2 Anchor;
            public bool AllowDock = true;

            public TeamId Team => TeamValue;
            public Vector2 DockAnchorWorld => Anchor;

            public bool CanDock(byte seatId, TeamId team) => AllowDock && team == TeamValue;
            public void OnAirshipDockedServer(byte seatId) { }
            public void OnAirshipUndockedServer(byte seatId) { }
        }

        [Test]
        public void InputChannelLatchesEdgeActionsUntilConsumedByTick()
        {
            var channel = new PlayerInputChannel(0);

            channel.UpdateFromFrame(new PlayerInputSnapshot
            {
                Move = new Vector2(1f, 0f),
                DockPressed = true,
                SelectedBuildSlot = 2,
                FireHeld = true
            });

            // A subsequent frame with DockPressed = false must NOT wipe the latched press before OnTick.
            channel.UpdateFromFrame(new PlayerInputSnapshot
            {
                Move = new Vector2(0.5f, 0f),
                DockPressed = false,
                SelectedBuildSlot = -1,
                FireHeld = false
            });

            PlayerInputSnapshot tick = channel.ConsumeTickSnapshot();
            Assert.That(tick.Move.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(tick.DockPressed, Is.True);
            Assert.That(tick.SelectedBuildSlot, Is.EqualTo(2));
            Assert.That(tick.FireHeld, Is.False);

            PlayerInputSnapshot nextTick = channel.ConsumeTickSnapshot();
            Assert.That(nextTick.DockPressed, Is.False);
            Assert.That(nextTick.SelectedBuildSlot, Is.EqualTo(-1));
        }

        [Test]
        public void InputRouterRoutesSeatsDifferentlyInSingleVsLocalDualModes()
        {
            var go = new GameObject("InputRouterTest");
            try
            {
                var router = go.AddComponent<PlayerInputRouter>();
                router.EnableHardwarePolling = false;

                router.ConfigureMode(InputRoutingMode.LocalDualPlayer);
                router.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = new Vector2(-1f, 0f), SelectedBuildSlot = 1 });
                router.InjectSyntheticInput(1, new PlayerInputSnapshot { Move = new Vector2(1f, 0f), DockPressed = true, SelectedBuildSlot = 4 });

                PlayerInputSnapshot p1 = router.GetChannelForSeat(0).ConsumeTickSnapshot();
                PlayerInputSnapshot p2 = router.GetChannelForSeat(1).ConsumeTickSnapshot();

                Assert.That(p1.Move.x, Is.EqualTo(-1f));
                Assert.That(p1.DockPressed, Is.False);
                Assert.That(p1.SelectedBuildSlot, Is.EqualTo(1));

                Assert.That(p2.Move.x, Is.EqualTo(1f));
                Assert.That(p2.DockPressed, Is.True);
                Assert.That(p2.SelectedBuildSlot, Is.EqualTo(4));

                // In SinglePlayer (Online PvP), any locally owned seat (even Seat 1 on remote client) reads Channel 0.
                router.ConfigureMode(InputRoutingMode.SinglePlayer);
                router.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = new Vector2(0f, 1f), DockPressed = true, SelectedBuildSlot = -1 });
                PlayerInputSnapshot onlineLocal = router.GetChannelForSeat(1).ConsumeTickSnapshot();
                Assert.That(onlineLocal.Move.y, Is.EqualTo(1f));
                Assert.That(onlineLocal.DockPressed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AirshipKinematicsAcceleratesDeceleratesAndClampsToPlayableBounds()
        {
            var bounds = new Rect(-10f, -2f, 20f, 12f);
            Vector2 pos = Vector2.zero;
            Vector2 vel = Vector2.zero;

            for (int i = 0; i < 30; i++)
            {
                AirshipKinematics.Step2D(ref pos, ref vel, new Vector2(10f, 10f), 10f, 40f, 20f, 0.1f, bounds);
            }

            Assert.That(pos.x, Is.EqualTo(bounds.xMax).Within(0.001f));
            Assert.That(pos.y, Is.EqualTo(bounds.yMax).Within(0.001f));
            Assert.That(vel.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(vel.y, Is.EqualTo(0f).Within(0.001f));

            // Verify drag brings velocity smoothly back to zero when input ceases.
            vel = new Vector2(-8f, 0f);
            pos = Vector2.zero;
            for (int i = 0; i < 20; i++)
            {
                AirshipKinematics.Step2D(ref pos, ref vel, Vector2.zero, 10f, 40f, 15f, 0.05f, bounds);
            }

            Assert.That(vel.sqrMagnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void DockingEvaluatorPicksNearestAlliedCandidateAndDetectsDestroyedTargets()
        {
            var allyNearGo = new GameObject("AllyNear");
            var allyFarGo = new GameObject("AllyFar");
            var enemyNearGo = new GameObject("EnemyNear");

            try
            {
                var allyNear = allyNearGo.AddComponent<FakeDockable>();
                allyNear.TeamValue = TeamId.TeamA;
                allyNear.Anchor = new Vector2(2f, 0f);

                var allyFar = allyFarGo.AddComponent<FakeDockable>();
                allyFar.TeamValue = TeamId.TeamA;
                allyFar.Anchor = new Vector2(3.2f, 0f);

                var enemyNear = enemyNearGo.AddComponent<FakeDockable>();
                enemyNear.TeamValue = TeamId.TeamB;
                enemyNear.Anchor = new Vector2(0.5f, 0f);

                var candidates = new List<DockableRegistration>
                {
                    new(enemyNear, enemyNear, null),
                    new(allyFar, allyFar, null),
                    new(allyNear, allyNear, null)
                };

                bool found = DockingEvaluator.TryFindBestDockable(
                    Vector2.zero,
                    0,
                    TeamId.TeamA,
                    3.5f,
                    candidates,
                    out DockableRegistration best);

                Assert.That(found, Is.True);
                Assert.That(best.Dockable, Is.EqualTo(allyNear));
                Assert.That(DockingEvaluator.IsTargetStillValid(best), Is.True);

                // Out of range should fail.
                bool foundFar = DockingEvaluator.TryFindBestDockable(
                    new Vector2(-10f, 0f),
                    0,
                    TeamId.TeamA,
                    3.5f,
                    candidates,
                    out _);
                Assert.That(foundFar, Is.False);

                // Destroying the target must invalidate it so server auto-undocks.
                Object.DestroyImmediate(allyNearGo);
                Assert.That(DockingEvaluator.IsTargetStillValid(best), Is.False);
            }
            finally
            {
                if (allyNearGo != null) Object.DestroyImmediate(allyNearGo);
                if (allyFarGo != null) Object.DestroyImmediate(allyFarGo);
                if (enemyNearGo != null) Object.DestroyImmediate(enemyNearGo);
            }
        }
    }
}
