using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Input;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EconomyTerritoryAndBuildTests
    {
        [Test]
        public void EconomyLedgerAccumulatesFractionalIncomeAndManagesSources()
        {
            var ledger = new EconomyLedger();
            ledger.RegisterSeat(0, 10f);
            ledger.RegisterSeat(1, 5f);

            var earned0 = 0;
            var earned1 = 0;
            void Collect(byte seatId, int whole)
            {
                if (seatId == 0) earned0 += whole;
                else if (seatId == 1) earned1 += whole;
            }

            // 10/sec over 10 ticks of 0.05s (= 0.5s total) -> 5 gold for seat 0, 2 gold for seat 1 (2.5 carried).
            for (int i = 0; i < 10; i++)
            {
                ledger.StepTick(0.05f, Collect);
            }

            Assert.That(earned0, Is.EqualTo(5));
            Assert.That(earned1, Is.EqualTo(2));
            Assert.That(ledger.GetTotalIncomePerSecond(0), Is.EqualTo(10f).Within(0.001f));

            // Add a Gold Mine (+8/sec) to Seat 0.
            ledger.SetIncomeSource(0, 101, 8f);
            Assert.That(ledger.GetTotalIncomePerSecond(0), Is.EqualTo(18f).Within(0.001f));

            // Step 1 second in 0.25s chunks -> +18 gold.
            earned0 = 0;
            for (int i = 0; i < 4; i++)
            {
                ledger.StepTick(0.25f, Collect);
            }
            Assert.That(earned0, Is.EqualTo(18));

            // Removing the Gold Mine returns income rate back to base 10/sec.
            ledger.RemoveIncomeSource(0, 101);
            Assert.That(ledger.GetTotalIncomePerSecond(0), Is.EqualTo(10f).Within(0.001f));

            Assert.That(EconomyLedger.TrySpend(100, 75, out int remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(25));
            Assert.That(EconomyLedger.TrySpend(25, 80, out int unchanged), Is.False);
            Assert.That(unchanged, Is.EqualTo(25));
            Assert.That(EconomyLedger.ComputeRefund(100, 0.5f), Is.EqualTo(50));
        }

        [Test]
        public void TerritoryMathExpandsWithBalloonZoneAndShrinksOnRemoval()
        {
            var zones = new List<TerritoryZone>
            {
                new(-1, TeamId.TeamA, new Vector2(-12f, 0f), 8f),
                new(-2, TeamId.TeamB, new Vector2(12f, 0f), 8f)
            };

            Vector2 forwardPoint = new(-1.5f, 0f);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamA, new Vector2(-6f, 0f), zones), Is.True);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamA, forwardPoint, zones), Is.False);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamB, new Vector2(-6f, 0f), zones), Is.False);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.Neutral, Vector2.zero, zones), Is.False);

            // Adding a Territory Balloon for TeamA at x = -5 with radius 7 covers x = -1.5 (dist = 3.5 <= 7).
            zones.Add(new TerritoryZone(42, TeamId.TeamA, new Vector2(-5f, 0f), 7f));
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamA, forwardPoint, zones), Is.True);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamB, forwardPoint, zones), Is.False);

            Assert.That(TerritoryMath.TryGetCombinedHorizontalExtent(TeamId.TeamA, zones, out float minX, out float maxX), Is.True);
            Assert.That(minX, Is.EqualTo(-20f).Within(0.001f));
            Assert.That(maxX, Is.EqualTo(2f).Within(0.001f));

            // Removing the Balloon zone shrinks reach back to HQ radius [-20..-4].
            zones.RemoveAll(z => z.SourceId == 42);
            Assert.That(TerritoryMath.IsPointInsideTeamTerritory(TeamId.TeamA, forwardPoint, zones), Is.False);
        }

        [Test]
        public void StructurePlacementValidatorEnforcesAllRulesInPriorityOrder()
        {
            BuildValidationInput MakeValid() => new(
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
                overlapsExistingStructure: false);

            Assert.That(StructurePlacementValidator.Validate(MakeValid()), Is.EqualTo(BuildRejectReason.None));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.PreMatchDraft, false, false, false, false, true, 0, 100, false, false, false, true)),
                Is.EqualTo(BuildRejectReason.NotPlaying));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, false, true, true, true, false, 200, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.InvalidSeat));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, false, true, true, false, 200, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.UnauthorizedSender));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, false, true, false, 200, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.UnknownCard));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, false, false, 200, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.CardNotInLoadout));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, true, 200, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.OnCooldown));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 50, 100, true, true, true, false)),
                Is.EqualTo(BuildRejectReason.InsufficientGold));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 200, 100, false, true, true, false)),
                Is.EqualTo(BuildRejectReason.OutOfBuildRange));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 200, 100, true, false, true, false)),
                Is.EqualTo(BuildRejectReason.OutsideTerritory));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 200, 100, true, true, false, false)),
                Is.EqualTo(BuildRejectReason.InvalidTerrain));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 200, 100, true, true, true, true)),
                Is.EqualTo(BuildRejectReason.OverlappingStructure));
        }

        [Test]
        public void TerritoryCircleVisualBuildsCircularDiscAndRingMatchingRadius()
        {
            var go = new GameObject("TerritoryCircleVisualTest");
            try
            {
                var visual = go.AddComponent<TerritoryCircleVisual>();
                visual.Configure(new Vector2(-6f, 2f), 7.5f, new Color(0.25f, 0.62f, 0.96f));

                Assert.That(visual.CurrentRadius, Is.EqualTo(7.5f).Within(0.001f));
                Assert.That(visual.DiscMeshFilter, Is.Not.Null);
                Assert.That(visual.DiscMeshFilter.sharedMesh, Is.Not.Null);
                Assert.That(visual.DiscMeshFilter.sharedMesh.vertexCount, Is.EqualTo(visual.SegmentCount + 1));
                Assert.That(visual.DiscMeshFilter.transform.localScale, Is.EqualTo(new Vector3(7.5f, 7.5f, 1f)));

                Assert.That(visual.BorderLineRenderer, Is.Not.Null);
                Assert.That(visual.BorderLineRenderer.loop, Is.True);
                Assert.That(visual.BorderLineRenderer.positionCount, Is.EqualTo(visual.SegmentCount));

                for (int i = 0; i < visual.BorderLineRenderer.positionCount; i++)
                {
                    Vector3 point = visual.BorderLineRenderer.GetPosition(i);
                    Assert.That(new Vector2(point.x, point.y).magnitude, Is.EqualTo(7.5f).Within(0.001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class FakeGoldDeposit : IGoldDeposit
        {
            public int DepositId { get; set; }
            public Vector2 AnchorWorld { get; set; }
            public float SnapRadius { get; set; }
            public bool IsOccupied { get; set; }
            public void OccupyServer(int structureObjectId) => IsOccupied = true;
            public void ReleaseServer(int structureObjectId) => IsOccupied = false;
        }

        [Test]
        public void GoldDepositMatcherAndPlacementValidatorEnforceDepositSpots()
        {
            var freeDeposit = new FakeGoldDeposit { DepositId = 1, AnchorWorld = new Vector2(-9f, -1f), SnapRadius = 2.5f, IsOccupied = false };
            var occupiedDeposit = new FakeGoldDeposit { DepositId = 2, AnchorWorld = new Vector2(-2f, -1f), SnapRadius = 2.5f, IsOccupied = true };
            var deposits = new List<IGoldDeposit> { freeDeposit, occupiedDeposit };

            Assert.That(GoldDepositMatcher.TryFindNearestDeposit(new Vector2(-8f, -1f), deposits, out IGoldDeposit matched, out bool occupiedOnly), Is.True);
            Assert.That(matched, Is.EqualTo(freeDeposit));
            Assert.That(occupiedOnly, Is.False);

            Assert.That(GoldDepositMatcher.TryFindNearestDeposit(new Vector2(-2.5f, -1f), deposits, out IGoldDeposit miss, out bool foundOcc), Is.False);
            Assert.That(miss, Is.Null);
            Assert.That(foundOcc, Is.True);

            Assert.That(GoldDepositMatcher.TryFindNearestDeposit(new Vector2(-15f, -1f), deposits, out _, out bool farOcc), Is.False);
            Assert.That(farOcc, Is.False);

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 100, 80, true, true, true, false,
                requiresGoldDeposit: true, hasValidGoldDeposit: false, depositOccupied: false)),
                Is.EqualTo(BuildRejectReason.RequiresGoldDeposit));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 100, 80, true, true, true, false,
                requiresGoldDeposit: true, hasValidGoldDeposit: false, depositOccupied: true)),
                Is.EqualTo(BuildRejectReason.DepositOccupied));

            Assert.That(StructurePlacementValidator.Validate(new BuildValidationInput(
                MatchPhase.Playing, true, true, true, true, false, 100, 80, true, true, true, false,
                requiresGoldDeposit: true, hasValidGoldDeposit: true, depositOccupied: false)),
                Is.EqualTo(BuildRejectReason.None));
        }

        [Test]
        public void InputChannelLatchesConfirmAndCancelCarryUntilTickConsumes()
        {
            var channel = new PlayerInputChannel(0);
            channel.UpdateFromFrame(new PlayerInputSnapshot
            {
                ConfirmPlacePressed = true,
                CancelCarryPressed = true,
                SelectedBuildSlot = -1
            });

            channel.UpdateFromFrame(new PlayerInputSnapshot
            {
                ConfirmPlacePressed = false,
                CancelCarryPressed = false,
                SelectedBuildSlot = -1
            });

            PlayerInputSnapshot tick = channel.ConsumeTickSnapshot();
            Assert.That(tick.ConfirmPlacePressed, Is.True);
            Assert.That(tick.CancelCarryPressed, Is.True);

            PlayerInputSnapshot nextTick = channel.ConsumeTickSnapshot();
            Assert.That(nextTick.ConfirmPlacePressed, Is.False);
            Assert.That(nextTick.CancelCarryPressed, Is.False);
        }

        [Test]
        public void GoldDepositMatcherEvaluatesTerritoryRangeAndOccupancyStates()
        {
            var zones = new List<TerritoryZone>
            {
                new(-1, TeamId.TeamA, new Vector2(-12.5f, 0f), 8.5f)
            };

            var depositNear = new FakeGoldDeposit { DepositId = 1, AnchorWorld = new Vector2(-9.5f, -1.3f), SnapRadius = 2.6f, IsOccupied = false };
            var depositForward = new FakeGoldDeposit { DepositId = 2, AnchorWorld = new Vector2(-2.0f, -1.3f), SnapRadius = 2.6f, IsOccupied = false };
            var depositEnemy = new FakeGoldDeposit { DepositId = 3, AnchorWorld = new Vector2(9.5f, -1.3f), SnapRadius = 2.6f, IsOccupied = false };

            Vector2 airshipPos = new(-10f, 3f);
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositNear, TeamId.TeamA, zones, airshipPos, 10f), Is.EqualTo(GoldDepositSpotState.Valid));
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositForward, TeamId.TeamA, zones, airshipPos, 10f), Is.EqualTo(GoldDepositSpotState.OutsideTerritory));
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositEnemy, TeamId.TeamA, zones, airshipPos, 10f), Is.EqualTo(GoldDepositSpotState.OutsideTerritory));

            // Inside territory but airship too far horizontally -> OutOfRange.
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositNear, TeamId.TeamA, zones, new Vector2(-20f, 3f), 10f), Is.EqualTo(GoldDepositSpotState.OutOfRange));

            // Occupied deposit stays Occupied regardless of territory/range.
            depositNear.IsOccupied = true;
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositNear, TeamId.TeamA, zones, airshipPos, 10f), Is.EqualTo(GoldDepositSpotState.Occupied));

            // Expanding territory via Territory Balloon makes forward deposit Valid when airship flies near.
            zones.Add(new TerritoryZone(55, TeamId.TeamA, new Vector2(-6f, 0f), 7.5f));
            Assert.That(GoldDepositMatcher.EvaluateDepositSpot(depositForward, TeamId.TeamA, zones, new Vector2(-4f, 3f), 10f), Is.EqualTo(GoldDepositSpotState.Valid));
        }
    }
}
