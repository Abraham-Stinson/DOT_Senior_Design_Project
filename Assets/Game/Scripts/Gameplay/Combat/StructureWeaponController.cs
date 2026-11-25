using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Server-authoritative manual weapon controller attached to weapon structures (Cannon, Flak, Mortar, Laser).
    /// Weapons do NOT auto-fire: an allied airship must dock/enter to aim and fire manually,
    /// triggering a per-structure cooldown before the weapon can be entered and fired again.
    /// </summary>
    public sealed class StructureWeaponController : NetworkBehaviour
    {
        [SerializeField] private BallisticProjectile _projectilePrefab;
        [SerializeField] private float _muzzleHeightOffset = 0.5f;
        [SerializeField] private float _manualAimSpeedDegPerSec = 65f;

        private readonly SyncVar<bool> _hasWeapon = new();
        private readonly SyncVar<float> _aimAngleDegrees = new(35f);
        private readonly SyncVar<float> _cooldownRemaining = new(new SyncTypeSettings(0.1f));
        private readonly SyncVar<float> _cooldownDuration = new();
        private readonly SyncVar<float> _projectileSpeed = new(19f);
        private readonly SyncVar<float> _gravityScale = new(1f);

        private StructureBase _structure;
        private MatchContext _context;
        private WeaponConfig _config = WeaponConfig.None;
        private int _shotsFiredServer;

        private Transform _barrelPivot;
        private SpriteRenderer _barrelRenderer;
        private WorldHealthBarVisual _cooldownBarVisual;
        private LineRenderer _aimTrajectoryLine;

        private static Texture2D s_whiteTexture;
        private static Sprite s_whiteSprite;
        private static Material s_lineMaterial;

        public bool HasWeapon => _hasWeapon.Value;
        public float AimAngleDegrees => _aimAngleDegrees.Value;
        public bool IsOnCooldown => _cooldownRemaining.Value > 0.01f;
        public float CooldownRemaining => _cooldownRemaining.Value;
        public float CooldownDuration => _cooldownDuration.Value;
        public int ShotsFiredServer => _shotsFiredServer;
        public WeaponConfig Config => _config;

        public event Action<Vector2, Vector2> WeaponFiredClient;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            s_whiteSprite = null;
            s_whiteTexture = null;
            s_lineMaterial = null;
        }

        private void Awake()
        {
            if (_structure == null)
            {
                _structure = GetComponent<StructureBase>();
            }

            _hasWeapon.OnChange += HandleWeaponStateChanged;
            _aimAngleDegrees.OnChange += HandleAimAngleChanged;
            _cooldownRemaining.OnChange += HandleCooldownChanged;
            _cooldownDuration.OnChange += HandleCooldownChanged;
        }

        private void OnDestroy()
        {
            _hasWeapon.OnChange -= HandleWeaponStateChanged;
            _aimAngleDegrees.OnChange -= HandleAimAngleChanged;
            _cooldownRemaining.OnChange -= HandleCooldownChanged;
            _cooldownDuration.OnChange -= HandleCooldownChanged;
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

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshVisuals();
        }

        public void InitializeWeaponServer(
            StructureBase structure,
            MatchContext context,
            WeaponConfig config,
            BallisticProjectile projectilePrefabOverride = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _structure = structure != null ? structure : GetComponent<StructureBase>();
            _context = context;
            _config = config;

            if (projectilePrefabOverride != null)
            {
                _projectilePrefab = projectilePrefabOverride;
            }
            else if (_projectilePrefab == null && _context != null && _context.ProjectilePrefab != null)
            {
                _projectilePrefab = _context.ProjectilePrefab;
            }

            _cooldownDuration.Value = Mathf.Max(0.1f, config.FireCooldownSeconds);
            _cooldownRemaining.Value = 0f;
            _projectileSpeed.Value = Mathf.Max(0.5f, config.ProjectileSpeed);
            _gravityScale.Value = Mathf.Max(0f, config.GravityScale);
            _shotsFiredServer = 0;
            _hasWeapon.Value = config.CanFire;

            TeamId team = _structure != null ? _structure.Team : TeamId.TeamA;
            _aimAngleDegrees.Value = team == TeamId.TeamB ? 145f : 35f;
            RefreshVisuals();
        }

        public void ApplyManualAimStepServer(Vector2 aimMoveInput, float dt)
        {
            if (!IsServerInitialized || !_hasWeapon.Value || _structure == null || !_structure.IsAlive || IsOnCooldown)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            _aimAngleDegrees.Value = ManualAimMath.StepBarrelAngle(
                _aimAngleDegrees.Value,
                aimMoveInput,
                _manualAimSpeedDegPerSec,
                dt);
            RefreshVisuals();
        }

        public void SetManualAimDirectionServer(Vector2 aimDirection)
        {
            if (!IsServerInitialized || !_hasWeapon.Value || _structure == null || !_structure.IsAlive || IsOnCooldown)
            {
                return;
            }

            if (aimDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            _aimAngleDegrees.Value = ManualAimMath.DirectionToBarrelAngle(aimDirection);
            RefreshVisuals();
        }

        public bool HandleManualInteractServer(byte actorSeatId, in InteractPayload payload)
        {
            if (!IsServerInitialized || !_hasWeapon.Value || _structure == null || !_structure.IsAlive)
            {
                return false;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return false;
            }

            if (IsOnCooldown)
            {
                return false;
            }

            if (payload.TargetPoint.sqrMagnitude > 0.01f)
            {
                _aimAngleDegrees.Value = ManualAimMath.DirectionToBarrelAngle(payload.TargetPoint);
            }

            float angleDeg = ManualAimMath.ClampBarrelAngle(_aimAngleDegrees.Value);
            Vector2 muzzle = GetMuzzleWorldPosition();
            Vector2 launchVelocity = ManualAimMath.BarrelAngleToVelocity(angleDeg, _config.ProjectileSpeed);

            FireProjectileServer(muzzle, launchVelocity, angleDeg, actorSeatId);
            return true;
        }

        public void RefreshVisuals()
        {
            if (!_hasWeapon.Value)
            {
                if (_barrelPivot != null) _barrelPivot.gameObject.SetActive(false);
                if (_cooldownBarVisual != null) _cooldownBarVisual.gameObject.SetActive(false);
                if (_aimTrajectoryLine != null) _aimTrajectoryLine.enabled = false;
                return;
            }

            EnsureBarrelVisual();
            _barrelPivot.gameObject.SetActive(true);
            _barrelPivot.localPosition = new Vector3(0f, _muzzleHeightOffset, -0.05f);
            _barrelPivot.localRotation = Quaternion.Euler(0f, 0f, _aimAngleDegrees.Value);

            if (_barrelRenderer != null && _structure != null)
            {
                Color readyColor = _structure.Team == TeamId.TeamA
                    ? new Color(0.75f, 0.93f, 1f)
                    : new Color(1f, 0.8f, 0.72f);
                _barrelRenderer.color = IsOnCooldown ? Color.Lerp(readyColor, Color.gray, 0.65f) : readyColor;
            }

            UpdateCooldownVisual();
            UpdateAimTrajectoryVisual();
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || !_hasWeapon.Value || _structure == null || !_structure.IsAlive)
            {
                return;
            }

            if (_context != null && _context.CanPilotAirships != null && !_context.CanPilotAirships())
            {
                return;
            }

            if (_cooldownRemaining.Value <= 0f)
            {
                return;
            }

            float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);
            _cooldownRemaining.Value = Mathf.Max(0f, _cooldownRemaining.Value - dt);
        }

        private void FireProjectileServer(Vector2 muzzle, Vector2 launchVelocity, float angleDeg, byte sourceSeatId)
        {
            BallisticProjectile prefab = _projectilePrefab != null
                ? _projectilePrefab
                : (_context != null ? _context.ProjectilePrefab : null);
            if (prefab == null)
            {
                return;
            }

            float cooldown = Mathf.Max(0.1f, _config.FireCooldownSeconds);
            _aimAngleDegrees.Value = angleDeg;
            _cooldownDuration.Value = cooldown;
            _cooldownRemaining.Value = cooldown;
            _shotsFiredServer++;

            BallisticProjectile projectile = Instantiate(prefab, new Vector3(muzzle.x, muzzle.y, 0f), Quaternion.identity);
            projectile.name = $"Projectile_{_structure.Team}_{_shotsFiredServer}";
            ServerManager.Spawn(projectile.NetworkObject);
            projectile.InitializeServer(
                _structure.Team,
                sourceSeatId,
                launchVelocity,
                _config.GravityScale,
                _config.Damage,
                _config.SplashRadius,
                _context);

            NotifyWeaponFiredObserversRpc(muzzle, launchVelocity);
            RefreshVisuals();
        }

        private Vector2 GetMuzzleWorldPosition()
        {
            return (Vector2)transform.position + new Vector2(0f, _muzzleHeightOffset);
        }

        [ObserversRpc]
        private void NotifyWeaponFiredObserversRpc(Vector2 muzzle, Vector2 launchVelocity)
        {
            WeaponFiredClient?.Invoke(muzzle, launchVelocity);
        }

        private void HandleWeaponStateChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            RefreshVisuals();
        }

        private void HandleAimAngleChanged(float prev, float next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            RefreshVisuals();
        }

        private void HandleCooldownChanged(float prev, float next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            RefreshVisuals();
        }

        private bool ShouldSuppressServerDuplicate()
        {
            return IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted);
        }

        private void UpdateCooldownVisual()
        {
            bool cooling = IsOnCooldown && _cooldownDuration.Value > 0.01f;
            if (!cooling)
            {
                if (_cooldownBarVisual != null)
                {
                    _cooldownBarVisual.gameObject.SetActive(false);
                }
                return;
            }

            if (_cooldownBarVisual == null)
            {
                var barGo = new GameObject("WeaponCooldownBar");
                barGo.transform.SetParent(transform, false);
                _cooldownBarVisual = barGo.AddComponent<WorldHealthBarVisual>();
            }

            _cooldownBarVisual.gameObject.SetActive(true);
            float elapsed = Mathf.Clamp(_cooldownDuration.Value - _cooldownRemaining.Value, 0.01f, _cooldownDuration.Value);
            float yOffset = _structure != null ? -(_structure.FootprintBounds.extents.y + 0.25f) : -0.95f;
            _cooldownBarVisual.SetHealth(elapsed, _cooldownDuration.Value, yOffset, width: 1.5f, alwaysShow: true);
        }

        private void UpdateAimTrajectoryVisual()
        {
            bool showArc = _structure != null && _structure.IsAlive && _structure.IsWeaponControlled && !IsOnCooldown;
            if (!showArc)
            {
                if (_aimTrajectoryLine != null)
                {
                    _aimTrajectoryLine.enabled = false;
                }
                return;
            }

            EnsureTrajectoryLine();
            _aimTrajectoryLine.enabled = true;

            const int sampleCount = 20;
            const float stepDt = 0.065f;
            _aimTrajectoryLine.positionCount = sampleCount;

            Vector2 pos = GetMuzzleWorldPosition();
            Vector2 vel = ManualAimMath.BarrelAngleToVelocity(_aimAngleDegrees.Value, Mathf.Max(0.5f, _projectileSpeed.Value));
            float gravity = Ballistics2D.DefaultGravity * Mathf.Max(0f, _gravityScale.Value);

            for (int i = 0; i < sampleCount; i++)
            {
                _aimTrajectoryLine.SetPosition(i, new Vector3(pos.x, pos.y, -0.08f));
                Ballistics2D.StepProjectile(ref pos, ref vel, gravity, stepDt);
            }
        }

        private void EnsureBarrelVisual()
        {
            if (_barrelPivot != null)
            {
                return;
            }

            var pivotGo = new GameObject("WeaponBarrelPivot");
            pivotGo.transform.SetParent(transform, false);
            _barrelPivot = pivotGo.transform;

            var barrelGo = new GameObject("WeaponBarrelSprite");
            barrelGo.transform.SetParent(_barrelPivot, false);
            barrelGo.transform.localPosition = new Vector3(0.45f, 0f, 0f);
            barrelGo.transform.localScale = new Vector3(0.9f, 0.22f, 1f);
            _barrelRenderer = barrelGo.AddComponent<SpriteRenderer>();
            _barrelRenderer.sprite = GetOrCreateWhiteSprite();
            _barrelRenderer.sortingOrder = 6;
        }

        private void EnsureTrajectoryLine()
        {
            if (_aimTrajectoryLine != null)
            {
                return;
            }

            var arcGo = new GameObject("WeaponAimTrajectory");
            arcGo.transform.SetParent(transform, false);
            _aimTrajectoryLine = arcGo.AddComponent<LineRenderer>();
            _aimTrajectoryLine.useWorldSpace = true;
            _aimTrajectoryLine.startWidth = 0.06f;
            _aimTrajectoryLine.endWidth = 0.03f;
            _aimTrajectoryLine.sortingOrder = 7;
            _aimTrajectoryLine.sharedMaterial = GetOrCreateLineMaterial();
            _aimTrajectoryLine.startColor = new Color(1f, 0.94f, 0.45f, 0.9f);
            _aimTrajectoryLine.endColor = new Color(1f, 0.72f, 0.25f, 0.15f);
        }

        private static Sprite GetOrCreateWhiteSprite()
        {
            if (s_whiteSprite != null)
            {
                return s_whiteSprite;
            }

            s_whiteTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            s_whiteTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            s_whiteTexture.Apply();
            s_whiteSprite = Sprite.Create(s_whiteTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            s_whiteSprite.hideFlags = HideFlags.DontSave;
            return s_whiteSprite;
        }

        private static Material GetOrCreateLineMaterial()
        {
            if (s_lineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                s_lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }

            return s_lineMaterial;
        }
    }
}
