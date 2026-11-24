using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Server-authoritative 2D ballistic projectile stepped deterministically in TimeManager.OnTick.
    /// Replicates impact visuals via ObserversRpc and applies direct + splash damage via IDamageable.
    /// </summary>
    public sealed class BallisticProjectile : NetworkBehaviour
    {
        [SerializeField] private SpriteRenderer _bodyRenderer;
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private float _airshipHitRadius = 0.85f;

        private readonly SyncVar<TeamId> _sourceTeam = new();
        private readonly SyncVar<float> _splashRadius = new();

        private MatchContext _context;
        private Vector2 _velocity;
        private byte _sourceSeatId;
        private float _gravityScale = 1f;
        private float _damage;
        private float _maxLifetime = 8f;
        private float _elapsedLifetime;
        private bool _hasImpacted;
        private bool _initializedServer;

        public TeamId SourceTeam => _sourceTeam.Value;
        public float SplashRadius => _splashRadius.Value;
        public Vector2 ServerVelocity => _velocity;
        public bool HasImpacted => _hasImpacted;

        public static event Action<Vector2, float, TeamId> ProjectileImpactedClient;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            ProjectileImpactedClient = null;
        }

        private void Awake()
        {
            if (_bodyRenderer == null)
            {
                _bodyRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (_trailRenderer == null)
            {
                _trailRenderer = GetComponentInChildren<TrailRenderer>();
            }

            _sourceTeam.OnChange += HandleTeamChanged;
        }

        private void OnDestroy()
        {
            _sourceTeam.OnChange -= HandleTeamChanged;
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
            UpdateVisuals();
        }

        public void InitializeServer(
            TeamId sourceTeam,
            byte sourceSeatId,
            Vector2 launchVelocity,
            float gravityScale,
            float damage,
            float splashRadius,
            MatchContext context,
            float maxLifetimeSeconds = 8f)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _sourceTeam.Value = sourceTeam;
            _splashRadius.Value = Mathf.Max(0f, splashRadius);
            _sourceSeatId = sourceSeatId;
            _velocity = launchVelocity;
            _gravityScale = Mathf.Max(0f, gravityScale);
            _damage = Mathf.Max(0f, damage);
            _context = context;
            _maxLifetime = Mathf.Max(0.25f, maxLifetimeSeconds);
            _elapsedLifetime = 0f;
            _hasImpacted = false;
            _initializedServer = true;

            UpdateVisuals();
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || !_initializedServer || _hasImpacted)
            {
                return;
            }

            float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);
            _elapsedLifetime += dt;

            Vector2 prevPos = transform.position;
            Vector2 nextPos = prevPos;
            float gravity = Ballistics2D.DefaultGravity * _gravityScale;
            Ballistics2D.StepProjectile(ref nextPos, ref _velocity, gravity, dt);

            if (TryFindEarliestImpact(prevPos, nextPos, out Vector2 hitPoint, out IDamageable directTarget))
            {
                transform.position = new Vector3(hitPoint.x, hitPoint.y, transform.position.z);
                HandleImpactServer(hitPoint, directTarget);
                return;
            }

            transform.position = new Vector3(nextPos.x, nextPos.y, transform.position.z);

            if (_elapsedLifetime >= _maxLifetime || IsOutOfArenaBounds(nextPos))
            {
                _hasImpacted = true;
                Despawn();
            }
        }

        private bool TryFindEarliestImpact(
            Vector2 prevPos,
            Vector2 nextPos,
            out Vector2 hitPoint,
            out IDamageable directTarget)
        {
            float bestT = float.MaxValue;
            hitPoint = nextPos;
            directTarget = null;
            bool hitAnything = false;

            if (_context != null)
            {
                IReadOnlyList<IStructure> structures = _context.Structures;
                for (int i = 0; i < structures.Count; i++)
                {
                    IStructure structure = structures[i];
                    if (structure == null || !structure.IsAlive)
                    {
                        continue;
                    }

                    IDamageable damageable = structure;
                    if (damageable.Team == TeamId.Neutral || damageable.Team == _sourceTeam.Value)
                    {
                        continue;
                    }

                    Bounds bounds = structure.FootprintBounds;
                    if (Ballistics2D.TrySegmentIntersectsRect(prevPos, nextPos, bounds.min, bounds.max, out float t, out Vector2 pt)
                        && t < bestT)
                    {
                        bestT = t;
                        hitPoint = pt;
                        directTarget = damageable;
                        hitAnything = true;
                    }
                }

                IReadOnlyList<AirshipController> airships = _context.Airships;
                for (int i = 0; i < airships.Count; i++)
                {
                    AirshipController ship = airships[i];
                    if (ship == null || !ship.IsAlive || ship.Team == TeamId.Neutral || ship.Team == _sourceTeam.Value)
                    {
                        continue;
                    }

                    if (Ballistics2D.TrySegmentIntersectsCircle(prevPos, nextPos, ship.transform.position, _airshipHitRadius, out float t, out Vector2 pt)
                        && t < bestT)
                    {
                        bestT = t;
                        hitPoint = pt;
                        directTarget = ship;
                        hitAnything = true;
                    }
                }

                if (_context.Terrain != null)
                {
                    float surfaceY = _context.Terrain.SampleGroundSurfaceY(nextPos.x);
                    if (nextPos.y <= surfaceY || _context.Terrain.IsSolid(nextPos))
                    {
                        float denom = prevPos.y - nextPos.y;
                        float terrainT = prevPos.y > surfaceY && denom > 0.0001f
                            ? Mathf.Clamp01((prevPos.y - surfaceY) / denom)
                            : 0f;

                        if (terrainT < bestT)
                        {
                            bestT = terrainT;
                            hitPoint = Vector2.Lerp(prevPos, nextPos, terrainT);
                            directTarget = null;
                            hitAnything = true;
                        }
                    }
                }
            }

            return hitAnything;
        }

        private void HandleImpactServer(Vector2 hitPoint, IDamageable directTarget)
        {
            _hasImpacted = true;

            var damageBatch = new List<(IDamageable Target, float Amount)>();
            float splash = _splashRadius.Value;

            if (splash > 0.01f && _context != null)
            {
                IReadOnlyList<IStructure> structures = _context.Structures;
                for (int i = 0; i < structures.Count; i++)
                {
                    IStructure structure = structures[i];
                    if (structure == null || !structure.IsAlive)
                    {
                        continue;
                    }

                    IDamageable damageable = structure;
                    if (damageable.Team == TeamId.Neutral || damageable.Team == _sourceTeam.Value)
                    {
                        continue;
                    }

                    float amount;
                    if (ReferenceEquals(damageable, directTarget))
                    {
                        amount = _damage;
                    }
                    else
                    {
                        Bounds b = structure.FootprintBounds;
                        float dist = Ballistics2D.DistancePointToRect(hitPoint, b.min, b.max);
                        amount = Ballistics2D.ComputeSplashDamage(_damage, dist, splash);
                    }

                    if (amount > 0.01f)
                    {
                        damageBatch.Add((damageable, amount));
                    }
                }

                IReadOnlyList<AirshipController> airships = _context.Airships;
                for (int i = 0; i < airships.Count; i++)
                {
                    AirshipController ship = airships[i];
                    if (ship == null || !ship.IsAlive || ship.Team == TeamId.Neutral || ship.Team == _sourceTeam.Value)
                    {
                        continue;
                    }

                    float amount;
                    if (ReferenceEquals(ship, directTarget))
                    {
                        amount = _damage;
                    }
                    else
                    {
                        float dist = Mathf.Max(0f, Vector2.Distance(hitPoint, ship.transform.position) - _airshipHitRadius * 0.5f);
                        amount = Ballistics2D.ComputeSplashDamage(_damage, dist, splash);
                    }

                    if (amount > 0.01f)
                    {
                        damageBatch.Add((ship, amount));
                    }
                }
            }
            else if (directTarget != null && directTarget.IsAlive && _damage > 0.01f)
            {
                damageBatch.Add((directTarget, _damage));
            }

            if (directTarget != null && directTarget.IsAlive && _damage > 0.01f)
            {
                bool alreadyQueued = false;
                for (int i = 0; i < damageBatch.Count; i++)
                {
                    if (ReferenceEquals(damageBatch[i].Target, directTarget))
                    {
                        alreadyQueued = true;
                        break;
                    }
                }

                if (!alreadyQueued)
                {
                    damageBatch.Add((directTarget, _damage));
                }
            }

            for (int i = 0; i < damageBatch.Count; i++)
            {
                (IDamageable target, float amount) = damageBatch[i];
                if (target != null && target.IsAlive)
                {
                    target.ApplyDamageServer(new DamageInfo(amount, _sourceTeam.Value, _sourceSeatId, hitPoint));
                }
            }

            BroadcastImpactObserversRpc(hitPoint, splash, _sourceTeam.Value);
            Despawn();
        }

        private bool IsOutOfArenaBounds(Vector2 pos)
        {
            Rect bounds = _context != null ? _context.PlayableAirspace : new Rect(-18f, -2f, 36f, 16f);
            return pos.x < bounds.xMin - 14f
                || pos.x > bounds.xMax + 14f
                || pos.y < bounds.yMin - 6f
                || pos.y > bounds.yMax + 28f;
        }

        [ObserversRpc]
        private void BroadcastImpactObserversRpc(Vector2 hitPoint, float splashRadius, TeamId sourceTeam)
        {
            ProjectileImpactedClient?.Invoke(hitPoint, splashRadius, sourceTeam);
            SpawnImpactVisual(hitPoint, splashRadius, sourceTeam);
        }

        private void HandleTeamChanged(TeamId prev, TeamId next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            Color color = _sourceTeam.Value == TeamId.TeamA
                ? new Color(0.32f, 0.85f, 1f)
                : (_sourceTeam.Value == TeamId.TeamB ? new Color(1f, 0.48f, 0.22f) : Color.white);

            if (_bodyRenderer != null)
            {
                _bodyRenderer.color = color;
            }

            if (_trailRenderer != null)
            {
                _trailRenderer.startColor = color;
                _trailRenderer.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private static void SpawnImpactVisual(Vector2 hitPoint, float splashRadius, TeamId sourceTeam)
        {
            var burstGo = new GameObject("ProjectileImpactFlash");
            burstGo.transform.position = new Vector3(hitPoint.x, hitPoint.y, -0.1f);
            var flash = burstGo.AddComponent<ProjectileImpactFlash>();
            Color color = sourceTeam == TeamId.TeamA
                ? new Color(0.45f, 0.9f, 1f, 0.85f)
                : new Color(1f, 0.62f, 0.25f, 0.85f);
            flash.Play(Mathf.Max(0.45f, splashRadius), color);
        }

        private sealed class ProjectileImpactFlash : MonoBehaviour
        {
            private static Texture2D s_circleTex;
            private static Sprite s_circleSprite;

            private SpriteRenderer _sr;
            private float _duration = 0.22f;
            private float _elapsed;
            private float _targetScale = 1f;
            private Color _baseColor;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStaticFlash()
            {
                s_circleSprite = null;
                s_circleTex = null;
            }

            public void Play(float radius, Color color)
            {
                _targetScale = Mathf.Max(0.5f, radius * 2f);
                _baseColor = color;
                _sr = gameObject.AddComponent<SpriteRenderer>();
                _sr.sprite = GetCircleSprite();
                _sr.color = color;
                _sr.sortingOrder = 12;
                transform.localScale = Vector3.one * (_targetScale * 0.35f);
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _duration);
                float scale = Mathf.Lerp(_targetScale * 0.35f, _targetScale, t);
                transform.localScale = new Vector3(scale, scale, 1f);
                if (_sr != null)
                {
                    Color c = _baseColor;
                    c.a = _baseColor.a * (1f - t);
                    _sr.color = c;
                }

                if (_elapsed >= _duration)
                {
                    Destroy(gameObject);
                }
            }

            private static Sprite GetCircleSprite()
            {
                if (s_circleSprite != null)
                {
                    return s_circleSprite;
                }

                const int size = 32;
                s_circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };

                float c = (size - 1) * 0.5f;
                float r = size * 0.45f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        float alpha = Mathf.Clamp01((r - dist) / 2.5f) * 0.7f;
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }

                s_circleTex.SetPixels32(pixels);
                s_circleTex.Apply();
                s_circleSprite = Sprite.Create(s_circleTex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
                s_circleSprite.hideFlags = HideFlags.DontSave;
                return s_circleSprite;
            }
        }
    }
}
