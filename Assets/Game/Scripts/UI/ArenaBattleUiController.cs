using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UiCommand = PirateGame.UI.Command;
using UiMenuCommand = PirateGame.UI.MenuCommand;
using UiScreenView = PirateGame.UI.ScreenView;

namespace Game.UI
{
    public sealed class ArenaBattleUiController : MonoBehaviour
    {
        [SerializeField] private ArenaSessionBootstrap _bootstrap = null;
        [SerializeField] private ArenaBattleHudView _hudView = null;
        [SerializeField] private UiScreenView _pauseView = null;
        [SerializeField] private UiScreenView _victoryView = null;
        [SerializeField] private UiScreenView _defeatView = null;

        private readonly List<ushort> _loadoutBuffer = new();
        private readonly List<ArenaHudCardEntry> _leftCards = new();
        private readonly List<ArenaHudCardEntry> _rightCards = new();
        private readonly ArenaHudSnapshot _snapshot = new();
        private bool _isPaused;
        private bool _bindingsInitialized;

        public ArenaBattleHudView HudView => _hudView;
        public UiScreenView PauseView => _pauseView;
        public UiScreenView VictoryView => _victoryView;
        public UiScreenView DefeatView => _defeatView;
        public bool IsPaused => _isPaused;

        public void BindForTest(
            ArenaSessionBootstrap bootstrap,
            ArenaBattleHudView hudView,
            UiScreenView pauseView = null,
            UiScreenView victoryView = null,
            UiScreenView defeatView = null)
        {
            UnbindHud();
            _bootstrap = bootstrap;
            _hudView = hudView;
            _pauseView = pauseView;
            _victoryView = victoryView;
            _defeatView = defeatView;
            _bindingsInitialized = false;
            EnsureBindings();
        }

        private void Awake()
        {
            EnsureBindings();
            SetOverlayVisibility(pauseVisible: false, victoryVisible: false, defeatVisible: false);
        }

        private void OnDestroy()
        {
            UnbindHud();
            _bootstrap?.SetLocalInputPaused(false);
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = true;
            }
        }

        private void Update()
        {
            EnsureBindings();
            if (_bootstrap == null)
            {
                return;
            }

            HandleEscapeInput();
            RefreshUi();
        }

        public void SetPaused(bool paused)
        {
            if (_bootstrap != null && (_bootstrap.IsSessionDisconnected || _bootstrap.CurrentPhase == MatchPhase.Ended))
            {
                paused = false;
            }

            if (_isPaused == paused)
            {
                return;
            }

            _isPaused = paused;
            _bootstrap?.SetLocalInputPaused(_isPaused);
        }

        public bool TryTogglePauseFromUserInput()
        {
            if (_bootstrap == null || _bootstrap.IsSessionDisconnected || _bootstrap.CurrentPhase == MatchPhase.Ended)
            {
                return false;
            }

            if (_isPaused)
            {
                SetPaused(false);
                return true;
            }

            if (ShouldBlockPauseForActiveGameplayInput())
            {
                return false;
            }

            SetPaused(true);
            return true;
        }

        private bool ShouldBlockPauseForActiveGameplayInput()
        {
            if (_bootstrap == null)
            {
                return false;
            }

            int seatCount = _bootstrap.SeatCount;
            for (int i = 0; i < seatCount; i++)
            {
                if (!_bootstrap.TryGetSeatEconomyInfo(i, out byte seatId, out _, out _, out _, out _)
                    || !_bootstrap.IsSeatLocallyOwned(seatId))
                {
                    continue;
                }

                if (_bootstrap.TryGetAirshipCarryInfo(seatId, out _, out _, out _, out _))
                {
                    return true;
                }

                if (_bootstrap.TryGetDockedInteractionInfo(
                        seatId,
                        out _,
                        out DockedInteractionSubMode subMode,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _)
                    && subMode == DockedInteractionSubMode.WeaponManualControl)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleEscapeInput()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                TryTogglePauseFromUserInput();
            }
        }

