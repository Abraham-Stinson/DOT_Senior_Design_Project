using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using Game.Input;
using UnityEngine;

namespace Game.Gameplay
{
    [RequireComponent(typeof(AirshipMovement), typeof(AirshipDockingController))]
    public sealed class AirshipController : NetworkBehaviour, IDamageable
    {
        [SerializeField] private AirshipMovement _movement;
        [SerializeField] private AirshipDockingController _docking;
        [SerializeField] private SpriteRenderer _bodyRenderer;
        [SerializeField] private SpriteRenderer _dockIndicatorRenderer;
        [SerializeField] private float _defaultMaxHealth = 100f;
        [SerializeField] private float _respawnDelaySeconds = 3f;

        private readonly SyncVar<byte> _seatId = new();
        private readonly SyncVar<TeamId> _teamId = new();
        private readonly SyncVar<AirshipCarryState> _carryState = new(AirshipCarryState.Empty);
        private readonly SyncVar<float> _currentHealth = new(100f);
        private readonly SyncVar<float> _maxHealth = new(100f);
        private readonly SyncVar<bool> _isDowned = new(false);

        private MatchContext _context;
        private AirshipPlacementGhostVisual _ghostVisual;
        private WorldHealthBarVisual _healthBarVisual;
        private Vector3 _spawnPosition;
        private float _respawnTimer;
        private int _lastManualFireTick = -100;
        private Vector2 _lastMouseAimScreenPos;
        private bool _clientTickSubscribed;

        public byte SeatId => _seatId.Value;
        public TeamId Team => _teamId.Value;
        public float CurrentHealth => _currentHealth.Value;
        public float MaxHealth => _maxHealth.Value;
        public bool IsAlive => !_isDowned.Value && _currentHealth.Value > 0f;
        public bool IsDowned => _isDowned.Value;
        public AirshipMovement Movement => _movement;
        public AirshipDockingController Docking => _docking;
        public AirshipDockMode DockMode => _docking != null ? _docking.State.Mode : AirshipDockMode.Flying;
        public AirshipCarryState CarryState => _carryState.Value;
        public bool IsCarrying => _carryState.Value.IsCarrying;

        public event Action<AirshipCarryState> CarryStateChanged;
        public event Action<float, float> HealthChanged;
        public event Action<AirshipController> AirshipDownedServer;

        private void Awake()
        {
            if (_movement == null) _movement = GetComponent<AirshipMovement>();
            if (_docking == null) _docking = GetComponent<AirshipDockingController>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInChildren<SpriteRenderer>();

            _seatId.OnChange += HandleIdentityChanged;
            _teamId.OnChange += HandleTeamChanged;
            _carryState.OnChange += HandleCarryStateChanged;
            _currentHealth.OnChange += HandleHealthChanged;
            _maxHealth.OnChange += HandleHealthChanged;
            _isDowned.OnChange += HandleDownedChanged;
            if (_docking != null)
            {
                _docking.DockStateChanged += HandleDockStateChanged;
            }
        }

