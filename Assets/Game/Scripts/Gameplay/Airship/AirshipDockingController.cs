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
        private uint _enteredWeaponControlServerTick;

        public AirshipDockState State => _dockState.Value;
        public bool IsDocked => _dockState.Value.IsDocked;
        public bool IsDockedAtBase => IsDocked && _dockState.Value.TargetKind == DockedTargetKind.Base;
        public bool IsControllingWeapon => IsDocked && _dockState.Value.SubMode == DockedInteractionSubMode.WeaponManualControl;
        public DockedInteractionSubMode SubMode => _dockState.Value.SubMode;
        public Vector2 CurrentAnchorPosition => _dockState.Value.AnchorPosition;
        public float DockRange => _dockRange;

        public float TargetWeaponCooldownRemaining =>
            IsDocked && _currentTarget?.UnityObject is StructureBase structure && structure.WeaponController != null
                ? structure.WeaponController.CooldownRemaining
                : 0f;

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

        public void RequestCycleDockedSelection(sbyte delta)
        {
            if (!IsOwner || !IsClientInitialized || !IsDocked || delta == 0)
            {
                return;
            }

            RequestCycleDockedSelectionServerRpc(delta);
        }

        public void RequestConfirmDockedSelection()
        {
            if (!IsOwner || !IsClientInitialized || !IsDocked)
            {
                return;
            }

            RequestConfirmDockedSelectionServerRpc();
        }

        public void RequestExitWeaponControl()
        {
            if (!IsOwner || !IsClientInitialized || !IsControllingWeapon)
            {
                return;
            }

            RequestExitWeaponControlServerRpc();
        }

        public void RequestDockedFire(Vector2 aimDirection)
        {
            if (!IsOwner || !IsClientInitialized || !IsControllingWeapon)
            {
                return;
            }

            RequestDockedFireServerRpc(aimDirection);
        }

        public void SubmitDockedAim(Vector2 moveAimInput, bool hasDirectAimDirection, Vector2 directAimDirection)
        {
            if (!IsOwner || !IsClientInitialized || !IsControllingWeapon)
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
            if (!IsServerInitialized || !IsControllingWeapon)
            {
                return;
            }

            _serverDockedMoveAim = Vector2.ClampMagnitude(moveAimInput, 1f);
            _serverHasDirectAim = hasDirectAimDirection && directAimDirection.sqrMagnitude > 0.001f;
            _serverDirectAimDir = _serverHasDirectAim ? directAimDirection.normalized : Vector2.zero;
        }

        [ServerRpc]
        private void RequestCycleDockedSelectionServerRpc(sbyte delta)
        {
            if (!IsServerInitialized || !IsDocked || delta == 0)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            AirshipDockState next = _dockState.Value;
            if (next.SubMode == DockedInteractionSubMode.BaseDeckSelect)
            {
                int slotCount = GetSeatLoadoutCountServer(_seatId);
                next.SelectedDeckSlot = DockedMenuNavigator.CycleDeckSlot(next.SelectedDeckSlot, delta, slotCount);
                next.TargetCardId = ResolveSeatLoadoutCardServer(_seatId, next.SelectedDeckSlot);
                _dockState.Value = next;
            }
            else if (next.SubMode == DockedInteractionSubMode.StructureActionSelect)
            {
                next.SelectedAction = DockedMenuNavigator.CycleStructureAction(next.SelectedAction, delta, next.SupportsUseAction);
                _dockState.Value = next;
            }
        }

        [ServerRpc]
        private void RequestConfirmDockedSelectionServerRpc()
        {
            if (!IsServerInitialized || !IsDocked || _context == null)
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

            AirshipDockState current = _dockState.Value;
            if (current.SubMode == DockedInteractionSubMode.BaseDeckSelect)
            {
                _context.BuildController?.BeginCarryFromDockedBaseServer(_seatId, current.SelectedDeckSlot, Owner);
                return;
            }

            if (current.SubMode != DockedInteractionSubMode.StructureActionSelect)
            {
                return;
            }

            if (_currentTarget?.Dockable is not StructureBase targetStructure || !targetStructure.IsAlive)
            {
                ForceUndockServer();
                return;
            }

            if (current.SelectedAction == DockedStructureAction.Use)
            {
                if (current.SupportsUseAction && targetStructure.IsWeaponReadyForControl)
                {
                    targetStructure.SetWeaponControlActiveServer(true);
                    _enteredWeaponControlServerTick = TimeManager != null ? TimeManager.Tick : 0u;
                    current.SubMode = DockedInteractionSubMode.WeaponManualControl;
                    current.CanUseTarget = true;
                    _dockState.Value = current;
                }
                return;
            }

            if (current.SelectedAction == DockedStructureAction.Sell)
            {
                if (!StructureSellRules.CanSellStructure(targetStructure.Category, targetStructure.IsAlive, targetStructure.Team, _teamId))
                {
                    return;
                }

                // Undock cleanly before despawning so OnAirshipUndockedServer and SyncVar updates stay valid.
                ForceUndockServer();
                if (_context.BuildController != null)
                {
                    _context.BuildController.TrySellStructureServer(_seatId, targetStructure, Owner, out _);
                }
                else
                {
                    targetStructure.SellAndDespawnServer();
                }
            }
        }

        [ServerRpc]
        private void RequestExitWeaponControlServerRpc()
        {
            if (!IsServerInitialized || !IsControllingWeapon)
            {
                return;
            }

            bool canUse = false;
            if (_currentTarget?.Dockable is StructureBase structure)
            {
                structure.SetWeaponControlActiveServer(false);
                canUse = structure.IsWeaponReadyForControl;
            }

            _serverDockedMoveAim = Vector2.zero;
            _serverHasDirectAim = false;
            _serverDirectAimDir = Vector2.zero;

            AirshipDockState next = _dockState.Value;
            next.SubMode = DockedInteractionSubMode.StructureActionSelect;
            next.CanUseTarget = canUse;
            _dockState.Value = next;
        }

        [ServerRpc]
        private void RequestDockedFireServerRpc(Vector2 aimDirection)
        {
            if (!IsServerInitialized || !IsControllingWeapon || _currentTarget == null || _currentTarget.UnityObject == null)
            {
                return;
            }

            if (TimeManager != null && _enteredWeaponControlServerTick != 0u && TimeManager.Tick == _enteredWeaponControlServerTick)
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
                    weaponStructure.SetWeaponControlActiveServer(false);
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

            if (best.Dockable is StructureBase structure && structure.Category != StructureCategory.Core)
            {
                bool supportsUse = StructureInteractionRules.SupportsUseAction(structure.Category);
                DockedStructureAction initialAction = StructureInteractionRules.GetDefaultDockedAction(structure.Category);
                int sellRefund = _context.BuildController != null
                    ? _context.BuildController.ComputeSellRefundForCard(structure.CardId)
                    : 0;
                _dockState.Value = AirshipDockState.CreateDockedInteraction(
                    AirshipDockMode.DockedToStructure,
                    best.NetworkObjectId,
                    anchor,
                    DockedTargetKind.NonBaseStructure,
                    DockedInteractionSubMode.StructureActionSelect,
                    selectedDeckSlot: 0,
                    selectedAction: initialAction,
                    targetCardId: structure.CardId,
                    sellRefundGold: sellRefund,
                    canUseTarget: supportsUse && structure.IsWeaponReadyForControl,
                    supportsUseAction: supportsUse);
            }
            else
            {
                ushort slot0Card = ResolveSeatLoadoutCardServer(_seatId, 0);
                _dockState.Value = AirshipDockState.CreateDockedInteraction(
                    AirshipDockMode.DockedToStructure,
                    best.NetworkObjectId,
                    anchor,
                    DockedTargetKind.Base,
                    DockedInteractionSubMode.BaseDeckSelect,
                    selectedDeckSlot: 0,
                    selectedAction: DockedStructureAction.Use,
                    targetCardId: slot0Card,
                    sellRefundGold: 0,
                    canUseTarget: false,
                    supportsUseAction: false);
            }

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

            if (_currentTarget?.Dockable is StructureBase structure)
            {
                if (SubMode == DockedInteractionSubMode.StructureActionSelect)
                {
                    bool canUseNow = _dockState.Value.SupportsUseAction && structure.IsWeaponReadyForControl;
                    if (canUseNow != _dockState.Value.CanUseTarget)
                    {
                        AirshipDockState updated = _dockState.Value;
                        updated.CanUseTarget = canUseNow;
                        _dockState.Value = updated;
                    }
                }
                else if (IsControllingWeapon && structure.HasWeapon)
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
        }

        private void ReleaseTargetServer()
        {
            if (_currentTarget != null && _currentTarget.UnityObject != null && _currentTarget.Dockable != null)
            {
                if (_currentTarget.Dockable is StructureBase structure)
                {
                    structure.SetWeaponControlActiveServer(false);
                }

                _currentTarget.Dockable.OnAirshipUndockedServer(_seatId);
            }

            _currentTarget = null;
            _serverDockedMoveAim = Vector2.zero;
            _serverHasDirectAim = false;
            _serverDirectAimDir = Vector2.zero;
        }

        private int GetSeatLoadoutCountServer(byte seatId)
        {
            if (_context != null && _context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null && seat.ActiveLoadout.Count > 0)
            {
                return seat.ActiveLoadout.Count;
            }

            return DeckRules.DeckSize;
        }

        private ushort ResolveSeatLoadoutCardServer(byte seatId, int slotIndex)
        {
            if (_context != null
                && _context.TryGetSeat(seatId, out PlayerSeatController seat)
                && seat != null
                && slotIndex >= 0
                && slotIndex < seat.ActiveLoadout.Count)
            {
                return seat.ActiveLoadout[slotIndex];
            }

            return 0;
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
