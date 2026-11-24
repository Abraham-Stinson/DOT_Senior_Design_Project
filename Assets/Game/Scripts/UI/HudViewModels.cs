namespace Game.UI
{
    public readonly struct BuildSlotViewModel
    {
        public readonly int SlotIndex;
        public readonly ushort CardId;
        public readonly bool IsLockedSlotZero;
        public readonly bool IsAffordable;

        public BuildSlotViewModel(int slotIndex, ushort cardId, bool isLockedSlotZero, bool isAffordable)
        {
            SlotIndex = slotIndex;
            CardId = cardId;
            IsLockedSlotZero = isLockedSlotZero;
            IsAffordable = isAffordable;
        }
    }
}
