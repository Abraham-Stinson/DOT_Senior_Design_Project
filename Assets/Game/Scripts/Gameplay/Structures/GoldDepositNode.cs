using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public static class ActiveGoldDepositsRegistry
    {
        private static readonly List<GoldDepositNode> s_deposits = new();

        public static IReadOnlyList<GoldDepositNode> ActiveDeposits => s_deposits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            s_deposits.Clear();
        }

        public static void Register(GoldDepositNode deposit)
        {
            if (deposit != null && !s_deposits.Contains(deposit))
            {
                s_deposits.Add(deposit);
            }
        }

        public static void Unregister(GoldDepositNode deposit)
        {
            if (deposit != null)
            {
                s_deposits.Remove(deposit);
            }
        }
    }

    /// <summary>
    /// Replicated specialized resource deposit anchor required for Gold Mine placement.
    /// </summary>
    public sealed class GoldDepositNode : NetworkBehaviour, IGoldDeposit
    {
        [SerializeField] private float _snapRadius = 2.6f;
        [SerializeField] private SpriteRenderer _visualRenderer;
        [SerializeField] private SpriteRenderer _ringRenderer;

        private readonly SyncVar<int> _depositId = new();
        private readonly SyncVar<bool> _isOccupied = new();

        private MatchContext _context;
        private int _occupiedByStructureId = -1;
        private GoldDepositSpotState _carrySpotState = GoldDepositSpotState.Neutral;

        public int DepositId => _depositId.Value;
        public Vector2 AnchorWorld => transform.position;
        public float SnapRadius => _snapRadius;
        public bool IsOccupied => _isOccupied.Value;
        public GoldDepositSpotState CurrentSpotState => _carrySpotState;

        private void Awake()
        {
            if (_visualRenderer == null)
            {
                _visualRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            _isOccupied.OnChange += HandleOccupiedChanged;
        }

        private void OnEnable()
        {
            ActiveGoldDepositsRegistry.Register(this);
            UpdateVisuals();
        }

        private void OnDisable()
        {
            ActiveGoldDepositsRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            _isOccupied.OnChange -= HandleOccupiedChanged;
            _context?.UnregisterGoldDeposit(this);
            ActiveGoldDepositsRegistry.Unregister(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ActiveGoldDepositsRegistry.Register(this);
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.RegisterReplicatedGoldDeposit(this);
            UpdateVisuals();
        }

        public override void OnStopClient()
        {
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.UnregisterReplicatedGoldDeposit(this);
            ActiveGoldDepositsRegistry.Unregister(this);
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            _context?.UnregisterGoldDeposit(this);
            base.OnStopServer();
        }

        public void InitializeServer(int depositId, MatchContext context)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _depositId.Value = depositId;
            _isOccupied.Value = false;
            _occupiedByStructureId = -1;
            _context = context;
            _context?.RegisterGoldDeposit(this);
            UpdateVisuals();
        }

        public void OccupyServer(int structureObjectId)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _occupiedByStructureId = structureObjectId;
            _isOccupied.Value = true;
            UpdateVisuals();
        }

        public void ReleaseServer(int structureObjectId)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (_occupiedByStructureId == structureObjectId || structureObjectId == -1)
            {
                _occupiedByStructureId = -1;
                _isOccupied.Value = false;
                UpdateVisuals();
            }
        }

        public void SetHighlightForCarry(bool highlight)
        {
            SetCarrySpotState(highlight ? GoldDepositSpotState.Valid : GoldDepositSpotState.Neutral);
        }

        public void SetCarrySpotState(GoldDepositSpotState state)
        {
            GoldDepositSpotState effective = _isOccupied.Value && state != GoldDepositSpotState.Neutral
                ? GoldDepositSpotState.Occupied
                : state;
            if (_carrySpotState == effective)
            {
                return;
            }

            _carrySpotState = effective;
            UpdateVisuals();
        }

        private void HandleOccupiedChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            if (next)
            {
                _carrySpotState = GoldDepositSpotState.Occupied;
            }
            else if (_carrySpotState == GoldDepositSpotState.Occupied)
            {
                _carrySpotState = GoldDepositSpotState.Neutral;
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_visualRenderer != null)
            {
                if (_isOccupied.Value)
                {
                    _visualRenderer.color = new Color(0.55f, 0.45f, 0.22f, 0.7f);
                }
                else
                {
                    _visualRenderer.color = _carrySpotState switch
                    {
                        GoldDepositSpotState.Valid => new Color(1f, 0.92f, 0.2f, 1f),
                        GoldDepositSpotState.OutsideTerritory => new Color(0.75f, 0.45f, 0.2f, 0.8f),
                        GoldDepositSpotState.OutOfRange => new Color(0.75f, 0.45f, 0.2f, 0.8f),
                        _ => new Color(0.95f, 0.76f, 0.16f, 0.95f)
                    };
                }
            }

            if (_ringRenderer != null)
            {
                _ringRenderer.enabled = !_isOccupied.Value;
                if (_ringRenderer.enabled)
                {
                    _ringRenderer.color = _carrySpotState switch
                    {
                        GoldDepositSpotState.Valid => new Color(0.25f, 0.96f, 0.42f, 0.72f),
                        GoldDepositSpotState.OutsideTerritory => new Color(0.95f, 0.25f, 0.25f, 0.6f),
                        GoldDepositSpotState.OutOfRange => new Color(0.95f, 0.25f, 0.25f, 0.6f),
                        _ => new Color(1f, 0.85f, 0.2f, 0.22f)
                    };
                }
            }
        }
    }
}
