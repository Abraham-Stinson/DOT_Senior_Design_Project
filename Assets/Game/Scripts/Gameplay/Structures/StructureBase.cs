using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Base networked structure supporting health/damage, airship docking, territory expansion (Balloon),
    /// passive income bonuses (Gold Mine), and weapon control/interaction (Cannon, Flak, Mortar, Laser).
    /// </summary>
    public sealed class StructureBase : NetworkBehaviour, IStructure, IInteractable
    {
        [SerializeField] private SpriteRenderer _bodyRenderer;
        [SerializeField] private SpriteRenderer _territoryRingRenderer;
        [SerializeField] private StructureWeaponController _weaponController;
        [SerializeField] private Vector2 _dockAnchorOffset = new(0f, 1.4f);

        private readonly SyncVar<ushort> _cardId = new();
        private readonly SyncVar<byte> _ownerSeatId = new();
        private readonly SyncVar<TeamId> _teamId = new();
        private readonly SyncVar<StructureCategory> _category = new();
        private readonly SyncVar<float> _currentHealth = new();
        private readonly SyncVar<float> _maxHealth = new();
        private readonly SyncVar<bool> _isConstructed = new();
        private readonly SyncVar<bool> _isOccupied = new();
        private readonly SyncVar<float> _territoryRadius = new();
        private readonly SyncVar<float> _incomeBonus = new();
        private readonly SyncVar<Vector2> _footprintSize = new(new Vector2(1.8f, 1.4f));

        private MatchContext _context;
        private byte _dockedSeatId = byte.MaxValue;
        private int _registeredSourceId;
        private bool _hasTerritoryRegistration;
        private bool _hasIncomeRegistration;
        private Sprite _defaultBodySprite;
        private TerritoryCircleVisual _standaloneTerritoryVisual;
        private WorldHealthBarVisual _healthBarVisual;
        private IGoldDeposit _boundGoldDeposit;

        private static Texture2D s_roundBodyTexture;
        private static Sprite s_roundBodySprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticRoundVisualCache()
        {
            s_roundBodySprite = null;
            s_roundBodyTexture = null;
        }

        public ushort CardId => _cardId.Value;
        public byte OwnerSeatId => _ownerSeatId.Value;
        public TeamId Team => _teamId.Value;
        public StructureCategory Category => _category.Value;
        public float CurrentHealth => _currentHealth.Value;
        public float MaxHealth => _maxHealth.Value;
        public bool IsAlive => _isConstructed.Value && _currentHealth.Value > 0f;
        public bool IsConstructed => _isConstructed.Value;
        public bool IsOccupied => _isOccupied.Value;
        public float TerritoryRadius => _territoryRadius.Value;
        public float IncomeBonus => _incomeBonus.Value;
        public StructureWeaponController WeaponController => _weaponController;
        public bool HasWeapon => _weaponController != null && _weaponController.HasWeapon;
        public bool IsWeaponReadyForControl => HasWeapon && !_weaponController.IsOnCooldown;
        public Vector2 DockAnchorWorld => (Vector2)transform.position + _dockAnchorOffset;
        public Bounds FootprintBounds => new(transform.position, new Vector3(_footprintSize.Value.x, _footprintSize.Value.y, 1f));

        public event Action<float, float> HealthChanged;
        public event Action<StructureBase> DestroyedServer;

        private void Awake()
        {
            if (_bodyRenderer == null)
            {
                _bodyRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (_weaponController == null)
            {
                _weaponController = GetComponent<StructureWeaponController>();
            }

            if (_bodyRenderer != null)
            {
                _defaultBodySprite = _bodyRenderer.sprite;
            }

            if (_territoryRingRenderer != null)
            {
                _territoryRingRenderer.enabled = false;
            }

            _teamId.OnChange += HandleTeamOrIdentityChanged;
            _category.OnChange += HandleCategoryChanged;
            _currentHealth.OnChange += HandleHealthSyncChanged;
            _maxHealth.OnChange += HandleHealthSyncChanged;
            _isOccupied.OnChange += HandleOccupiedChanged;
            _territoryRadius.OnChange += HandleFloatVisualChanged;
            _footprintSize.OnChange += HandleFootprintChanged;
        }

        private void OnDestroy()
        {
            _teamId.OnChange -= HandleTeamOrIdentityChanged;
            _category.OnChange -= HandleCategoryChanged;
            _currentHealth.OnChange -= HandleHealthSyncChanged;
            _maxHealth.OnChange -= HandleHealthSyncChanged;
            _isOccupied.OnChange -= HandleOccupiedChanged;
            _territoryRadius.OnChange -= HandleFloatVisualChanged;
            _footprintSize.OnChange -= HandleFootprintChanged;
            CleanupServerRegistrations();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            UpdateVisuals();
        }

        public override void OnStopServer()
        {
            CleanupServerRegistrations();
            base.OnStopServer();
        }

        public void InitializeServer(
            ushort cardId,
            byte ownerSeatId,
            TeamId team,
            StructureCategory category,
            float maxHealth,
            Vector2 footprintSize,
            float territoryRadius,
            float incomeBonus,
            MatchContext context)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _context = context;
            _cardId.Value = cardId;
            _ownerSeatId.Value = ownerSeatId;
            _teamId.Value = team;
            _category.Value = category;
            _maxHealth.Value = Mathf.Max(1f, maxHealth);
            _currentHealth.Value = _maxHealth.Value;
            _footprintSize.Value = new Vector2(Mathf.Max(0.25f, footprintSize.x), Mathf.Max(0.25f, footprintSize.y));
            _territoryRadius.Value = Mathf.Max(0f, territoryRadius);
            _incomeBonus.Value = Mathf.Max(0f, incomeBonus);
            _isConstructed.Value = true;
            _isOccupied.Value = false;
            _dockedSeatId = byte.MaxValue;

            _registeredSourceId = NetworkObject != null ? NetworkObject.ObjectId : GetInstanceID();

            if (_context != null)
            {
                _context.RegisterStructure(this);
                _context.RegisterDockable(this, this, NetworkObject);

                if (_territoryRadius.Value > 0f && _context.Territory != null)
                {
                    _context.Territory.AddOrUpdateZoneServer(_registeredSourceId, team, transform.position, _territoryRadius.Value);
                    _hasTerritoryRegistration = true;
                }

                if (_incomeBonus.Value > 0f && _context.Economy != null)
                {
                    _context.Economy.RegisterIncomeSourceServer(ownerSeatId, _registeredSourceId, _incomeBonus.Value);
                    _hasIncomeRegistration = true;
                }
            }

            UpdateVisuals();
        }

        public void BindGoldDepositServer(IGoldDeposit deposit)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _boundGoldDeposit = deposit;
        }

        public void InitializeWeaponServer(WeaponConfig config, BallisticProjectile projectilePrefab = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (_weaponController == null)
            {
                _weaponController = GetComponent<StructureWeaponController>();
            }

            _weaponController?.InitializeWeaponServer(this, _context, config, projectilePrefab);
        }

        public bool CanInteract(byte seatId, TeamId team)
        {
            return IsServerInitialized
                && IsAlive
                && team != TeamId.Neutral
                && team == _teamId.Value
                && _weaponController != null
                && _weaponController.HasWeapon;
        }

        public void HandleActionServer(byte seatId, in InteractPayload payload)
        {
            TryHandleManualFireServer(seatId, payload);
        }

        public void HandleManualAimStepServer(byte seatId, Vector2 aimMoveInput, float dt)
        {
            if (!CanInteract(seatId, _teamId.Value))
            {
                return;
            }

            _weaponController.ApplyManualAimStepServer(aimMoveInput, dt);
        }

        public void HandleManualAimDirectionServer(byte seatId, Vector2 aimDirection)
        {
            if (!CanInteract(seatId, _teamId.Value))
            {
                return;
            }

            _weaponController.SetManualAimDirectionServer(aimDirection);
        }

        public bool TryHandleManualFireServer(byte seatId, in InteractPayload payload)
        {
            if (!CanInteract(seatId, _teamId.Value))
            {
                return false;
            }

            return _weaponController.HandleManualInteractServer(seatId, payload);
        }

        public void ApplyDamageServer(in DamageInfo damage)
        {
            if (!IsServerInitialized || !IsAlive || damage.Amount <= 0f)
            {
                return;
            }

            _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - damage.Amount);
            UpdateHealthBarVisual();

            if (_currentHealth.Value <= 0f)
            {
                _isConstructed.Value = false;
                DestroyedServer?.Invoke(this);
                CleanupServerRegistrations();
                Despawn();
            }
        }

        public bool CanDock(byte seatId, TeamId team)
        {
            if (!IsServerInitialized || !IsAlive || team == TeamId.Neutral || team != _teamId.Value)
            {
                return false;
            }

            if (HasWeapon && _weaponController.IsOnCooldown)
            {
                return false;
            }

            return !_isOccupied.Value || _dockedSeatId == seatId;
        }

        public void OnAirshipDockedServer(byte seatId)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _dockedSeatId = seatId;
            _isOccupied.Value = true;
        }

        public void OnAirshipUndockedServer(byte seatId)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (_dockedSeatId == seatId)
            {
                _dockedSeatId = byte.MaxValue;
                _isOccupied.Value = false;
            }
        }

        private void CleanupServerRegistrations()
        {
            if (_context == null)
            {
                return;
            }

            _context.UnregisterStructure(this);
            _context.UnregisterDockable(this);

            if (_hasTerritoryRegistration)
            {
                _hasTerritoryRegistration = false;
                _context.Territory?.RemoveZoneServer(_registeredSourceId);
            }

            if (_hasIncomeRegistration)
            {
                _hasIncomeRegistration = false;
                _context.Economy?.UnregisterIncomeSourceServer(_ownerSeatId.Value, _registeredSourceId);
            }

            if (_boundGoldDeposit != null)
            {
                _boundGoldDeposit.ReleaseServer(_registeredSourceId);
                _boundGoldDeposit = null;
            }
        }

        private bool ShouldSuppressServerDuplicate()
        {
            return IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted);
        }

        private void HandleTeamOrIdentityChanged(TeamId prev, TeamId next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateVisuals();
        }

        private void HandleCategoryChanged(StructureCategory prev, StructureCategory next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateVisuals();
        }

        private void HandleOccupiedChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateVisuals();
            _weaponController?.RefreshVisuals();
        }

        private void HandleFloatVisualChanged(float prev, float next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateVisuals();
        }

        private void HandleFootprintChanged(Vector2 prev, Vector2 next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateVisuals();
        }

        private void HandleHealthSyncChanged(float prev, float next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate()) return;
            UpdateHealthBarVisual();
            HealthChanged?.Invoke(_currentHealth.Value, _maxHealth.Value);
        }

        private void UpdateVisuals()
        {
            Color teamColor = _teamId.Value == TeamId.TeamA
                ? new Color(0.25f, 0.62f, 0.96f)
                : (_teamId.Value == TeamId.TeamB ? new Color(0.96f, 0.36f, 0.34f) : Color.gray);

            if (_bodyRenderer != null)
            {
                if (_category.Value == StructureCategory.Territory)
                {
                    _bodyRenderer.sprite = GetOrCreateRoundBodySprite();
                }
                else if (_defaultBodySprite != null)
                {
                    _bodyRenderer.sprite = _defaultBodySprite;
                }

                Color categoryTint = _category.Value switch
                {
                    StructureCategory.Core => Color.Lerp(teamColor, new Color(0.95f, 0.92f, 0.72f), 0.28f),
                    StructureCategory.Territory => Color.Lerp(teamColor, Color.cyan, 0.35f),
                    StructureCategory.Economy => Color.Lerp(teamColor, new Color(1f, 0.85f, 0.25f), 0.55f),
                    StructureCategory.Defense => Color.Lerp(teamColor, Color.white, 0.25f),
                    _ => teamColor
                };

                _bodyRenderer.color = _isOccupied.Value
                    ? Color.Lerp(categoryTint, Color.yellow, 0.35f)
                    : categoryTint;

                _bodyRenderer.transform.localScale = new Vector3(_footprintSize.Value.x, _footprintSize.Value.y, 1f);
            }

            UpdateHealthBarVisual();

            if (_territoryRingRenderer != null)
            {
                _territoryRingRenderer.enabled = false;
            }

            // TerritoryController draws all replicated circular zones cleanly during a match;
            // only fall back to a local structure circle when spawned standalone without one.
            bool hasTerritoryController = ActiveTerritoryControllerRegistry.Current != null
                || (_context != null && _context.Territory != null);

            if (!hasTerritoryController && _territoryRadius.Value > 0.001f)
            {
                if (_standaloneTerritoryVisual == null)
                {
                    var ringGo = new GameObject("StandaloneTerritoryCircle");
                    ringGo.transform.SetParent(transform, false);
                    _standaloneTerritoryVisual = ringGo.AddComponent<TerritoryCircleVisual>();
                }

                _standaloneTerritoryVisual.Configure(
                    transform.position,
                    _territoryRadius.Value,
                    teamColor,
                    fillAlpha: 0.12f,
                    borderAlpha: 0.72f,
                    borderWidth: 0.07f);
            }
            else if (_standaloneTerritoryVisual != null)
            {
                _standaloneTerritoryVisual.gameObject.SetActive(false);
            }
        }

        private void UpdateHealthBarVisual()
        {
            bool alwaysShow = _category.Value == StructureCategory.Core;
            bool shouldExist = alwaysShow || (_maxHealth.Value > 0.01f && _currentHealth.Value > 0.001f && _currentHealth.Value < _maxHealth.Value - 0.01f);
            if (!shouldExist && _healthBarVisual == null)
            {
                return;
            }

            if (_healthBarVisual == null)
            {
                var barGo = new GameObject("StructureHealthBar");
                barGo.transform.SetParent(transform, false);
                _healthBarVisual = barGo.AddComponent<WorldHealthBarVisual>();
            }

            float yOffset = _footprintSize.Value.y * 0.5f + 0.38f;
            float width = Mathf.Max(1.4f, _footprintSize.Value.x * 0.95f);
            _healthBarVisual.SetHealth(_currentHealth.Value, _maxHealth.Value, yOffset, width, alwaysShow);
        }

        private static Sprite GetOrCreateRoundBodySprite()
        {
            if (s_roundBodySprite != null)
            {
                return s_roundBodySprite;
            }

            const int size = 64;
            s_roundBodyTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            float center = (size - 1) * 0.5f;
            float radius = size * 0.46f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((radius + 0.75f - dist) / 1.5f);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            s_roundBodyTexture.SetPixels32(pixels);
            s_roundBodyTexture.Apply();

            s_roundBodySprite = Sprite.Create(
                s_roundBodyTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            s_roundBodySprite.hideFlags = HideFlags.DontSave;
            return s_roundBodySprite;
        }
    }
}