        private void RefreshUi()
        {
            PopulateSnapshot();

            ArenaBattleHudState state = ArenaBattleHudStateBuilder.Build(_snapshot);
            if (_hudView != null)
            {
                _hudView.Present(state);
            }

            if (state.IsEnded)
            {
                SetPaused(false);
                SetOverlayVisibility(
                    pauseVisible: false,
                    victoryVisible: state.ShowVictoryScreen,
                    defeatVisible: !state.ShowVictoryScreen);
                UpdateEndScreenView(state.ShowVictoryScreen ? _victoryView : _defeatView, state);
                SyncEventSystemNavigation(modalOpen: true);
                return;
            }

            SetOverlayVisibility(pauseVisible: _isPaused, victoryVisible: false, defeatVisible: false);
            if (_isPaused && _pauseView != null)
            {
                _pauseView.Label("pauseNote", state.PauseNote);
                _pauseView.Label("leaveCaption", "RETURN TO MENU");
            }

            SyncEventSystemNavigation(modalOpen: _isPaused);
        }

        private static void SyncEventSystemNavigation(bool modalOpen)
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            if (es.sendNavigationEvents != modalOpen)
            {
                es.sendNavigationEvents = modalOpen;
            }

            if (!modalOpen && es.currentSelectedGameObject != null)
            {
                es.SetSelectedGameObject(null);
            }
        }

        private void PopulateSnapshot()
        {
            _leftCards.Clear();
            _rightCards.Clear();
            ResetSeatSnapshot(_snapshot.LeftSeat, 0, TeamId.TeamA);
            ResetSeatSnapshot(_snapshot.RightSeat, 1, TeamId.TeamB);

            CardCatalogSO catalog = _bootstrap.CardCatalog;
            int seatCount = _bootstrap.SeatCount;
            for (int i = 0; i < seatCount; i++)
            {
                if (!_bootstrap.TryGetSeatEconomyInfo(
                        i,
                        out byte seatId,
                        out TeamId team,
                        out _,
                        out int gold,
                        out float incomePerSec))
                {
                    continue;
                }

                ArenaSeatHudSnapshot target = seatId == 1 ? _snapshot.RightSeat : _snapshot.LeftSeat;
                List<ArenaHudCardEntry> cardBuffer = seatId == 1 ? _rightCards : _leftCards;

                target.SeatId = seatId;
                target.Team = team;
                target.Exists = true;
                target.IsLocallyOwned = _bootstrap.IsSeatLocallyOwned(seatId);
                target.Gold = gold;
                target.IncomePerSecond = incomePerSec;
                target.DeckCards = cardBuffer;

                if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                {
                    for (int c = 0; c < _loadoutBuffer.Count; c++)
                    {
                        ushort cardId = _loadoutBuffer[c];
                        cardBuffer.Add(new ArenaHudCardEntry(
                            cardId,
                            ResolveCardName(cardId, catalog),
                            ResolveCardCost(cardId, catalog)));
                    }
                }

                if (_bootstrap.TryGetDockedInteractionInfo(
                        seatId,
                        out DockedTargetKind targetKind,
                        out DockedInteractionSubMode subMode,
                        out int deckSlot,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    target.IsDockedAtBase = targetKind == DockedTargetKind.Base && subMode == DockedInteractionSubMode.BaseDeckSelect;
                    target.SelectedDeckSlot = deckSlot;
                }

                if (_bootstrap.TryGetAirshipCarryInfo(
                        seatId,
                        out _,
                        out ushort carriedCardId,
                        out _,
                        out int reservedGold))
                {
                    target.IsCarrying = true;
                    target.CarriedCardName = ResolveCardName(carriedCardId, catalog);
                    target.CarriedReservedGold = reservedGold;
                }

                target.StatusNotice = ResolveStatusNotice(seatId, catalog);
            }

            _bootstrap.TryGetHqHealth(TeamId.TeamA, out float blueCur, out float blueMax);
            _bootstrap.TryGetHqHealth(TeamId.TeamB, out float redCur, out float redMax);

            _snapshot.Phase = _bootstrap.CurrentPhase;
            _snapshot.WinnerTeam = _bootstrap.WinnerTeam;
            _snapshot.OutcomeReason = _bootstrap.IsSessionDisconnected ? _bootstrap.LastError : _bootstrap.MatchOutcomeReason;
            _snapshot.IsDisconnected = _bootstrap.IsSessionDisconnected;
            _snapshot.IsUsingPersistentOnlineSession = _bootstrap.IsUsingPersistentOnlineSession;
            _snapshot.HasPresetLoadout = MatchLaunchConfig.HasAnyPresetLoadout;
            _snapshot.BlueHqCurrent = blueCur;
            _snapshot.BlueHqMax = blueMax;
            _snapshot.RedHqCurrent = redCur;
            _snapshot.RedHqMax = redMax;
        }

        private static void ResetSeatSnapshot(ArenaSeatHudSnapshot target, byte defaultSeatId, TeamId defaultTeam)
        {
            target.SeatId = defaultSeatId;
            target.Team = defaultTeam;
            target.Exists = false;
            target.IsLocallyOwned = false;
            target.Gold = 0;
            target.IncomePerSecond = 0f;
            target.DeckCards = null;
            target.IsDockedAtBase = false;
            target.SelectedDeckSlot = 0;
            target.IsCarrying = false;
            target.CarriedCardName = string.Empty;
            target.CarriedReservedGold = 0;
            target.StatusNotice = string.Empty;
        }

        private string ResolveStatusNotice(byte seatId, CardCatalogSO catalog)
        {
            if (_bootstrap.TryGetLastBuildFeedback(seatId, out BuildFeedback feedback) && !feedback.Accepted)
            {
                string cardName = ResolveCardName(feedback.CardId, catalog);
                return $"{cardName}: {FormatBuildRejectReason(feedback.RejectReason)}";
            }

            if (_bootstrap.TryGetLastActionNotice(seatId, out string actionNotice) && !string.IsNullOrEmpty(actionNotice))
            {
                return actionNotice;
            }

            return string.Empty;
        }

        private void UpdateEndScreenView(UiScreenView view, ArenaBattleHudState state)
        {
            if (view == null)
            {
                return;
            }

            view.Label("resultLevel", state.ResultHeader);
            view.Show("stars", false);
            view.Show("reward", false);
            view.Label("destination", "RETURN TO MENU");
            view.Show("replay", state.ShowRematchButton);
        }

        private void SetOverlayVisibility(bool pauseVisible, bool victoryVisible, bool defeatVisible)
        {
            if (_pauseView != null && _pauseView.gameObject.activeSelf != pauseVisible)
            {
                _pauseView.gameObject.SetActive(pauseVisible);
            }

            if (_victoryView != null && _victoryView.gameObject.activeSelf != victoryVisible)
            {
                _victoryView.gameObject.SetActive(victoryVisible);
            }

            if (_defeatView != null && _defeatView.gameObject.activeSelf != defeatVisible)
            {
                _defeatView.gameObject.SetActive(defeatVisible);
            }
        }

        private void EnsureBindings()
        {
            if (_bindingsInitialized)
            {
                return;
            }

            _bindingsInitialized = true;

            if (_hudView != null)
            {
                _hudView.PauseClicked += HandlePauseClicked;
                _hudView.SlotClicked += HandleSlotClicked;
                _hudView.BuyClicked += HandleBuyClicked;
                _hudView.LeaveShopClicked += HandleLeaveShopClicked;
                _hudView.DeployClicked += HandleDeployClicked;
                _hudView.CancelCarryClicked += HandleCancelCarryClicked;
            }

            BindOverlayCommands(_pauseView, isPauseMenu: true);
            BindOverlayCommands(_victoryView, isPauseMenu: false);
            BindOverlayCommands(_defeatView, isPauseMenu: false);
        }

        private void UnbindHud()
        {
            if (_hudView != null)
            {
                _hudView.PauseClicked -= HandlePauseClicked;
                _hudView.SlotClicked -= HandleSlotClicked;
                _hudView.BuyClicked -= HandleBuyClicked;
                _hudView.LeaveShopClicked -= HandleLeaveShopClicked;
                _hudView.DeployClicked -= HandleDeployClicked;
                _hudView.CancelCarryClicked -= HandleCancelCarryClicked;
            }
        }

        private void BindOverlayCommands(UiScreenView view, bool isPauseMenu)
        {
            if (view == null)
            {
                return;
            }

            UiMenuCommand[] commands = view.GetComponentsInChildren<UiMenuCommand>(true);
            for (int i = 0; i < commands.Length; i++)
            {
                UiMenuCommand cmd = commands[i];
                if (cmd == null || !cmd.TryGetComponent(out Button button))
                {
                    continue;
                }

                switch (cmd.command)
                {
                    case UiCommand.Resume:
                        button.onClick.AddListener(HandleResumeClicked);
                        break;
                    case UiCommand.LeaveMatch:
                    case UiCommand.ContinueResult:
                    case UiCommand.MainMenu:
                        button.onClick.AddListener(HandleReturnToMainMenuClicked);
                        break;
                    case UiCommand.Replay:
                        button.onClick.AddListener(HandleRematchClicked);
                        break;
                    case UiCommand.Settings:
                        if (isPauseMenu)
                        {
                            button.gameObject.SetActive(false);
                        }
                        break;
                }
            }
        }

        private void HandlePauseClicked()
        {
            SetPaused(!_isPaused);
        }

        private void HandleSlotClicked(byte seatId, int slot)
        {
            _bootstrap?.RequestSelectDockedDeckSlot(seatId, slot);
        }

        private void HandleBuyClicked(byte seatId)
        {
            _bootstrap?.RequestConfirmDockedSelection(seatId);
        }

        private void HandleLeaveShopClicked(byte seatId)
        {
            _bootstrap?.RequestToggleDock(seatId);
        }

        private void HandleDeployClicked(byte seatId)
        {
            _bootstrap?.RequestDeployCarried(seatId);
        }

        private void HandleCancelCarryClicked(byte seatId)
        {
            _bootstrap?.RequestCancelCarry(seatId);
        }

        private void HandleResumeClicked()
        {
            SetPaused(false);
        }

        private void HandleReturnToMainMenuClicked()
        {
            SetPaused(false);
            _bootstrap?.ReturnToMainMenu();
        }

        private void HandleRematchClicked()
        {
            SetPaused(false);
            _bootstrap?.ReloadArenaForRematch();
        }

        private static string ResolveCardName(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.DisplayName
                : $"#{cardId}";
        }

        private static int ResolveCardCost(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.BuildCostGold
                : 0;
        }

        private static string FormatBuildRejectReason(BuildRejectReason reason)
        {
            return reason switch
            {
                BuildRejectReason.InsufficientGold => "Insufficient gold",
                BuildRejectReason.OutsideTerritory => "Outside territory",
                BuildRejectReason.OutOfBuildRange => "Out of range",
                BuildRejectReason.TooFarFromGround => "Move closer to ground",
                BuildRejectReason.InvalidTerrain => "Invalid terrain",
                BuildRejectReason.OverlappingStructure => "Space occupied",
                BuildRejectReason.RequiresGoldDeposit => "Requires gold deposit",
                BuildRejectReason.DepositOccupied => "Deposit occupied",
                BuildRejectReason.MustDockAtBase => "Dock at base",
                BuildRejectReason.OnCooldown => "On cooldown",
                _ => "Cannot build here"
            };
        }
    }
}
