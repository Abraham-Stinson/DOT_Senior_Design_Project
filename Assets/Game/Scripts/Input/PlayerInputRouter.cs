using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input
{
    public static class ActiveInputRouterRegistry
    {
        public static IPlayerInputRouter Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Current = null;
        }

        public static void Register(IPlayerInputRouter router)
        {
            if (router != null)
            {
                Current = router;
            }
        }

        public static void Unregister(IPlayerInputRouter router)
        {
            if (ReferenceEquals(Current, router))
            {
                Current = null;
            }
        }
    }

    public sealed class PlayerInputRouter : MonoBehaviour, IPlayerInputRouter
    {
        [SerializeField] private InputRoutingMode _routingMode = InputRoutingMode.SinglePlayer;
        [SerializeField] private bool _enableHardwarePolling = true;

        private readonly PlayerInputChannel[] _channels =
        {
            new(0),
            new(1)
        };

        public InputRoutingMode RoutingMode => _routingMode;
        public bool EnableHardwarePolling
        {
            get => _enableHardwarePolling;
            set => _enableHardwarePolling = value;
        }

        private void OnEnable()
        {
            ActiveInputRouterRegistry.Register(this);
        }

        private void OnDisable()
        {
            ActiveInputRouterRegistry.Unregister(this);
        }

        public void ConfigureMode(InputRoutingMode mode)
        {
            _routingMode = mode;
            ResetAllChannels();
        }

        public IPlayerInputChannel GetChannel(byte slotId)
        {
            return slotId < _channels.Length ? _channels[slotId] : null;
        }

        public IPlayerInputChannel GetChannelForSeat(byte seatId)
        {
            if (_routingMode == InputRoutingMode.SinglePlayer)
            {
                return _channels[0];
            }

            return GetChannel(seatId);
        }

        public void InjectSyntheticInput(byte slotId, in PlayerInputSnapshot snapshot)
        {
            if (slotId < _channels.Length)
            {
                _channels[slotId].UpdateFromFrame(snapshot);
            }
        }

        public void ResetAllChannels()
        {
            for (int i = 0; i < _channels.Length; i++)
            {
                _channels[i].Reset();
            }
        }

        private void Update()
        {
            if (!_enableHardwarePolling)
            {
                return;
            }

            if (_routingMode == InputRoutingMode.SinglePlayer)
            {
                _channels[0].UpdateFromFrame(ReadSinglePlayerFrame());
                _channels[1].Reset();
            }
            else
            {
                _channels[0].UpdateFromFrame(ReadLocalPlayerOneFrame());
                _channels[1].UpdateFromFrame(ReadLocalPlayerTwoFrame());
            }
        }

        private static PlayerInputSnapshot ReadSinglePlayerFrame()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;

            Vector2 move = Vector2.zero;
            bool dockPressed = false;
            bool fireHeld = false;
            bool repairHeld = false;
            bool confirmPlacePressed = false;
            bool cancelCarryPressed = false;
            sbyte buildSlot = -1;

            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                dockPressed |= kb.eKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame;
                fireHeld |= kb.spaceKey.isPressed;
                repairHeld |= kb.rKey.isPressed;
                confirmPlacePressed |= kb.spaceKey.wasPressedThisFrame;
                cancelCarryPressed |= kb.qKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
                buildSlot = ReadDigitBuildSlot(kb);
            }

            Vector2 aim = Vector2.zero;
            if (mouse != null)
            {
                aim = mouse.position.ReadValue();
                fireHeld |= mouse.leftButton.isPressed;
                confirmPlacePressed |= mouse.leftButton.wasPressedThisFrame;
                cancelCarryPressed |= mouse.rightButton.wasPressedThisFrame;
            }

            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) move += stick;
                Vector2 rightStick = pad.rightStick.ReadValue();
                if (rightStick.sqrMagnitude > 0.04f) aim = rightStick;
                dockPressed |= pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame;
                fireHeld |= pad.rightTrigger.ReadValue() > 0.25f || pad.buttonSouth.isPressed;
                repairHeld |= pad.leftTrigger.ReadValue() > 0.25f;
                confirmPlacePressed |= pad.buttonSouth.wasPressedThisFrame;
                cancelCarryPressed |= pad.buttonEast.wasPressedThisFrame;
            }

            return new PlayerInputSnapshot
            {
                Move = Vector2.ClampMagnitude(move, 1f),
                Aim = aim,
                DockPressed = dockPressed,
                FireHeld = fireHeld,
                SelectedBuildSlot = buildSlot,
                RepairHeld = repairHeld,
                ConfirmPlacePressed = confirmPlacePressed,
                CancelCarryPressed = cancelCarryPressed
            };
        }

        private static PlayerInputSnapshot ReadLocalPlayerOneFrame()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.all.Count >= 2 ? Gamepad.all[0] : null;

            Vector2 move = Vector2.zero;
            Vector2 aim = Vector2.zero;
            bool dockPressed = false;
            bool fireHeld = false;
            bool repairHeld = false;
            bool confirmPlacePressed = false;
            bool cancelCarryPressed = false;
            sbyte buildSlot = -1;

            if (kb != null)
            {
                if (kb.aKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.wKey.isPressed) move.y += 1f;
                dockPressed |= kb.eKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame;
                fireHeld |= kb.spaceKey.isPressed;
                repairHeld |= kb.rKey.isPressed;
                confirmPlacePressed |= kb.spaceKey.wasPressedThisFrame;
                cancelCarryPressed |= kb.qKey.wasPressedThisFrame;
                buildSlot = ReadDigitBuildSlot(kb);
            }

            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) move += stick;
                Vector2 rightStick = pad.rightStick.ReadValue();
                if (rightStick.sqrMagnitude > 0.04f) aim = rightStick;
                dockPressed |= pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame;
                fireHeld |= pad.rightTrigger.ReadValue() > 0.25f || pad.buttonSouth.isPressed;
                repairHeld |= pad.leftTrigger.ReadValue() > 0.25f;
                confirmPlacePressed |= pad.buttonSouth.wasPressedThisFrame;
                cancelCarryPressed |= pad.buttonEast.wasPressedThisFrame;
            }

            return new PlayerInputSnapshot
            {
                Move = Vector2.ClampMagnitude(move, 1f),
                Aim = aim,
                DockPressed = dockPressed,
                FireHeld = fireHeld,
                SelectedBuildSlot = buildSlot,
                RepairHeld = repairHeld,
                ConfirmPlacePressed = confirmPlacePressed,
                CancelCarryPressed = cancelCarryPressed
            };
        }

        private static PlayerInputSnapshot ReadLocalPlayerTwoFrame()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.all.Count >= 2
                ? Gamepad.all[1]
                : (Gamepad.all.Count == 1 ? Gamepad.all[0] : null);

            Vector2 move = Vector2.zero;
            Vector2 aim = Vector2.zero;
            bool dockPressed = false;
            bool fireHeld = false;
            bool repairHeld = false;
            bool confirmPlacePressed = false;
            bool cancelCarryPressed = false;
            sbyte buildSlot = -1;

            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.upArrowKey.isPressed) move.y += 1f;
                dockPressed |= kb.rightShiftKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                fireHeld |= kb.rightCtrlKey.isPressed || kb.slashKey.isPressed || kb.numpad0Key.isPressed;
                repairHeld |= kb.backspaceKey.isPressed;
                confirmPlacePressed |= kb.rightCtrlKey.wasPressedThisFrame || kb.slashKey.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame;
                cancelCarryPressed |= kb.backspaceKey.wasPressedThisFrame || kb.rightAltKey.wasPressedThisFrame;
                buildSlot = ReadNumpadOrAltBuildSlot(kb);
            }

            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) move += stick;
                Vector2 rightStick = pad.rightStick.ReadValue();
                if (rightStick.sqrMagnitude > 0.04f) aim = rightStick;
                dockPressed |= pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame;
                fireHeld |= pad.rightTrigger.ReadValue() > 0.25f || pad.buttonSouth.isPressed;
                repairHeld |= pad.leftTrigger.ReadValue() > 0.25f;
                confirmPlacePressed |= pad.buttonSouth.wasPressedThisFrame;
                cancelCarryPressed |= pad.buttonEast.wasPressedThisFrame;
            }

            return new PlayerInputSnapshot
            {
                Move = Vector2.ClampMagnitude(move, 1f),
                Aim = aim,
                DockPressed = dockPressed,
                FireHeld = fireHeld,
                SelectedBuildSlot = buildSlot,
                RepairHeld = repairHeld,
                ConfirmPlacePressed = confirmPlacePressed,
                CancelCarryPressed = cancelCarryPressed
            };
        }

        private static sbyte ReadDigitBuildSlot(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame) return 0;
            if (kb.digit2Key.wasPressedThisFrame) return 1;
            if (kb.digit3Key.wasPressedThisFrame) return 2;
            if (kb.digit4Key.wasPressedThisFrame) return 3;
            if (kb.digit5Key.wasPressedThisFrame) return 4;
            return -1;
        }

        private static sbyte ReadNumpadOrAltBuildSlot(Keyboard kb)
        {
            if (kb.numpad1Key.wasPressedThisFrame || kb.digit6Key.wasPressedThisFrame) return 0;
            if (kb.numpad2Key.wasPressedThisFrame || kb.digit7Key.wasPressedThisFrame) return 1;
            if (kb.numpad3Key.wasPressedThisFrame || kb.digit8Key.wasPressedThisFrame) return 2;
            if (kb.numpad4Key.wasPressedThisFrame || kb.digit9Key.wasPressedThisFrame) return 3;
            if (kb.numpad5Key.wasPressedThisFrame || kb.digit0Key.wasPressedThisFrame) return 4;
            return -1;
        }
    }
}
