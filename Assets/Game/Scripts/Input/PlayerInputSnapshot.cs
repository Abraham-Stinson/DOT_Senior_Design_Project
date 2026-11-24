using UnityEngine;

namespace Game.Input
{
    public enum InputRoutingMode : byte
    {
        SinglePlayer = 0,
        LocalDualPlayer = 1
    }

    public struct PlayerInputSnapshot
    {
        public Vector2 Move;
        public Vector2 Aim;
        public bool DockPressed;
        public bool FireHeld;
        public sbyte SelectedBuildSlot; // -1 when unchanged, 0..4 for deck slots
        public bool RepairHeld;
        public bool ConfirmPlacePressed;
        public bool CancelCarryPressed;

        public static PlayerInputSnapshot Empty => new()
        {
            Move = Vector2.zero,
            Aim = Vector2.zero,
            DockPressed = false,
            FireHeld = false,
            SelectedBuildSlot = -1,
            RepairHeld = false,
            ConfirmPlacePressed = false,
            CancelCarryPressed = false
        };
    }

    public interface IPlayerInputChannel
    {
        byte SlotId { get; }
        PlayerInputSnapshot Current { get; }
        PlayerInputSnapshot ConsumeTickSnapshot();
    }

    public interface IPlayerInputRouter
    {
        InputRoutingMode RoutingMode { get; }
        void ConfigureMode(InputRoutingMode mode);
        IPlayerInputChannel GetChannel(byte slotId);
        IPlayerInputChannel GetChannelForSeat(byte seatId);
    }
}
