using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Server-authoritative gold income generator and spend/refund ledger synced via PlayerSeatController SyncVars.
    /// </summary>
    public sealed class EconomyController : NetworkBehaviour, IEconomyService
    {
        [SerializeField] private float _baseIncomePerSecond = 10f;

        private readonly EconomyLedger _ledger = new();
        private MatchContext _context;
        private GameModeController _gameMode;

        public float BaseIncomePerSecond => _baseIncomePerSecond;

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

            _ledger.Clear();
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.RegisterReplicatedEconomyController(this);
        }

        public override void OnStopClient()
        {
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.UnregisterReplicatedEconomyController(this);
            base.OnStopClient();
        }

        public void InitializeServer(MatchContext context, GameModeController gameMode, float? baseIncomeOverride = null)
        {
            if (!IsServerInitialized || context == null)
            {
                return;
            }

            _context = context;
            _gameMode = gameMode;
            if (baseIncomeOverride.HasValue)
            {
                _baseIncomePerSecond = Mathf.Max(0f, baseIncomeOverride.Value);
            }

            _ledger.Clear();
            for (int i = 0; i < _context.Seats.Count; i++)
            {
                PlayerSeatController seat = _context.Seats[i];
                if (seat == null)
                {
                    continue;
                }

                _ledger.RegisterSeat(seat.SeatId, _baseIncomePerSecond);
                seat.SetIncomePerSecondServer(_ledger.GetTotalIncomePerSecond(seat.SeatId));
            }

            _context.Economy = this;
        }

        public int GetGold(byte seatId)
        {
            if (_context != null && _context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                return seat.Gold;
            }

            return 0;
        }

        public float GetIncomePerSecond(byte seatId)
        {
            if (_context != null && _context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                return seat.IncomePerSecond;
            }

            return 0f;
        }

        public bool TrySpendGoldServer(byte seatId, int amount)
        {
            if (!IsServerInitialized || _context == null || amount < 0)
            {
                return false;
            }

            if (!_context.TryGetSeat(seatId, out PlayerSeatController seat) || seat == null)
            {
                return false;
            }

            return seat.TrySpendGoldServer(amount);
        }

        public void AddGoldServer(byte seatId, int amount)
        {
            if (!IsServerInitialized || _context == null || amount <= 0)
            {
                return;
            }

            if (_context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                seat.AddGoldServer(amount);
            }
        }

        public void RefundGoldServer(byte seatId, int amount)
        {
            AddGoldServer(seatId, amount);
        }

        public void RegisterIncomeSourceServer(byte seatId, int sourceId, float goldPerSecond)
        {
            if (!IsServerInitialized || _context == null)
            {
                return;
            }

            _ledger.SetIncomeSource(seatId, sourceId, goldPerSecond);
            SyncSeatIncomeRateServer(seatId);
        }

        public void UnregisterIncomeSourceServer(byte seatId, int sourceId)
        {
            if (!IsServerInitialized || _context == null)
            {
                return;
            }

            _ledger.RemoveIncomeSource(seatId, sourceId);
            SyncSeatIncomeRateServer(seatId);
        }

        private void HandleServerTick()
        {
            if (!IsServerInitialized || _context == null)
            {
                return;
            }

            if (_gameMode != null && _gameMode.Phase != MatchPhase.Playing)
            {
                return;
            }

            float dt = TimeManager != null ? (float)TimeManager.TickDelta : (1f / 30f);
            _ledger.StepTick(dt, GrantWholeGoldServer);
        }

        private void GrantWholeGoldServer(byte seatId, int wholeGold)
        {
            if (_context != null && _context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                seat.AddGoldServer(wholeGold);
            }
        }

        private void SyncSeatIncomeRateServer(byte seatId)
        {
            if (_context != null && _context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                seat.SetIncomePerSecondServer(_ledger.GetTotalIncomePerSecond(seatId));
            }
        }
    }
}