        private void OnDestroy()
        {
            _seatId.OnChange -= HandleIdentityChanged;
            _teamId.OnChange -= HandleTeamChanged;
            _carryState.OnChange -= HandleCarryStateChanged;
            _currentHealth.OnChange -= HandleHealthChanged;
            _maxHealth.OnChange -= HandleHealthChanged;
            _isDowned.OnChange -= HandleDownedChanged;
            if (_docking != null)
            {
                _docking.DockStateChanged -= HandleDockStateChanged;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (TimeManager != null)
            {
                TimeManager.OnTick += HandleServerTick;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureGhostVisual();
            SubscribeClientTick(true);
            UpdateVisuals();
        }

        public override void OnStopClient()
        {
            SubscribeClientTick(false);
            base.OnStopClient();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            UpdateVisuals();
        }

        public override void OnStopServer()
        {
            if (TimeManager != null)
            {
                TimeManager.OnTick -= HandleServerTick;
            }

            if (IsServerInitialized && _carryState.Value.IsCarrying && _carryState.Value.ReservedGold > 0)
            {
                _context?.Economy?.RefundGoldServer(_carryState.Value.SeatId, _carryState.Value.ReservedGold);
                _carryState.Value = AirshipCarryState.Empty;
            }

            _context?.UnregisterAirship(this);
            base.OnStopServer();
        }

        public void InitializeServer(byte seatId, TeamId teamId, MatchContext context, Rect playableAirspace)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _context = context;
            _spawnPosition = transform.position;
            _seatId.Value = seatId;
            _teamId.Value = teamId;
            _carryState.Value = AirshipCarryState.Empty;
            _maxHealth.Value = Mathf.Max(1f, _defaultMaxHealth);
            _currentHealth.Value = _maxHealth.Value;
            _isDowned.Value = false;
            _respawnTimer = 0f;
            _movement?.InitializeServer(context, playableAirspace);
            _docking?.InitializeServer(seatId, teamId, context);
            _context?.RegisterAirship(this);
            UpdateVisuals();
        }

        public void ApplyDamageServer(in DamageInfo damage)
        {
            if (!IsServerInitialized || !IsAlive || damage.Amount <= 0f)
            {
                return;
            }

            _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - damage.Amount);
            if (_currentHealth.Value <= 0f)
            {
                _isDowned.Value = true;
                _respawnTimer = Mathf.Max(0.25f, _respawnDelaySeconds);
                _docking?.ForceUndockServer();

                if (_carryState.Value.IsCarrying)
                {
                    if (_carryState.Value.ReservedGold > 0)
                    {
                        _context?.Economy?.RefundGoldServer(_carryState.Value.SeatId, _carryState.Value.ReservedGold);
                    }

                    ClearCarryStateServer();
                }

                AirshipDownedServer?.Invoke(this);
            }

            UpdateVisuals();
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || !_isDowned.Value)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f)
            {
                _isDowned.Value = false;
                _currentHealth.Value = _maxHealth.Value;
                transform.position = _spawnPosition;
                UpdateVisuals();
            }
        }

        public void SetCarryStateServer(AirshipCarryState state)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (state.IsCarrying && _docking != null && _docking.IsDocked)
            {
                _docking.ForceUndockServer();
            }

            _carryState.Value = state;
            _ghostVisual?.RefreshVisuals();
        }

        public void ClearCarryStateServer()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _carryState.Value = AirshipCarryState.Empty;
            _ghostVisual?.RefreshVisuals();
        }

        private void EnsureGhostVisual()
        {
            if (_ghostVisual == null && !TryGetComponent(out _ghostVisual))
            {
                _ghostVisual = gameObject.AddComponent<AirshipPlacementGhostVisual>();
            }

            _ghostVisual.Attach(this);
        }

        private void SubscribeClientTick(bool subscribe)
        {
            if (TimeManager == null)
            {
                return;
            }

            if (subscribe && !_clientTickSubscribed)
            {
                TimeManager.OnTick += HandleClientTick;
                _clientTickSubscribed = true;
            }
            else if (!subscribe && _clientTickSubscribed)
            {
                TimeManager.OnTick -= HandleClientTick;
                _clientTickSubscribed = false;
            }
        }

        private void HandleClientTick()
        {
            if (!IsClientInitialized || !IsOwner)
            {
                return;
            }

            IPlayerInputRouter router = ActiveInputRouterRegistry.Current;
            IPlayerInputChannel channel = router?.GetChannelForSeat(_seatId.Value);
            if (channel == null)
            {
                return;
            }

            PlayerInputSnapshot input = channel.ConsumeTickSnapshot();
            if (!IsAlive)
            {
                _movement?.SubmitMoveInput(Vector2.zero);
                return;
            }

            bool isDockedAndControlling = _docking != null && _docking.IsDocked && !_carryState.Value.IsCarrying;
            if (isDockedAndControlling)
            {
                _movement?.SubmitMoveInput(Vector2.zero);

                bool hasDirectAim = false;
                Vector2 directAimDir = Vector2.zero;

                if (input.Move.sqrMagnitude <= 0.01f)
                {
                    if (input.Aim.sqrMagnitude > 0.04f && input.Aim.sqrMagnitude <= 2.25f)
                    {
                        hasDirectAim = true;
                        directAimDir = input.Aim.normalized;
                    }
                    else if (router.RoutingMode == InputRoutingMode.SinglePlayer && input.Aim.sqrMagnitude > 4f && Camera.main != null)
                    {
                        if ((input.Aim - _lastMouseAimScreenPos).sqrMagnitude > 2f)
                        {
                            _lastMouseAimScreenPos = input.Aim;
                            Vector3 worldPt = Camera.main.ScreenToWorldPoint(
                                new Vector3(input.Aim.x, input.Aim.y, -Camera.main.transform.position.z));
                            Vector2 dir = (Vector2)worldPt - (Vector2)transform.position;
                            if (dir.sqrMagnitude > 0.04f)
                            {
                                hasDirectAim = true;
                                directAimDir = dir.normalized;
                            }
                        }
                    }
                }

                _docking.SubmitDockedAim(input.Move, hasDirectAim, directAimDir);

                if (input.DockPressed)
                {
                    _docking.RequestToggleDock();
                    return;
                }

                if (input.SelectedBuildSlot >= 0 && input.SelectedBuildSlot < DeckRules.DeckSize)
                {
                    ActiveBuildControllerRegistry.Current?.RequestBeginCarryFromSlot(_seatId.Value, input.SelectedBuildSlot);
                    return;
                }

                if (input.FireHeld || input.ConfirmPlacePressed)
                {
                    int tick = TimeManager != null ? (int)TimeManager.Tick : 0;
                    if (tick == 0 || tick - _lastManualFireTick >= 4)
                    {
                        _lastManualFireTick = tick;
                        _docking.RequestDockedFire(hasDirectAim ? directAimDir : Vector2.zero);
                    }
                }

                return;
            }

            _movement?.SubmitMoveInput(input.Move);

            if (input.DockPressed && !_carryState.Value.IsCarrying)
            {
                _docking?.RequestToggleDock();
            }

            if (_carryState.Value.IsCarrying && input.CancelCarryPressed)
            {
                ActiveBuildControllerRegistry.Current?.RequestCancelCarry(_seatId.Value);
            }
            else if (_carryState.Value.IsCarrying && input.ConfirmPlacePressed)
            {
                ActiveBuildControllerRegistry.Current?.RequestDeployCarried(_seatId.Value, transform.position);
            }
            else if (input.SelectedBuildSlot >= 0 && input.SelectedBuildSlot < DeckRules.DeckSize)
            {
                if (_carryState.Value.IsCarrying && _carryState.Value.SlotIndex == input.SelectedBuildSlot)
                {
                    ActiveBuildControllerRegistry.Current?.RequestDeployCarried(_seatId.Value, transform.position);
                }
                else
                {
                    ActiveBuildControllerRegistry.Current?.RequestBeginCarryFromSlot(_seatId.Value, input.SelectedBuildSlot);
                }
            }
        }

        private void HandleCarryStateChanged(AirshipCarryState prev, AirshipCarryState next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            _ghostVisual?.RefreshVisuals();
            CarryStateChanged?.Invoke(next);
        }

        private void HandleIdentityChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && IsHostInitialized)
            {
                return;
            }

            UpdateVisuals();
        }

        private void HandleTeamChanged(TeamId prev, TeamId next, bool asServer)
        {
            if (asServer && IsHostInitialized)
            {
                return;
            }

            UpdateVisuals();
        }

        private void HandleDockStateChanged(AirshipDockState state)
        {
            UpdateVisuals();
        }

        private void HandleHealthChanged(float prev, float next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            UpdateHealthBarVisual();
            HealthChanged?.Invoke(_currentHealth.Value, _maxHealth.Value);
        }

        private void HandleDownedChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_bodyRenderer != null)
            {
                Color baseColor = _teamId.Value == TeamId.TeamA
                    ? new Color(0.28f, 0.72f, 1f)
                    : (_teamId.Value == TeamId.TeamB ? new Color(1f, 0.42f, 0.36f) : Color.white);
                if (_isDowned.Value)
                {
                    baseColor.a = 0.25f;
                }

                _bodyRenderer.color = baseColor;
            }

            if (_dockIndicatorRenderer != null)
            {
                bool docked = IsAlive && _docking != null && _docking.IsDocked;
                _dockIndicatorRenderer.enabled = docked;
                _dockIndicatorRenderer.color = new Color(1f, 0.9f, 0.25f, 0.9f);
            }

            UpdateHealthBarVisual();
        }

        private void UpdateHealthBarVisual()
        {
            bool damaged = IsAlive && _maxHealth.Value > 0.01f && _currentHealth.Value < _maxHealth.Value - 0.01f;
            if (!damaged && _healthBarVisual == null)
            {
                return;
            }

            if (_healthBarVisual == null)
            {
                var barGo = new GameObject("AirshipHealthBar");
                barGo.transform.SetParent(transform, false);
                _healthBarVisual = barGo.AddComponent<WorldHealthBarVisual>();
            }

            _healthBarVisual.SetHealth(_currentHealth.Value, _maxHealth.Value, yOffset: 0.75f, width: 1.4f, alwaysShow: false);
        }
    }
}
