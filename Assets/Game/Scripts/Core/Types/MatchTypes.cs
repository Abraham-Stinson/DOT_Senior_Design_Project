using System;
using UnityEngine;

namespace Game.Core
{
    public enum TeamId : byte
    {
        Neutral = 0,
        TeamA = 1,
        TeamB = 2,
        EnemyAI = 3
    }

    public enum MatchMode : byte
    {
        LocalPvP = 0,
        OnlinePvP = 1,
        Story = 2
    }

    public enum MatchPhase : byte
    {
        PreMatchDraft = 0,
        Spawning = 1,
        Playing = 2,
        Ended = 3
    }

    public enum SeatControlType : byte
    {
        HumanLocal = 0,
        HumanRemote = 1,
        AI = 2
    }

    public enum StructureCategory : byte
    {
        Core = 0,
        Territory = 1,
        Offense = 2,
        Defense = 3,
        Economy = 4,
        Utility = 5,
        Spawner = 6
    }

    public enum BuildRejectReason : byte
    {
        None = 0,
        InvalidSeat = 1,
        NotPlaying = 2,
        CardNotInLoadout = 3,
        UnknownCard = 4,
        InsufficientGold = 5,
        OutsideTerritory = 6,
        InvalidTerrain = 7,
        OverlappingStructure = 8,
        OutOfBuildRange = 9,
        UnauthorizedSender = 10,
        OnCooldown = 11,
        RequiresGoldDeposit = 12,
        DepositOccupied = 13,
        AlreadyCarrying = 14,
        NotCarrying = 15,
        TooFarFromGround = 16
    }

    public enum LoadoutValidationError : byte
    {
        None = 0,
        InvalidDeckSize = 1,
        InvalidCatalog = 2,
        SlotZeroMustBeTerritoryBalloon = 3,
        UnknownCard = 4,
        CardNotSelectable = 5,
        DuplicateCard = 6,
        CardLocked = 7
    }

    public enum CardUnlockResult : byte
    {
        Success = 0,
        UnknownCard = 1,
        NotUnlockable = 2,
        AlreadyUnlocked = 3,
        InsufficientCurrency = 4
    }

    public enum DraftRejectReason : byte
    {
        None = 0,
        NotInDraftPhase = 1,
        InvalidSeat = 2,
        UnauthorizedSender = 3,
        LoadoutInvalid = 4
    }

    [Serializable]
    public struct DamageInfo
    {
        public float Amount;
        public TeamId SourceTeam;
        public byte SourceSeatId;
        public Vector2 HitPoint;

        public DamageInfo(float amount, TeamId sourceTeam, byte sourceSeatId, Vector2 hitPoint)
        {
            Amount = amount;
            SourceTeam = sourceTeam;
            SourceSeatId = sourceSeatId;
            HitPoint = hitPoint;
        }
    }

    [Serializable]
    public struct InteractPayload
    {
        public byte ActionCode;
        public Vector2 TargetPoint;

        public InteractPayload(byte actionCode, Vector2 targetPoint)
        {
            ActionCode = actionCode;
            TargetPoint = targetPoint;
        }
    }

    [Serializable]
    public struct MatchOutcome
    {
        public TeamId WinnerTeam;
        public string Reason;

        public MatchOutcome(TeamId winnerTeam, string reason)
        {
            WinnerTeam = winnerTeam;
            Reason = reason ?? string.Empty;
        }
    }
}
