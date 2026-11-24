using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public sealed class DockableAnchorNode : NetworkBehaviour, IDockable
    {
        [SerializeField] private Vector2 _anchorLocalOffset = new(0f, 1.4f);
        [SerializeField] private SpriteRenderer _visualRenderer;

        private readonly SyncVar<TeamId> _teamId = new();
        private readonly SyncVar<bool> _isOccupied = new();

        private MatchContext _context;
        private byte _dockedSeatId = byte.MaxValue;

        public TeamId Team => _teamId.Value;
        public bool IsOccupied => _isOccupied.Value;
        public Vector2 DockAnchorWorld => (Vector2)transform.position + _anchorLocalOffset;

        private void Awake()
        {
            if (_visualRenderer == null)
            {
                _visualRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            _teamId.OnChange += HandleStateChanged;
            _isOccupied.OnChange += HandleOccupiedChanged;
        }

        private void OnDestroy()
        {
            _teamId.OnChange -= HandleStateChanged;
            _isOccupied.OnChange -= HandleOccupiedChanged;
            _context?.UnregisterDockable(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            UpdateVisuals();
        }

        public override void OnStopServer()
        {
            _context?.UnregisterDockable(this);
            base.OnStopServer();
        }

        public void InitializeServer(TeamId teamId, MatchContext context)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _teamId.Value = teamId;
            _isOccupied.Value = false;
            _dockedSeatId = byte.MaxValue;
            _context = context;
            _context?.RegisterDockable(this, this, NetworkObject);
            UpdateVisuals();
        }

        public bool CanDock(byte seatId, TeamId team)
        {
            if (!IsServerInitialized || team == TeamId.Neutral || team != _teamId.Value)
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

        private void HandleStateChanged(TeamId prev, TeamId next, bool asServer)
        {
            if (asServer && IsHostInitialized)
            {
                return;
            }

            UpdateVisuals();
        }

        private void HandleOccupiedChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && IsHostInitialized)
            {
                return;
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_visualRenderer == null)
            {
                return;
            }

            Color baseColor = _teamId.Value == TeamId.TeamA
                ? new Color(0.25f, 0.55f, 0.95f)
                : (_teamId.Value == TeamId.TeamB ? new Color(0.95f, 0.35f, 0.35f) : Color.gray);

            _visualRenderer.color = _isOccupied.Value
                ? Color.Lerp(baseColor, Color.yellow, 0.45f)
                : baseColor;
        }
    }
}
