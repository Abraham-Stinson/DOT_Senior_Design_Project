using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Pure geometric helpers for team territory membership and combined horizontal reach.
    /// </summary>
    public static class TerritoryMath
    {
        public static bool IsPointInsideTeamTerritory(
            TeamId team,
            Vector2 worldPoint,
            IReadOnlyList<TerritoryZone> zones)
        {
            if (team == TeamId.Neutral || zones == null)
            {
                return false;
            }

            for (int i = 0; i < zones.Count; i++)
            {
                TerritoryZone zone = zones[i];
                if (zone.Team != team || zone.Radius <= 0f)
                {
                    continue;
                }

                float radiusSq = zone.Radius * zone.Radius;
                if ((worldPoint - zone.Center).sqrMagnitude <= radiusSq)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetCombinedHorizontalExtent(
            TeamId team,
            IReadOnlyList<TerritoryZone> zones,
            out float minX,
            out float maxX)
        {
            minX = float.MaxValue;
            maxX = float.MinValue;
            bool found = false;

            if (team == TeamId.Neutral || zones == null)
            {
                minX = 0f;
                maxX = 0f;
                return false;
            }

            for (int i = 0; i < zones.Count; i++)
            {
                TerritoryZone zone = zones[i];
                if (zone.Team != team || zone.Radius <= 0f)
                {
                    continue;
                }

                found = true;
                float zoneMin = zone.Center.x - zone.Radius;
                float zoneMax = zone.Center.x + zone.Radius;
                if (zoneMin < minX) minX = zoneMin;
                if (zoneMax > maxX) maxX = zoneMax;
            }

            if (!found)
            {
                minX = 0f;
                maxX = 0f;
            }

            return found;
        }
    }

    /// <summary>
    /// Server-side accumulator tracking per-seat base income, dynamic bonus sources (e.g. Gold Mine),
    /// and fractional tick carry-over into whole gold grants.
    /// </summary>
    public sealed class EconomyLedger
    {
        private sealed class SeatIncomeEntry
        {
            public float BaseIncomePerSecond;
            public readonly Dictionary<int, float> BonusSources = new();
            public float FractionalGold;
        }

        private readonly Dictionary<byte, SeatIncomeEntry> _entries = new();

        public void RegisterSeat(byte seatId, float baseIncomePerSecond)
        {
            if (!_entries.TryGetValue(seatId, out SeatIncomeEntry entry))
            {
                entry = new SeatIncomeEntry();
                _entries[seatId] = entry;
            }

            entry.BaseIncomePerSecond = Math.Max(0f, baseIncomePerSecond);
            entry.FractionalGold = 0f;
            entry.BonusSources.Clear();
        }

        public void UnregisterSeat(byte seatId)
        {
            _entries.Remove(seatId);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public void SetIncomeSource(byte seatId, int sourceId, float goldPerSecond)
        {
            if (!_entries.TryGetValue(seatId, out SeatIncomeEntry entry))
            {
                return;
            }

            float clamped = Math.Max(0f, goldPerSecond);
            if (clamped <= 0f)
            {
                entry.BonusSources.Remove(sourceId);
            }
            else
            {
                entry.BonusSources[sourceId] = clamped;
            }
        }

        public void RemoveIncomeSource(byte seatId, int sourceId)
        {
            if (_entries.TryGetValue(seatId, out SeatIncomeEntry entry))
            {
                entry.BonusSources.Remove(sourceId);
            }
        }

        public float GetTotalIncomePerSecond(byte seatId)
        {
            if (!_entries.TryGetValue(seatId, out SeatIncomeEntry entry))
            {
                return 0f;
            }

            float total = entry.BaseIncomePerSecond;
            foreach (float bonus in entry.BonusSources.Values)
            {
                total += bonus;
            }

            return Math.Max(0f, total);
        }

        public void StepTick(float deltaTime, Action<byte, int> onWholeGoldEarned)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            foreach (KeyValuePair<byte, SeatIncomeEntry> kvp in _entries)
            {
                SeatIncomeEntry entry = kvp.Value;
                float totalRate = entry.BaseIncomePerSecond;
                foreach (float bonus in entry.BonusSources.Values)
                {
                    totalRate += bonus;
                }

                if (totalRate <= 0f)
                {
                    continue;
                }

                entry.FractionalGold += totalRate * deltaTime;
                int whole = (int)Math.Floor(entry.FractionalGold);
                if (whole > 0)
                {
                    entry.FractionalGold -= whole;
                    onWholeGoldEarned?.Invoke(kvp.Key, whole);
                }
            }
        }

        public static bool TrySpend(int currentGold, int cost, out int remainingGold)
        {
            if (cost < 0 || currentGold < cost)
            {
                remainingGold = currentGold;
                return false;
            }

            remainingGold = currentGold - cost;
            return true;
        }

        public static int ComputeRefund(int cost, float ratio)
        {
            float clampedRatio = Mathf.Clamp01(ratio);
            return Mathf.RoundToInt(Math.Max(0, cost) * clampedRatio);
        }
    }

    public enum GoldDepositSpotState : byte
    {
        Neutral = 0,
        Valid = 1,
        OutsideTerritory = 2,
        OutOfRange = 3,
        Occupied = 4
    }

    public static class GroundProximityEvaluator
    {
        public static bool IsWithinGroundProximity(
            bool requiresGroundSupport,
            float sourceHeightY,
            float groundSurfaceY,
            float maxGroundPlacementDistance)
        {
            if (!requiresGroundSupport || maxGroundPlacementDistance <= 0f)
            {
                return true;
            }

            float heightAboveGround = sourceHeightY - groundSurfaceY;
            return heightAboveGround <= maxGroundPlacementDistance && heightAboveGround >= -1.5f;
        }
    }

    public static class GoldDepositMatcher
    {
        public static GoldDepositSpotState EvaluateDepositSpot(
            IGoldDeposit deposit,
            TeamId team,
            IReadOnlyList<TerritoryZone> zones,
            Vector2 airshipPosition,
            float maxBuildRange,
            Vector2? resolvedPlacementPosition = null,
            float maxGroundPlacementDistance = 0f,
            float? groundSurfaceY = null)
        {
            if (deposit == null)
            {
                return GoldDepositSpotState.Neutral;
            }

            if (deposit.IsOccupied)
            {
                return GoldDepositSpotState.Occupied;
            }

            Vector2 checkPos = resolvedPlacementPosition ?? deposit.AnchorWorld;
            if (zones != null && !TerritoryMath.IsPointInsideTeamTerritory(team, checkPos, zones))
            {
                return GoldDepositSpotState.OutsideTerritory;
            }

            if (maxBuildRange > 0f && (checkPos - airshipPosition).sqrMagnitude > maxBuildRange * maxBuildRange)
            {
                return GoldDepositSpotState.OutOfRange;
            }

            if (maxGroundPlacementDistance > 0f
                && groundSurfaceY.HasValue
                && !GroundProximityEvaluator.IsWithinGroundProximity(true, airshipPosition.y, groundSurfaceY.Value, maxGroundPlacementDistance))
            {
                return GoldDepositSpotState.OutOfRange;
            }

            return GoldDepositSpotState.Valid;
        }

        public static bool TryFindNearestDeposit(
            Vector2 targetPoint,
            IReadOnlyList<IGoldDeposit> deposits,
            out IGoldDeposit availableDeposit,
            out bool foundOccupiedOnly)
        {
            availableDeposit = null;
            foundOccupiedOnly = false;
            if (deposits == null)
            {
                return false;
            }

            float bestDistSq = float.MaxValue;
            bool sawOccupiedInRange = false;

            for (int i = 0; i < deposits.Count; i++)
            {
                IGoldDeposit candidate = deposits[i];
                if (candidate == null || candidate.SnapRadius <= 0f)
                {
                    continue;
                }

                float radiusSq = candidate.SnapRadius * candidate.SnapRadius;
                float distSq = (targetPoint - candidate.AnchorWorld).sqrMagnitude;
                if (distSq > radiusSq)
                {
                    continue;
                }

                if (candidate.IsOccupied)
                {
                    sawOccupiedInRange = true;
                    continue;
                }

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    availableDeposit = candidate;
                }
            }

            if (availableDeposit != null)
            {
                return true;
            }

            foundOccupiedOnly = sawOccupiedInRange;
            return false;
        }
    }

    public readonly struct BuildValidationInput
    {
        public readonly MatchPhase Phase;
        public readonly bool SeatExists;
        public readonly bool CallerOwnsSeat;
        public readonly bool CardExists;
        public readonly bool CardInActiveLoadout;
        public readonly bool IsOnCooldown;
        public readonly int CurrentGold;
        public readonly int BuildCostGold;
        public readonly bool WithinBuildRange;
        public readonly bool InsideTeamTerritory;
        public readonly bool ValidTerrain;
        public readonly bool OverlapsExistingStructure;
        public readonly bool RequiresGoldDeposit;
        public readonly bool HasValidGoldDeposit;
        public readonly bool DepositOccupied;
        public readonly bool WithinGroundProximity;

        public BuildValidationInput(
            MatchPhase phase,
            bool seatExists,
            bool callerOwnsSeat,
            bool cardExists,
            bool cardInActiveLoadout,
            bool isOnCooldown,
            int currentGold,
            int buildCostGold,
            bool withinBuildRange,
            bool insideTeamTerritory,
            bool validTerrain,
            bool overlapsExistingStructure)
            : this(
                phase,
                seatExists,
                callerOwnsSeat,
                cardExists,
                cardInActiveLoadout,
                isOnCooldown,
                currentGold,
                buildCostGold,
                withinBuildRange,
                insideTeamTerritory,
                validTerrain,
                overlapsExistingStructure,
                requiresGoldDeposit: false,
                hasValidGoldDeposit: true,
                depositOccupied: false,
                withinGroundProximity: true)
        {
        }

        public BuildValidationInput(
            MatchPhase phase,
            bool seatExists,
            bool callerOwnsSeat,
            bool cardExists,
            bool cardInActiveLoadout,
            bool isOnCooldown,
            int currentGold,
            int buildCostGold,
            bool withinBuildRange,
            bool insideTeamTerritory,
            bool validTerrain,
            bool overlapsExistingStructure,
            bool requiresGoldDeposit,
            bool hasValidGoldDeposit,
            bool depositOccupied,
            bool withinGroundProximity = true)
        {
            Phase = phase;
            SeatExists = seatExists;
            CallerOwnsSeat = callerOwnsSeat;
            CardExists = cardExists;
            CardInActiveLoadout = cardInActiveLoadout;
            IsOnCooldown = isOnCooldown;
            CurrentGold = currentGold;
            BuildCostGold = buildCostGold;
            WithinBuildRange = withinBuildRange;
            InsideTeamTerritory = insideTeamTerritory;
            ValidTerrain = validTerrain;
            OverlapsExistingStructure = overlapsExistingStructure;
            RequiresGoldDeposit = requiresGoldDeposit;
            HasValidGoldDeposit = hasValidGoldDeposit;
            DepositOccupied = depositOccupied;
            WithinGroundProximity = withinGroundProximity;
        }
    }

    /// <summary>
    /// Deterministic priority-ordered build validator shared by server execution and unit tests.
    /// </summary>
    public static class StructurePlacementValidator
    {
        public static BuildRejectReason Validate(in BuildValidationInput input)
        {
            if (input.Phase != MatchPhase.Playing)
            {
                return BuildRejectReason.NotPlaying;
            }

            if (!input.SeatExists)
            {
                return BuildRejectReason.InvalidSeat;
            }

            if (!input.CallerOwnsSeat)
            {
                return BuildRejectReason.UnauthorizedSender;
            }

            if (!input.CardExists)
            {
                return BuildRejectReason.UnknownCard;
            }

            if (!input.CardInActiveLoadout)
            {
                return BuildRejectReason.CardNotInLoadout;
            }

            if (input.IsOnCooldown)
            {
                return BuildRejectReason.OnCooldown;
            }

            if (input.BuildCostGold < 0 || input.CurrentGold < input.BuildCostGold)
            {
                return BuildRejectReason.InsufficientGold;
            }

            if (!input.WithinBuildRange)
            {
                return BuildRejectReason.OutOfBuildRange;
            }

            if (!input.InsideTeamTerritory)
            {
                return BuildRejectReason.OutsideTerritory;
            }

            if (!input.WithinGroundProximity)
            {
                return BuildRejectReason.TooFarFromGround;
            }

            if (input.RequiresGoldDeposit)
            {
                if (input.DepositOccupied)
                {
                    return BuildRejectReason.DepositOccupied;
                }

                if (!input.HasValidGoldDeposit)
                {
                    return BuildRejectReason.RequiresGoldDeposit;
                }
            }

            if (!input.ValidTerrain)
            {
                return BuildRejectReason.InvalidTerrain;
            }

            if (input.OverlapsExistingStructure)
            {
                return BuildRejectReason.OverlappingStructure;
            }

            return BuildRejectReason.None;
        }
    }
}
