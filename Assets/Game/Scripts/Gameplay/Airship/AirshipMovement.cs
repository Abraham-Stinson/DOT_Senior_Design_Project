using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    [RequireComponent(typeof(AirshipDockingController))]
    public sealed class AirshipMovement : NetworkBehaviour
    {
        [SerializeField] private float _maxSpeed = 9f;
        [SerializeField] private float _acceleration = 28f;
        [SerializeField] private float _drag = 14f;
        [SerializeField] private float _dockSnapSpeed = 24f;

        private AirshipDockingController _docking;
        private MatchContext _context;
        private Rect _playableBounds = new(-18f, -2f, 36f, 16f);
        private Vector2 _serverMoveInput;
        private Vector2 _serverVelocity;
        private Vector2 _lastSentInput;

        public Vector2 ServerVelocity => _serverVelocity;

        private void Awake()
        {
            _docking = GetComponent<AirshipDockingController>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (TimeManager != null)
            {
                TimeManager.OnTick += HandleServerTick;
            }
        }

        public override void OnStopServer()
        {
            if (TimeManager != null)
            {
                TimeManager.OnTick -= HandleServerTick;
            }

            base.OnStopServer();
        }

        public void InitializeServer(MatchContext context, Rect playableBounds)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _context = context;
            _playableBounds = playableBounds;
            _serverMoveInput = Vector2.zero;
            _serverVelocity = Vector2.zero;
        }

        public void SubmitMoveInput(Vector2 moveInput)
        {
            if (!IsOwner || !IsClientInitialized)
            {
                return;
            }

            Vector2 clamped = Vector2.ClampMagnitude(moveInput, 1f);
            bool zeroTransition = clamped == Vector2.zero && _lastSentInput != Vector2.zero;
            if (zeroTransition || (clamped - _lastSentInput).sqrMagnitude > 0.0004f)
            {
                _lastSentInput = clamped;
                SubmitMoveInputServerRpc(clamped);
            }
        }

        [ServerRpc]
        private void SubmitMoveInputServerRpc(Vector2 moveInput)
        {
            _serverMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);

            if (_docking != null && _docking.IsDocked)
            {
                _serverVelocity = Vector2.zero;
                Vector2 current = transform.position;
                Vector2 next = Vector2.MoveTowards(current, _docking.CurrentAnchorPosition, _dockSnapSpeed * dt);
                transform.position = new Vector3(next.x, next.y, transform.position.z);
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                _serverVelocity = Vector2.zero;
                return;
            }

            Vector2 position = transform.position;
            AirshipKinematics.Step2D(
                ref position,
                ref _serverVelocity,
                _serverMoveInput,
                _maxSpeed,
                _acceleration,
                _drag,
                dt,
                _playableBounds);

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }
    }
}
