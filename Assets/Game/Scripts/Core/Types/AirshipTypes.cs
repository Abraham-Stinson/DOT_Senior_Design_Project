using System;
using UnityEngine;

namespace Game.Core
{
    public enum AirshipDockMode : byte
    {
        Flying = 0,
        DockedToStructure = 1,
        DockedToBuildSite = 2
    }

    [Serializable]
    public struct AirshipDockState
    {
        public AirshipDockMode Mode;
        public int TargetNetworkObjectId;
        public Vector2 AnchorPosition;

        public bool IsDocked => Mode != AirshipDockMode.Flying;

        public static AirshipDockState CreateFlying()
        {
            return new AirshipDockState
            {
                Mode = AirshipDockMode.Flying,
                TargetNetworkObjectId = -1,
                AnchorPosition = Vector2.zero
            };
        }

        public static AirshipDockState CreateDocked(AirshipDockMode mode, int targetNetworkObjectId, Vector2 anchorPosition)
        {
            return new AirshipDockState
            {
                Mode = mode == AirshipDockMode.Flying ? AirshipDockMode.DockedToStructure : mode,
                TargetNetworkObjectId = targetNetworkObjectId,
                AnchorPosition = anchorPosition
            };
        }
    }
}
