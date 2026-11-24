using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public sealed class AirshipDockingController : NetworkBehaviour
    {
        [SerializeField] private float _dockRange = 3.5f;

        private readonly SyncVar<AirshipDockState> _dockState = new(AirshipDockState.CreateFlying());

        private byte _seatId;
        private TeamId _teamId;
        private MatchContext _context;
        private DockableRegistration _currentTarget;
        private Vector2 _serverDockedMoveAim;
        private bool _serverHasDirectAim;
        private Vector2 _serverDirectAimDir;
        private Vector2 _lastSentDockedMoveAim;
        private Vector2 _lastSentDirectAimDir;

        public AirshipDockState State => _dockState.Value;
        public bool IsDocked => _dockState.Value.IsDocked;
        public Vector2 CurrentAnchorPosition => _dockState.Value.AnchorPosition;
        public float DockRange => _dockRange;

        public event Action<AirshipDockState> DockStateChanged;

        private void Awake()
        {
            _dockState.OnChange += HandleDockStateChanged;
        }

        private void OnDestroy()
        {
            _dockState.OnChange -= HandleDockStateChanged;
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

            ReleaseTargetServer();
            base.OnStopServer();
        }

        public void InitializeServer(byte seatId, TeamId teamId, MatchContext context)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _seatId = seatId;
            _teamId = teamId;
            _context = context;
            _currentTarget = null;
            _dockState.Value = AirshipDockState.CreateFlying();
        }

        public void RequestToggleDock()
        {
            if (!IsOwner || !IsClientInitialized)
            {
                return;
            }

            RequestToggleDockServerRpc();
        }

        public void RequestDockedFire(Vector2 aimDirection)
        {
            if (!IsOwner || !IsClientInitialized || !IsDocked)
            {
                return;
            }

            RequestDockedFireServerRpc(aimDirection);
        }

        public void SubmitDockedAim(Vector2 moveAimInput, bool hasDirectAimDirection, Vector2 directAimDirection)
        {
            if (!IsOwner || !IsClientInitialized || !IsDocked)
            {
                return;
            }

            Vector2 clampedMove = Vector2.ClampMagnitude(moveAimInput, 1f);
            Vector2 normalizedDirect = hasDirectAimDirection && directAimDirection.sqrMagnitude > 0.001f
                ? directAimDirection.normalized
                : Vector2.zero;
            bool sendDirect = hasDirectAimDirection && normalizedDirect.sqrMagnitude > 0.001f;

            bool moveZeroTransition = clampedMove == Vector2.zero && _lastSentDockedMoveAim != Vector2.zero;
            bool moveChanged = moveZeroTransition || (clampedMove - _lastSentDockedMoveAim).sqrMagnitude > 0.0004f;
            bool directChanged = sendDirect && (normalizedDirect - _lastSentDirectAimDir).sqrMagnitude > 0.0004f;

            if (moveChanged || directChanged)
            {
                _lastSentDockedMoveAim = clampedMove;
                if (sendDirect)
                {
                    _lastSentDirectAimDir = normalizedDirect;
                }

                SubmitDockedAimServerRpc(clampedMove, sendDirect, normalizedDirect);
            }
        }

        [ServerRpc]
        private void SubmitDockedAimServerRpc(Vector2 moveAimInput, bool hasDirectAimDirection, Vector2 directAimDirection)
        {
            if (!IsServerInitialized || !IsDocked)
            {
                return;
            }

            _serverDockedMoveAim = Vector2.ClampMagnitude(moveAimInput, 1f);
            _serverHasDirectAim = hasDirectAimDirection && directAimDirection.sqrMagnitude > 0.001f;
            _serverDirectAimDir = _serverHasDirectAim ? directAimDirection.normalized : Vector2.zero;
        }

        [ServerRpc]
        private void RequestDockedFireServerRpc(Vector2 aimDirection)
        {
            if (!IsServerInitialized || !IsDocked || _currentTarget == null || _currentTarget.UnityObject == null)
            {
                return;
            }

            if (TryGetComponent(out AirshipController airship) && !airship.IsAlive)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            if (_currentTarget.Dockable is StructureBase weaponStructure)
            {
                if (weaponStructure.TryHandleManualFireServer(_seatId, new InteractPayload(1, aimDirection)))
                {
                    ForceUndockServer();
                }
                return;
            }

            if (_currentTarget.Dockable is IInteractable interactable)
            {
                interactable.HandleActionServer(_seatId, new InteractPayload(1, aimDirection));
            }
        }

        [ServerRpc]
        private void RequestToggleDockServerRpc()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (IsDocked)
            {
                ForceUndockServer();
                return;
            }

            if (_context == null)
            {
                return;
            }

            if (_context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            if (TryGetComponent(out AirshipController airship) && (!airship.IsAlive || airship.IsCarrying))
            {
                return;
            }

            if (!DockingEvaluator.TryFindBestDockable(
                    transform.position,
                    _seatId,
                    _teamId,
                    _dockRange,
                    _context.Dockables,
                    out DockableRegistration best))
            {
                return;
            }

            best.Dockable.OnAirshipDockedServer(_seatId);
            _currentTarget = best;
            Vector2 anchor = best.Dockable.DockAnchorWorld;
            _dockState.Value = AirshipDockState.CreateDocked(
                AirshipDockMode.DockedToStructure,
                best.NetworkObjectId,
                anchor);

            NotifyDockChangedObserversRpc(true, anchor);
        }

        public void ForceUndockServer()
        {
            if (!IsServerInitialized || !IsDocked)
            {
                return;
            }

            ReleaseTargetServer();
            _dockState.Value = AirshipDockState.CreateFlying();
            NotifyDockChangedObserversRpc(false, transform.position);
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || !IsDocked)
            {
                return;
            }

            if (!DockingEvaluator.IsTargetStillValid(_currentTarget))
            {
                ForceUndockServer();
                return;
            }

            if (_currentTarget?.Dockable is StructureBase structure && structure.HasWeapon)
            {
                if (_serverHasDirectAim && _serverDirectAimDir.sqrMagnitude > 0.001f)
                {
                    structure.HandleManualAimDirectionServer(_seatId, _serverDirectAimDir);
                    _serverHasDirectAim = false;
                }
                else if (_serverDockedMoveAim.sqrMagnitude > 0.001f)
                {
                    float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);
                    structure.HandleManualAimStepServer(_seatId, _serverDockedMoveAim, dt);
                }
            }
        }

        private void ReleaseTargetServer()
        {
            if (_currentTarget != null && _currentTarget.UnityObject != null && _currentTarget.Dockable != null)
            {
                _currentTarget.Dockable.OnAirshipUndockedServer(_seatId);
            }

            _currentTarget = null;
            _serverDockedMoveAim = Vector2.zero;
            _serverHasDirectAim = false;
            _serverDirectAimDir = Vector2.zero;
        }

        [ObserversRpc]
        private void NotifyDockChangedObserversRpc(bool docked, Vector2 anchorPosition)
        {
            // Hook point for dock/undock VFX and SFX in later visual pass.
        }

        private void HandleDockStateChanged(AirshipDockState prev, AirshipDockState next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            DockStateChanged?.Invoke(next);
        }
    }
}
