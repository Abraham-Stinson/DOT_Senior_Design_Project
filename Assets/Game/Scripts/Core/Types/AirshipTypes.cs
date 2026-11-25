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

    public enum DockedTargetKind : byte
    {
        None = 0,
        Base = 1,
        NonBaseStructure = 2
    }

    public enum DockedStructureAction : byte
    {
        Use = 0,
        Sell = 1
    }

    public enum DockedInteractionSubMode : byte
    {
        None = 0,
        BaseDeckSelect = 1,
        StructureActionSelect = 2,
        WeaponManualControl = 3
    }

    [Serializable]
    public struct AirshipDockState
    {
        public AirshipDockMode Mode;
        public int TargetNetworkObjectId;
        public Vector2 AnchorPosition;
        public DockedTargetKind TargetKind;
        public DockedInteractionSubMode SubMode;
        public int SelectedDeckSlot;
        public DockedStructureAction SelectedAction;
        public ushort TargetCardId;
        public int TargetSellRefundGold;
        public bool CanUseTarget;

        public bool IsDocked => Mode != AirshipDockMode.Flying;

        public static AirshipDockState CreateFlying()
        {
            return new AirshipDockState
            {
                Mode = AirshipDockMode.Flying,
                TargetNetworkObjectId = -1,
                AnchorPosition = Vector2.zero,
                TargetKind = DockedTargetKind.None,
                SubMode = DockedInteractionSubMode.None,
                SelectedDeckSlot = 0,
                SelectedAction = DockedStructureAction.Use,
                TargetCardId = 0,
                TargetSellRefundGold = 0,
                CanUseTarget = false
            };
        }

        public static AirshipDockState CreateDocked(AirshipDockMode mode, int targetNetworkObjectId, Vector2 anchorPosition)
        {
            return CreateDockedInteraction(
                mode,
                targetNetworkObjectId,
                anchorPosition,
                DockedTargetKind.None,
                DockedInteractionSubMode.None,
                selectedDeckSlot: 0,
                selectedAction: DockedStructureAction.Use,
                targetCardId: 0,
                sellRefundGold: 0,
                canUseTarget: false);
        }

        public static AirshipDockState CreateDockedInteraction(
            AirshipDockMode mode,
            int targetNetworkObjectId,
            Vector2 anchorPosition,
            DockedTargetKind targetKind,
            DockedInteractionSubMode subMode,
            int selectedDeckSlot,
            DockedStructureAction selectedAction,
            ushort targetCardId,
            int sellRefundGold,
            bool canUseTarget)
        {
            return new AirshipDockState
            {
                Mode = mode == AirshipDockMode.Flying ? AirshipDockMode.DockedToStructure : mode,
                TargetNetworkObjectId = targetNetworkObjectId,
                AnchorPosition = anchorPosition,
                TargetKind = targetKind,
                SubMode = subMode,
                SelectedDeckSlot = selectedDeckSlot,
                SelectedAction = selectedAction,
                TargetCardId = targetCardId,
                TargetSellRefundGold = Math.Max(0, sellRefundGold),
                CanUseTarget = canUseTarget
            };
        }
    }
}
