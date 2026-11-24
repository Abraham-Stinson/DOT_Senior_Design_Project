using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;

namespace Game.Gameplay
{
    public sealed class PlayerSeatController : NetworkBehaviour
    {
        private readonly SyncVar<byte> _seatId = new();
        private readonly SyncVar<TeamId> _teamId = new();
        private readonly SyncVar<SeatControlType> _controlType = new();
        private readonly SyncVar<int> _gold = new(new SyncTypeSettings(0.15f));
        private readonly SyncVar<float> _incomePerSecond = new();
        private readonly SyncVar<bool> _isDraftReady = new();
        private readonly SyncList<ushort> _activeLoadout = new();

        public byte SeatId => _seatId.Value;
        public TeamId Team => _teamId.Value;
        public SeatControlType ControlType => _controlType.Value;
        public int Gold => _gold.Value;
        public float IncomePerSecond => _incomePerSecond.Value;
        public bool IsDraftReady => _isDraftReady.Value;
        public IReadOnlyList<ushort> ActiveLoadout => _activeLoadout;

        public event Action<int> GoldChanged;
        public event Action<float> IncomeChanged;
        public event Action<bool> DraftReadyChanged;
        public event Action LoadoutChanged;

        private void Awake()
        {
            _gold.OnChange += HandleGoldChanged;
            _incomePerSecond.OnChange += HandleIncomeChanged;
            _isDraftReady.OnChange += HandleDraftReadyChanged;
            _activeLoadout.OnChange += HandleLoadoutChanged;
        }

        private void OnDestroy()
        {
            _gold.OnChange -= HandleGoldChanged;
            _incomePerSecond.OnChange -= HandleIncomeChanged;
            _isDraftReady.OnChange -= HandleDraftReadyChanged;
            _activeLoadout.OnChange -= HandleLoadoutChanged;
        }

        public bool IsCardInActiveLoadout(ushort cardId)
        {
            if (cardId == 0)
            {
                return false;
            }

            for (int i = 0; i < _activeLoadout.Count; i++)
            {
                if (_activeLoadout[i] == cardId)
                {
                    return true;
                }
            }

            return false;
        }

        public void InitializeSeatServer(
            byte seatId,
            TeamId teamId,
            SeatControlType controlType,
            int startingGold)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _seatId.Value = seatId;
            _teamId.Value = teamId;
            _controlType.Value = controlType;
            _gold.Value = Math.Max(0, startingGold);
            _incomePerSecond.Value = 0f;
            _isDraftReady.Value = false;
            _activeLoadout.Clear();
        }

        public void ApplyValidatedLoadoutServer(IReadOnlyList<ushort> validatedFiveCards)
        {
            if (!IsServerInitialized || validatedFiveCards == null || validatedFiveCards.Count != DeckRules.DeckSize)
            {
                return;
            }

            _activeLoadout.Clear();
            for (int i = 0; i < validatedFiveCards.Count; i++)
            {
                _activeLoadout.Add(validatedFiveCards[i]);
            }
        }

        public void SetDraftReadyServer(bool ready)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _isDraftReady.Value = ready;
        }

        public void AddGoldServer(int amount)
        {
            if (!IsServerInitialized || amount <= 0)
            {
                return;
            }

            _gold.Value += amount;
        }

        public bool TrySpendGoldServer(int amount)
        {
            if (!IsServerInitialized || amount < 0 || _gold.Value < amount)
            {
                return false;
            }

            _gold.Value -= amount;
            return true;
        }

        public void SetIncomePerSecondServer(float incomePerSecond)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _incomePerSecond.Value = Math.Max(0f, incomePerSecond);
        }

        private bool ShouldSuppressServerDuplicate()
        {
            return IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted);
        }

        private void HandleGoldChanged(int prev, int next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            GoldChanged?.Invoke(next);
        }

        private void HandleIncomeChanged(float prev, float next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            IncomeChanged?.Invoke(next);
        }

        private void HandleDraftReadyChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            DraftReadyChanged?.Invoke(next);
        }

        private void HandleLoadoutChanged(SyncListOperation op, int index, ushort oldItem, ushort newItem, bool asServer)
        {
            if (asServer && ShouldSuppressServerDuplicate())
            {
                return;
            }

            LoadoutChanged?.Invoke();
        }
    }
}
