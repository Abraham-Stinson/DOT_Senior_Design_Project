using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;

namespace Game.Gameplay
{
    public sealed class DraftLoadoutController : NetworkBehaviour
    {
        private readonly SyncVar<bool> _seat0Ready = new();
        private readonly SyncVar<bool> _seat1Ready = new();

        private MatchContext _context;
        private GameModeController _gameMode;

        public event Action<byte, bool, DraftRejectReason, LoadoutValidationError, int> SubmissionFeedbackReceived;
        public event Action DraftStateChanged;
        public event Action DraftCompleted;

        private void Awake()
        {
            _seat0Ready.OnChange += HandleSeatReadyChanged;
            _seat1Ready.OnChange += HandleSeatReadyChanged;
        }

        private void OnDestroy()
        {
            _seat0Ready.OnChange -= HandleSeatReadyChanged;
            _seat1Ready.OnChange -= HandleSeatReadyChanged;
        }

        public void InitializeServer(MatchContext context, GameModeController gameMode)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _context = context;
            _gameMode = gameMode;
            _seat0Ready.Value = false;
            _seat1Ready.Value = false;
        }

        public bool IsSeatReady(byte seatId)
        {
            switch (seatId)
            {
                case 0: return _seat0Ready.Value;
                case 1: return _seat1Ready.Value;
                default: return false;
            }
        }

        public void RequestSubmitLoadout(byte slotId, ushort[] cardIds, bool isReady)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitLoadoutServerRpc(slotId, cardIds, isReady);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitLoadoutServerRpc(byte slotId, ushort[] cardIds, bool isReady, NetworkConnection sender = null)
        {
            if (!IsServerInitialized || _context == null || _gameMode == null)
            {
                if (sender != null && sender.IsActive)
                {
                    NotifySubmissionResultTargetRpc(
                        sender,
                        slotId,
                        false,
                        DraftRejectReason.NotInDraftPhase,
                        LoadoutValidationError.None,
                        -1);
                }
                return;
            }

            bool seatExists = _context.TryGetSeat(slotId, out PlayerSeatController seat);
            bool callerOwnsSeat = seatExists && sender != null && seat.Owner == sender;
            bool isSenderLocalHost = sender != null && sender.IsLocalClient;

            DraftValidationOutcome outcome = DraftSubmissionValidator.Validate(
                _gameMode.Phase,
                _context.Mode,
                seatExists,
                callerOwnsSeat,
                cardIds,
                _context.CardCatalog,
                _context.LocalUnlockedCards,
                isSenderLocalHost);

            if (!outcome.IsValid)
            {
                if (seatExists && callerOwnsSeat)
                {
                    seat.SetDraftReadyServer(false);
                    SetSeatReadyFlagServer(slotId, false);
                }

                if (sender != null && sender.IsActive)
                {
                    NotifySubmissionResultTargetRpc(
                        sender,
                        slotId,
                        false,
                        outcome.RejectReason,
                        outcome.LoadoutError,
                        outcome.FailingSlotIndex);
                }
                return;
            }

            seat.ApplyValidatedLoadoutServer(cardIds);
            seat.SetDraftReadyServer(isReady);
            SetSeatReadyFlagServer(slotId, isReady);

            if (sender != null && sender.IsActive)
            {
                NotifySubmissionResultTargetRpc(
                    sender,
                    slotId,
                    true,
                    DraftRejectReason.None,
                    LoadoutValidationError.None,
                    -1);
            }

            if (isReady && AreAllHumanSeatsReady())
            {
                _gameMode.TransitionPhaseServer(MatchPhase.Playing);
                NotifyDraftCompletedObserversRpc();
            }
        }

        [TargetRpc]
        private void NotifySubmissionResultTargetRpc(
            NetworkConnection target,
            byte slotId,
            bool accepted,
            DraftRejectReason rejectReason,
            LoadoutValidationError loadoutError,
            int failingSlotIndex)
        {
            SubmissionFeedbackReceived?.Invoke(slotId, accepted, rejectReason, loadoutError, failingSlotIndex);
        }

        [ObserversRpc(BufferLast = true)]
        private void NotifyDraftCompletedObserversRpc()
        {
            DraftCompleted?.Invoke();
        }

        private void SetSeatReadyFlagServer(byte slotId, bool ready)
        {
            if (slotId == 0)
            {
                _seat0Ready.Value = ready;
            }
            else if (slotId == 1)
            {
                _seat1Ready.Value = ready;
            }
        }

        private bool AreAllHumanSeatsReady()
        {
            int humanSeats = 0;
            for (int i = 0; i < _context.Seats.Count; i++)
            {
                PlayerSeatController seat = _context.Seats[i];
                if (seat == null || seat.ControlType == SeatControlType.AI)
                {
                    continue;
                }

                humanSeats++;
                if (!seat.IsDraftReady || seat.ActiveLoadout.Count != DeckRules.DeckSize)
                {
                    return false;
                }
            }

            return humanSeats > 0;
        }

        private void HandleSeatReadyChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            DraftStateChanged?.Invoke();
        }
    }
}
