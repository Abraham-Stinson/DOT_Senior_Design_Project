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
        private bool _latchedFirePressed;
        private sbyte _latchedBuildSlot = -1;
        private bool _latchedConfirmPlacePressed;
        private bool _latchedCancelCarryPressed;
        private sbyte _latchedCycleDelta;

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
            FirePressed = _latchedFirePressed,
            FireHeld = _latest.FireHeld,
            SelectedBuildSlot = _latchedBuildSlot,
            RepairHeld = _latest.RepairHeld,
            ConfirmPlacePressed = _latchedConfirmPlacePressed,
            CancelCarryPressed = _latchedCancelCarryPressed,
            CycleDelta = _latchedCycleDelta
        };

        public void UpdateFromFrame(in PlayerInputSnapshot frame)
        {
            _latest = frame;
            if (frame.DockPressed)
            {
                _latchedDockPressed = true;
            }

            if (frame.FirePressed)
            {
                _latchedFirePressed = true;
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

            if (frame.CycleDelta != 0)
            {
                _latchedCycleDelta = frame.CycleDelta;
            }
        }

        public PlayerInputSnapshot ConsumeTickSnapshot()
        {
            PlayerInputSnapshot snapshot = Current;
            _latchedDockPressed = false;
            _latchedFirePressed = false;
            _latchedBuildSlot = -1;
            _latchedConfirmPlacePressed = false;
            _latchedCancelCarryPressed = false;
            _latchedCycleDelta = 0;
            return snapshot;
        }

        public void Reset()
        {
            _latest = PlayerInputSnapshot.Empty;
            _latchedDockPressed = false;
            _latchedFirePressed = false;
            _latchedBuildSlot = -1;
            _latchedConfirmPlacePressed = false;
            _latchedCancelCarryPressed = false;
            _latchedCycleDelta = 0;
        }
    }
}
