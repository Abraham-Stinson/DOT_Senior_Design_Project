using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;

namespace Game.Gameplay
{
    public sealed class GameModeController : NetworkBehaviour
    {
        private readonly SyncVar<MatchMode> _matchMode = new();
        private readonly SyncVar<MatchPhase> _matchPhase = new();
        private readonly SyncVar<TeamId> _winnerTeam = new();

        private IMatchRule _activeRule;
        private MatchContext _context;

        public MatchMode Mode => _matchMode.Value;
        public MatchPhase Phase => _matchPhase.Value;
        public TeamId WinnerTeam => _winnerTeam.Value;
        public MatchOutcome LastOutcome { get; private set; }

        public event Action<MatchPhase, MatchPhase> PhaseChanged;
        public event Action<MatchOutcome> MatchEnded;

        private void Awake()
        {
            _matchPhase.OnChange += HandlePhaseChanged;
        }

        private void OnDestroy()
        {
            _matchPhase.OnChange -= HandlePhaseChanged;
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

        public void InitializeMatchServer(MatchMode mode, IMatchRule rule, MatchContext context)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _matchMode.Value = mode;
            _matchPhase.Value = MatchPhase.PreMatchDraft;
            _winnerTeam.Value = TeamId.Neutral;
            LastOutcome = default;
            _context = context;
            if (_context != null)
            {
                _context.Mode = mode;
            }

            _activeRule = rule;
            _activeRule?.InitializeServer(_context);
        }

        public void TransitionPhaseServer(MatchPhase nextPhase)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _matchPhase.Value = nextPhase;
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || _matchPhase.Value != MatchPhase.Playing || _activeRule == null)
            {
                return;
            }

            float tickDelta = TimeManager != null ? (float)TimeManager.TickDelta : 0f;
            _activeRule.TickServer(tickDelta);

            if (_activeRule.TryEvaluateOutcome(out MatchOutcome outcome))
            {
                LastOutcome = outcome;
                _winnerTeam.Value = outcome.WinnerTeam;
                _matchPhase.Value = MatchPhase.Ended;
                BroadcastMatchEndedObserversRpc(outcome);
            }
        }

        [ObserversRpc(BufferLast = true)]
        private void BroadcastMatchEndedObserversRpc(MatchOutcome outcome)
        {
            LastOutcome = outcome;
            MatchEnded?.Invoke(outcome);
        }

        private void HandlePhaseChanged(MatchPhase prev, MatchPhase next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            PhaseChanged?.Invoke(prev, next);
        }
    }
}
