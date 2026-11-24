using System;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct TerritoryZone
    {
        public int SourceId;
        public TeamId Team;
        public Vector2 Center;
        public float Radius;

        public TerritoryZone(int sourceId, TeamId team, Vector2 center, float radius)
        {
            SourceId = sourceId;
            Team = team;
            Center = center;
            Radius = radius;
        }
    }

    [Serializable]
    public struct BuildFeedback
    {
        public byte SeatId;
        public ushort CardId;
        public bool Accepted;
        public BuildRejectReason RejectReason;
        public Vector2 Position;

        public static BuildFeedback CreateAccepted(byte seatId, ushort cardId, Vector2 position)
        {
            return new BuildFeedback
            {
                SeatId = seatId,
                CardId = cardId,
                Accepted = true,
                RejectReason = BuildRejectReason.None,
                Position = position
            };
        }

        public static BuildFeedback CreateRejected(byte seatId, ushort cardId, BuildRejectReason reason, Vector2 position)
        {
            return new BuildFeedback
            {
                SeatId = seatId,
                CardId = cardId,
                Accepted = false,
                RejectReason = reason,
                Position = position
            };
        }
    }

    [Serializable]
    public struct AirshipCarryState
    {
        public bool IsCarrying;
        public byte SeatId;
        public ushort CardId;
        public int SlotIndex;
        public int ReservedGold;

        public static AirshipCarryState Empty => new()
        {
            IsCarrying = false,
            SeatId = 0,
            CardId = 0,
            SlotIndex = -1,
            ReservedGold = 0
        };

        public static AirshipCarryState Create(byte seatId, ushort cardId, int slotIndex, int reservedGold)
        {
            return new AirshipCarryState
            {
                IsCarrying = cardId != 0,
                SeatId = seatId,
                CardId = cardId,
                SlotIndex = slotIndex,
                ReservedGold = Math.Max(0, reservedGold)
            };
        }
    }
}
