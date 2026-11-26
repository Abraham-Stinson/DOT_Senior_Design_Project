using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "StructureCard", menuName = "Game/Cards/Structure Card")]
    public sealed class StructureCardSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private ushort _cardId;
        [SerializeField] private string _displayName;
        [SerializeField] private Sprite _icon;
        [SerializeField] private StructureCategory _category = StructureCategory.Offense;

        [Header("Deck & Meta Rules")]
        [Tooltip("True only for the Territory Balloon card permanently locked into Slot 0.")]
        [SerializeField] private bool _isLockedInSlotZero;
        [Tooltip("True for cards that players can freely draft into Slots 1..4.")]
        [SerializeField] private bool _isDeckSelectable = true;
        [SerializeField] private bool _unlockedByDefault;
        [SerializeField] private int _unlockCostSoftCurrency = 100;

        [Header("In-Match Stats")]
        [SerializeField] private int _buildCostGold = 100;
        [SerializeField] private float _buildDurationSeconds = 2f;
        [SerializeField] private float _buildCooldownSeconds = 1f;
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private Vector2 _footprintSize = new(2f, 2f);
        [SerializeField] private float _territoryExpansionRadius = 0f;
        [SerializeField] private float _incomePerSecondBonus = 0f;
        [SerializeField] private bool _requiresGroundSupport = true;
        [SerializeField] private bool _requiresGoldDeposit = false;

        [Header("Weapon Stats")]
        [SerializeField] private bool _canFireWeapon = false;
        [SerializeField] private float _weaponDamage = 0f;
        [SerializeField] private float _weaponFireCooldownSeconds = 10f;
        [SerializeField] private float _weaponRange = 22f;
        [SerializeField] private float _projectileSpeed = 19f;
        [SerializeField] private float _projectileGravityScale = 1f;
        [SerializeField] private float _splashRadius = 0f;
        [SerializeField] private bool _canTargetAirships = false;
        [SerializeField] private bool _preferHighArc = false;
        [SerializeField] private float _minAimAngleDegrees = 5f;
        [SerializeField] private float _maxAimAngleDegrees = 85f;

        [Header("Prefab")]
        [SerializeField] private NetworkObject _structurePrefab;

        public ushort CardId => _cardId;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public StructureCategory Category => _category;
        public bool IsLockedInSlotZero => _isLockedInSlotZero;
        public bool IsDeckSelectable => _isDeckSelectable;
        public bool UnlockedByDefault => _unlockedByDefault;
        public int UnlockCostSoftCurrency => _unlockCostSoftCurrency;
        public int BuildCostGold => _buildCostGold;
        public float BuildDurationSeconds => _buildDurationSeconds;
        public float BuildCooldownSeconds => _buildCooldownSeconds;
        public float MaxHealth => _maxHealth;
        public Vector2 FootprintSize => _footprintSize;
        public float TerritoryExpansionRadius => _territoryExpansionRadius;
        public float IncomePerSecondBonus => _incomePerSecondBonus;
        public bool RequiresGroundSupport => _requiresGroundSupport;
        public bool RequiresGoldDeposit => _requiresGoldDeposit;
        public bool CanFireWeapon => _canFireWeapon;
        public float WeaponDamage => _weaponDamage;
        public float WeaponFireCooldownSeconds => _weaponFireCooldownSeconds;
        public float WeaponRange => _weaponRange;
        public float ProjectileSpeed => _projectileSpeed;
        public float ProjectileGravityScale => _projectileGravityScale;
        public float SplashRadius => _splashRadius;
        public bool CanTargetAirships => _canTargetAirships;
        public bool PreferHighArc => _preferHighArc;
        public float MinAimAngleDegrees => _minAimAngleDegrees;
        public float MaxAimAngleDegrees => _maxAimAngleDegrees;
        public NetworkObject StructurePrefab => _structurePrefab;

        public CardRuleMetadata ToRuleMetadata()
        {
            return new CardRuleMetadata(_cardId, _isLockedInSlotZero, _isDeckSelectable, _unlockedByDefault);
        }

        public WeaponConfig ToWeaponConfig()
        {
            if (!_canFireWeapon)
            {
                return WeaponConfig.None;
            }

            float minAngle = Mathf.Min(_minAimAngleDegrees, _maxAimAngleDegrees);
            float maxAngle = Mathf.Max(minAngle, _maxAimAngleDegrees);
            return new WeaponConfig
            {
                CanFire = true,
                Damage = Mathf.Max(0f, _weaponDamage),
                FireCooldownSeconds = Mathf.Max(0.1f, _weaponFireCooldownSeconds),
                Range = Mathf.Max(0f, _weaponRange),
                ProjectileSpeed = Mathf.Max(0.5f, _projectileSpeed),
                GravityScale = Mathf.Max(0f, _projectileGravityScale),
                SplashRadius = Mathf.Max(0f, _splashRadius),
                CanTargetStructures = true,
                CanTargetAirships = false,
                PreferHighArc = _preferHighArc,
                MinAimAngleDegrees = minAngle,
                MaxAimAngleDegrees = maxAngle
            };
        }

        public void ConfigureForTest(
            ushort cardId,
            string displayName,
            StructureCategory category,
            bool isLockedInSlotZero,
            bool isDeckSelectable,
            bool unlockedByDefault,
            int buildCostGold = 100,
            int unlockCostSoftCurrency = 100)
        {
            _cardId = cardId;
            _displayName = displayName;
            _category = category;
            _isLockedInSlotZero = isLockedInSlotZero;
            _isDeckSelectable = isDeckSelectable;
            _unlockedByDefault = unlockedByDefault;
            _buildCostGold = buildCostGold;
            _unlockCostSoftCurrency = unlockCostSoftCurrency;
        }

        public void ConfigureGameplayStats(
            float territoryExpansionRadius,
            float incomePerSecondBonus,
            float buildCooldownSeconds,
            bool requiresGroundSupport,
            float maxHealth,
            Vector2 footprintSize,
            NetworkObject structurePrefab,
            bool requiresGoldDeposit = false,
            float buildDurationSeconds = -1f)
        {
            _territoryExpansionRadius = Mathf.Max(0f, territoryExpansionRadius);
            _incomePerSecondBonus = Mathf.Max(0f, incomePerSecondBonus);
            _buildCooldownSeconds = Mathf.Max(0f, buildCooldownSeconds);
            _requiresGroundSupport = requiresGroundSupport;
            _maxHealth = Mathf.Max(1f, maxHealth);
            _footprintSize = new Vector2(Mathf.Max(0.25f, footprintSize.x), Mathf.Max(0.25f, footprintSize.y));
            _structurePrefab = structurePrefab;
            _requiresGoldDeposit = requiresGoldDeposit;
            if (buildDurationSeconds >= 0f)
            {
                _buildDurationSeconds = buildDurationSeconds;
            }
        }

        public void SetBuildDurationSeconds(float seconds)
        {
            _buildDurationSeconds = Mathf.Max(0f, seconds);
        }

        public void ConfigureWeaponStats(
            bool canFireWeapon,
            float weaponDamage,
            float fireCooldownSeconds,
            float weaponRange,
            float projectileSpeed,
            float gravityScale = 1f,
            float splashRadius = 0f,
            bool canTargetAirships = false,
            bool preferHighArc = false,
            float minAimAngleDegrees = 5f,
            float maxAimAngleDegrees = 85f)
        {
            _canFireWeapon = canFireWeapon;
            _weaponDamage = Mathf.Max(0f, weaponDamage);
            _weaponFireCooldownSeconds = Mathf.Max(0.1f, fireCooldownSeconds);
            _weaponRange = Mathf.Max(0f, weaponRange);
            _projectileSpeed = Mathf.Max(0.5f, projectileSpeed);
            _projectileGravityScale = Mathf.Max(0f, gravityScale);
            _splashRadius = Mathf.Max(0f, splashRadius);
            _canTargetAirships = canTargetAirships;
            _preferHighArc = preferHighArc;
            _minAimAngleDegrees = minAimAngleDegrees;
            _maxAimAngleDegrees = maxAimAngleDegrees;
        }
    }
}
