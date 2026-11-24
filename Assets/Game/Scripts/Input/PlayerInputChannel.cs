namespace Game.Input
{
    /// <summary>
    /// Per-slot input channel that latches edge-triggered actions (DockPressed, SelectedBuildSlot)
    /// between variable-rate Unity Update frames and fixed-rate FishNet ticks.
    /// </summary>
    public sealed class PlayerInputChannel : IPlayerInputChannel
    {
        private PlayerInputSnapshot _latest;
        private bool _latchedDockPressed;
        private sbyte _latchedBuildSlot = -1;
        private bool _latchedConfirmPlacePressed;
        private bool _latchedCancelCarryPressed;

        public PlayerInputChannel(byte slotId)
        {
            SlotId = slotId;
            _latest = PlayerInputSnapshot.Empty;
        }

        public byte SlotId { get; }

        public PlayerInputSnapshot Current => new()
        {
            Move = _latest.Move,
            Aim = _latest.Aim,
            DockPressed = _latchedDockPressed,
            FireHeld = _latest.FireHeld,
            SelectedBuildSlot = _latchedBuildSlot,
            RepairHeld = _latest.RepairHeld,
            ConfirmPlacePressed = _latchedConfirmPlacePressed,
            CancelCarryPressed = _latchedCancelCarryPressed
        };

        public void UpdateFromFrame(in PlayerInputSnapshot frame)
        {
            _latest = frame;
            if (frame.DockPressed)
            {
                _latchedDockPressed = true;
            }

            if (frame.SelectedBuildSlot >= 0)
            {
                _latchedBuildSlot = frame.SelectedBuildSlot;
            }

            if (frame.ConfirmPlacePressed)
            {
                _latchedConfirmPlacePressed = true;
            }

            if (frame.CancelCarryPressed)
            {
                _latchedCancelCarryPressed = true;
            }
        }

        public PlayerInputSnapshot ConsumeTickSnapshot()
        {
            PlayerInputSnapshot snapshot = Current;
            _latchedDockPressed = false;
            _latchedBuildSlot = -1;
            _latchedConfirmPlacePressed = false;
            _latchedCancelCarryPressed = false;
            return snapshot;
        }

        public void Reset()
        {
            _latest = PlayerInputSnapshot.Empty;
            _latchedDockPressed = false;
            _latchedBuildSlot = -1;
            _latchedConfirmPlacePressed = false;
            _latchedCancelCarryPressed = false;
        }
    }
}
