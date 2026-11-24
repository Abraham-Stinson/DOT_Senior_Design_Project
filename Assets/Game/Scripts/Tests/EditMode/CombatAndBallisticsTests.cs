using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class CombatAndBallisticsTests
    {
        [Test]
        public void Ballistics2D_TrySolveLaunchVelocity_HitsTargetUnderGravity()
        {
            Vector2 origin = new(-10f, 0f);
            Vector2 target = new(8f, 0.5f);
            const float speed = 19f;
            const float gravity = Ballistics2D.DefaultGravity;

            bool solved = Ballistics2D.TrySolveLaunchVelocity(
                origin,
                target,
                speed,
                gravity,
                preferHighArc: false,
                minAngleDegrees: 5f,
                maxAngleDegrees: 85f,
                out Vector2 velocity,
                out float angleDeg);

            Assert.That(solved, Is.True);
            Assert.That(angleDeg, Is.GreaterThan(0f).And.LessThan(85f));

            Vector2 pos = origin;
            Vector2 vel = velocity;
            float minDistance = float.MaxValue;
            const float dt = 1f / 60f;
            for (int i = 0; i < 300; i++)
            {
                Ballistics2D.StepProjectile(ref pos, ref vel, gravity, dt);
                float dist = Vector2.Distance(pos, target);
                if (dist < minDistance)
                {
                    minDistance = dist;
                }
            }

            Assert.That(minDistance, Is.LessThan(0.35f), $"Simulated trajectory missed target (closest={minDistance}).");
        }

        [Test]
        public void Ballistics2D_ComputeSplashDamage_ScalesWithDistanceAndZeroOutsideRadius()
        {
            Assert.That(Ballistics2D.ComputeSplashDamage(100f, 0f, 4f), Is.EqualTo(100f).Within(0.01f));
            Assert.That(Ballistics2D.ComputeSplashDamage(100f, 2f, 4f), Is.EqualTo(67.5f).Within(0.01f));
            Assert.That(Ballistics2D.ComputeSplashDamage(100f, 4f, 4f), Is.EqualTo(35f).Within(0.01f));
            Assert.That(Ballistics2D.ComputeSplashDamage(100f, 4.1f, 4f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Ballistics2D_SegmentIntersectsRectAndCircle_DetectsTunnelingFreeHits()
        {
            bool rectHit = Ballistics2D.TrySegmentIntersectsRect(
                new Vector2(-4f, 1f),
                new Vector2(4f, 1f),
                new Vector2(0f, 0f),
                new Vector2(2f, 2f),
                out float rectT,
                out Vector2 rectPoint);

            Assert.That(rectHit, Is.True);
            Assert.That(rectT, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(rectPoint.x, Is.EqualTo(0f).Within(0.01f));

            bool circleHit = Ballistics2D.TrySegmentIntersectsCircle(
                new Vector2(-3f, 0f),
                new Vector2(3f, 0f),
                new Vector2(1f, 0f),
                1f,
                out float circleT,
                out Vector2 circlePoint);

            Assert.That(circleHit, Is.True);
            Assert.That(circleT, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(circlePoint.x, Is.EqualTo(0f).Within(0.01f));

            bool miss = Ballistics2D.TrySegmentIntersectsCircle(
                new Vector2(-3f, 3f),
                new Vector2(3f, 3f),
                new Vector2(1f, 0f),
                1f,
                out _,
                out _);
            Assert.That(miss, Is.False);
        }

        [Test]
        public void WeaponTargetEvaluator_RespectsRangeTeamAndAirshipFilter()
        {
            var candidates = new List<CombatTargetCandidate>
            {
                new(TeamId.TeamA, isAlive: true, isAirship: false, new Vector2(2f, 0f), targetIndex: 0), // Ally
                new(TeamId.TeamB, isAlive: true, isAirship: true, new Vector2(3f, 4f), targetIndex: 1),  // Airship
                new(TeamId.TeamB, isAlive: false, isAirship: false, new Vector2(4f, 0f), targetIndex: 2), // Dead
                new(TeamId.TeamB, isAlive: true, isAirship: false, new Vector2(12f, 0f), targetIndex: 3), // Valid structure
                new(TeamId.TeamB, isAlive: true, isAirship: false, new Vector2(35f, 0f), targetIndex: 4)  // Out of range
            };

            var cannonConfig = new WeaponConfig
            {
                CanFire = true,
                Range = 20f,
                CanTargetStructures = true,
                CanTargetAirships = false
            };

            bool foundCannonTarget = WeaponTargetEvaluator.TrySelectBestTarget(
                Vector2.zero,
                TeamId.TeamA,
                cannonConfig,
                candidates,
                out CombatTargetCandidate cannonBest);

            Assert.That(foundCannonTarget, Is.True);
            Assert.That(cannonBest.TargetIndex, Is.EqualTo(3));

            var flakConfig = new WeaponConfig
            {
                CanFire = true,
                Range = 20f,
                CanTargetStructures = true,
                CanTargetAirships = true
            };

            bool foundFlakTarget = WeaponTargetEvaluator.TrySelectBestTarget(
                Vector2.zero,
                TeamId.TeamA,
                flakConfig,
                candidates,
                out CombatTargetCandidate flakBest);

            Assert.That(foundFlakTarget, Is.True);
            Assert.That(flakBest.TargetIndex, Is.EqualTo(1));
        }

        [Test]
        public void HqMatchOutcomeEvaluator_ResolvesWinnerWhenEnemyHqDestroyed()
        {
            Assert.That(HqMatchOutcomeEvaluator.TryEvaluate(true, true, true, true, out _), Is.False);

            bool teamAWon = HqMatchOutcomeEvaluator.TryEvaluate(true, true, true, false, out MatchOutcome outcomeA);
            Assert.That(teamAWon, Is.True);
            Assert.That(outcomeA.WinnerTeam, Is.EqualTo(TeamId.TeamA));

            bool teamBWon = HqMatchOutcomeEvaluator.TryEvaluate(true, false, true, true, out MatchOutcome outcomeB);
            Assert.That(teamBWon, Is.True);
            Assert.That(outcomeB.WinnerTeam, Is.EqualTo(TeamId.TeamB));
        }

        [Test]
        public void ManualAimMath_StepsAndClampsBarrelAngleWithinAllowedBounds()
        {
            float angle = ManualAimMath.StepBarrelAngle(35f, new Vector2(0f, 1f), 40f, 0.5f);
            Assert.That(angle, Is.EqualTo(55f).Within(0.01f));

            float clampedHigh = ManualAimMath.StepBarrelAngle(170f, new Vector2(-1f, 0f), 60f, 1f);
            Assert.That(clampedHigh, Is.EqualTo(ManualAimMath.DefaultMaxBarrelAngle).Within(0.01f));

            float clampedLow = ManualAimMath.StepBarrelAngle(10f, new Vector2(1f, 0f), 60f, 1f);
            Assert.That(clampedLow, Is.EqualTo(ManualAimMath.DefaultMinBarrelAngle).Within(0.01f));

            Vector2 vel = ManualAimMath.BarrelAngleToVelocity(45f, 20f);
            Assert.That(vel.magnitude, Is.EqualTo(20f).Within(0.01f));
            Assert.That(vel.x, Is.GreaterThan(0f));
            Assert.That(vel.y, Is.GreaterThan(0f));
        }

        [Test]
        public void GroundProximityEvaluator_AndValidator_EnforceGroundDistanceOnlyForGroundStructures()
        {
            Assert.That(GroundProximityEvaluator.IsWithinGroundProximity(true, 1.0f, -2.0f, 3.25f), Is.True);
            Assert.That(GroundProximityEvaluator.IsWithinGroundProximity(true, 5.0f, -2.0f, 3.25f), Is.False);
            Assert.That(GroundProximityEvaluator.IsWithinGroundProximity(false, 12.0f, -2.0f, 3.25f), Is.True);

            var tooHighInput = new BuildValidationInput(
                phase: MatchPhase.Playing,
                seatExists: true,
                callerOwnsSeat: true,
                cardExists: true,
                cardInActiveLoadout: true,
                isOnCooldown: false,
                currentGold: 200,
                buildCostGold: 100,
                withinBuildRange: true,
                insideTeamTerritory: true,
                validTerrain: true,
                overlapsExistingStructure: false,
                requiresGoldDeposit: false,
                hasValidGoldDeposit: true,
                depositOccupied: false,
                withinGroundProximity: false);

            Assert.That(StructurePlacementValidator.Validate(tooHighInput), Is.EqualTo(BuildRejectReason.TooFarFromGround));
        }
    }
}
