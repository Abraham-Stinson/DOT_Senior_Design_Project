using Game.Core;

namespace Game.Gameplay
{
    /// <summary>
    /// Standard win/loss match rule: destroying a team's Headquarters awards victory to the opposing team
    /// and transitions GameModeController into MatchPhase.Ended.
    /// </summary>
    public sealed class HqDestructionMatchRule : IMatchRule
    {
        private MatchContext _context;

        public void InitializeServer(IMatchRuleContext context)
        {
            _context = context as MatchContext;
        }

        public void TickServer(float deltaTime)
        {
        }

        public bool TryEvaluateOutcome(out MatchOutcome outcome)
        {
            if (_context == null)
            {
                outcome = default;
                return false;
            }

            bool hasA = _context.HasRegisteredHq(TeamId.TeamA);
            bool aliveA = _context.IsHqAlive(TeamId.TeamA);
            bool hasB = _context.HasRegisteredHq(TeamId.TeamB);
            bool aliveB = _context.IsHqAlive(TeamId.TeamB);

            return HqMatchOutcomeEvaluator.TryEvaluate(hasA, aliveA, hasB, aliveB, out outcome);
        }
    }
}
