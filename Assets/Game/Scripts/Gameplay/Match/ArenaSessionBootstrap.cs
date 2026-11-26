using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using Game.Core;
using Game.Data;
using Game.Input;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    public static class ActiveArenaBootstrapRegistry
    {
        private static readonly List<ArenaSessionBootstrap> s_instances = new();

        public static IReadOnlyList<ArenaSessionBootstrap> Instances => s_instances;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instances.Clear();
        }

        public static void Register(ArenaSessionBootstrap bootstrap)
        {
            if (bootstrap != null && !s_instances.Contains(bootstrap))
            {
                s_instances.Add(bootstrap);
            }
        }

        public static void Unregister(ArenaSessionBootstrap bootstrap)
        {
            if (bootstrap != null)
            {
                s_instances.Remove(bootstrap);
            }
        }

        public static ArenaSessionBootstrap FindFor(NetworkManager manager)
        {
            for (int i = s_instances.Count - 1; i >= 0; i--)
            {
                ArenaSessionBootstrap candidate = s_instances[i];
                if (candidate == null)
                {
                    s_instances.RemoveAt(i);
                    continue;
                }

                if (manager != null && ReferenceEquals(candidate.BoundNetworkManager, manager))
                {
                    return candidate;
                }
            }

            if (manager == null && s_instances.Count == 1)
            {
                return s_instances[0];
            }

            return null;
        }
    }

    public sealed class ArenaSessionBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject _localNetworkRig;
        [SerializeField] private MonoBehaviour _networkSessionComponent;
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PlayerInputRouter _inputRouter;
        [SerializeField] private PlayerSeatController _seatPrefab;
        [SerializeField] private GameModeController _gameModePrefab;
        [SerializeField] private DraftLoadoutController _draftControllerPrefab;
        [SerializeField] private EconomyController _economyControllerPrefab;
        [SerializeField] private TerritoryController _territoryControllerPrefab;
        [SerializeField] private StructureBuildController _buildControllerPrefab;
        [SerializeField] private StructureBase _greyboxStructurePrefab;
        [SerializeField] private GoldDepositNode _goldDepositPrefab;
        [SerializeField] private AirshipController _airshipPrefab;
        [SerializeField] private DockableAnchorNode _dockablePadPrefab;
        [SerializeField] private BallisticProjectile _projectilePrefab;
        [SerializeField] private CardCatalogSO _cardCatalog;
        [SerializeField] private ArenaLayoutConfig _arenaLayout;
        [SerializeField] private int _startingGold = 250;
        [SerializeField] private float _baseIncomePerSecond = 10f;
        [SerializeField] private float _initialTerritoryRadius = 8.5f;

        private readonly Dictionary<byte, BuildFeedback> _lastBuildFeedbackBySeat = new();
        private readonly Dictionary<byte, string> _lastActionNoticeBySeat = new();
        private INetworkSession _session;
        private IActiveOnlineSessionBridge _onlineBridge;
        private IUnlockedCardProvider _localUnlockedCardProvider;
        private bool _subscribedStartScenes;
        private bool _subscribedScenePresence;
        private bool _subscribedRemoteConnectionState;
        private bool _modeOverriddenForTest;

        public MatchMode ActiveMode { get; private set; } = MatchMode.LocalPvP;
        public MatchContext Context { get; private set; }
        public GameModeController GameMode { get; private set; }
        public DraftLoadoutController DraftController { get; private set; }
        public EconomyController EconomyController { get; private set; }
        public TerritoryController TerritoryController { get; private set; }
        public StructureBuildController BuildController { get; private set; }
        public NetworkManager BoundNetworkManager => _networkManager;
        public bool IsUsingPersistentOnlineSession => _onlineBridge != null;
        public bool IsSessionDisconnected => _session != null && (_session.State == SessionState.Faulted || (_onlineBridge != null && _session.State == SessionState.Offline));
        public CardCatalogSO CardCatalog => _cardCatalog;
        public ArenaLayoutConfig ArenaLayout => _arenaLayout;
        public bool IsMatchInitialized { get; private set; }
        public bool HasGameMode => GameMode != null;
        public bool IsDraftAvailable => IsMatchInitialized && DraftController != null && CurrentPhase == MatchPhase.PreMatchDraft;
        public MatchPhase CurrentPhase => GameMode != null ? GameMode.Phase : MatchPhase.PreMatchDraft;
        public TeamId WinnerTeam => GameMode != null ? GameMode.WinnerTeam : TeamId.Neutral;
        public string MatchOutcomeReason => GameMode != null ? GameMode.LastOutcome.Reason : string.Empty;
        public int SeatCount => Context != null ? Context.Seats.Count : 0;
        public int BuiltStructureCount => Context != null ? Context.Structures.Count : 0;
        public string SessionStateSummary => _session != null ? _session.State.ToString() : "Unavailable";
        public string TransportModeSummary => _session != null ? _session.TransportMode.ToString() : "None";
        public string LastError => _session != null ? _session.LastError : "Oturum bileşeni eksik.";

        public event Action MatchInitialized;
        public event Action<byte, bool, DraftRejectReason, LoadoutValidationError, int> DraftFeedbackReceived;
        public event Action DraftStateChanged;
        public event Action DraftCompleted;
        public event Action MatchPhaseChanged;
        public event Action<MatchOutcome> MatchEnded;
        public event Action<BuildFeedback> BuildFeedbackReceived;

        private void Awake()
        {
            IActiveOnlineSessionBridge activeOnline = ActiveOnlineSessionRegistry.Current;
            if (activeOnline != null
                && activeOnline.IsActiveOnlineSession
                && activeOnline.Session != null
                && activeOnline.NetworkManagerObject is NetworkManager persistentNm)
            {
                _onlineBridge = activeOnline;
                _session = activeOnline.Session;
                _networkManager = persistentNm;
                ActiveMode = MatchMode.OnlinePvP;

                if (_localNetworkRig != null && _localNetworkRig.activeSelf)
                {
                    _localNetworkRig.SetActive(false);
                }
            }
            else
            {
                if (_localNetworkRig != null && !_localNetworkRig.activeSelf)
                {
                    _localNetworkRig.SetActive(true);
                }

                _session = _networkSessionComponent as INetworkSession;
                if (_session == null)
                {
                    _session = GetComponent<INetworkSession>();
                }
                if (_session == null && _localNetworkRig != null)
                {
                    _session = _localNetworkRig.GetComponent<INetworkSession>();
                }

                if (_networkManager == null)
                {
                    _networkManager = GetComponent<NetworkManager>();
                }
                if (_networkManager == null && _localNetworkRig != null)
                {
                    _networkManager = _localNetworkRig.GetComponent<NetworkManager>();
                }

                ActiveMode = MatchLaunchConfig.HasExplicitSelection
                    ? MatchLaunchConfig.SelectedMode
                    : MatchMode.LocalPvP;
            }

            if (_inputRouter == null)
            {
                _inputRouter = GetComponent<PlayerInputRouter>();
            }

            ActiveArenaBootstrapRegistry.Register(this);
            EnsureMatchContextInitialized();

            if (_session != null)
            {
                _session.Changed += HandleSessionChanged;
            }
        }

        private void Start()
        {
            if (_onlineBridge == null && !_modeOverriddenForTest)
            {
                ActiveMode = MatchLaunchConfig.HasExplicitSelection
                    ? MatchLaunchConfig.SelectedMode
                    : MatchMode.LocalPvP;
            }

            if (Context != null)
            {
                Context.Mode = ActiveMode;
            }

            if (_inputRouter != null)
            {
                _inputRouter.ConfigureMode(
                    ActiveMode == MatchMode.LocalPvP
                        ? InputRoutingMode.LocalDualPlayer
                        : InputRoutingMode.SinglePlayer);
            }

            if (_session == null || _networkManager == null)
            {
                return;
            }

            SubscribeNetworkEvents();

            if (ActiveMode == MatchMode.LocalPvP)
            {
                if (_session.State == SessionState.Offline || _session.State == SessionState.Faulted)
                {
                    _session.StartLocalHost();
                }
            }
            else if (_session.IsServer && _session.IsAuthenticated)
            {
                TryInitializeServerMatch();
            }

            TryCompleteClientMatchInitialization();
        }

        private void OnDestroy()
        {
            ActiveArenaBootstrapRegistry.Unregister(this);
            DetachMatchObjectCallbacks();
            UnsubscribeNetworkEvents();

            if (_session != null)
            {
                _session.Changed -= HandleSessionChanged;
                if (_onlineBridge == null)
                {
                    _session.Stop();
                }
            }

            Context?.ClearSeats();
        }

        public void ConfigureExternalSessionForTest(
            INetworkSession externalSession,
            NetworkManager externalManager,
            MatchMode mode,
            bool deactivateLocalRig = true)
        {
            if (deactivateLocalRig && _localNetworkRig != null && _localNetworkRig.activeSelf)
            {
                _localNetworkRig.SetActive(false);
            }

            if (_session != null)
            {
                _session.Changed -= HandleSessionChanged;
            }

            UnsubscribeNetworkEvents();

            _session = externalSession;
            _networkManager = externalManager;
            ActiveMode = mode;
            _modeOverriddenForTest = true;

            if (Context != null)
            {
                Context.Mode = mode;
            }

            if (_inputRouter != null)
            {
                _inputRouter.ConfigureMode(
                    mode == MatchMode.LocalPvP
                        ? InputRoutingMode.LocalDualPlayer
                        : InputRoutingMode.SinglePlayer);
            }

            if (_session != null)
            {
                _session.Changed += HandleSessionChanged;
            }

            SubscribeNetworkEvents();
        }

        public void ConfigureAssetsForTest(
            CardCatalogSO cardCatalog,
            PlayerSeatController seatPrefab,
            GameModeController gameModePrefab,
            DraftLoadoutController draftControllerPrefab,
            EconomyController economyControllerPrefab,
            TerritoryController territoryControllerPrefab,
            StructureBuildController buildControllerPrefab,
            StructureBase greyboxStructurePrefab,
            GoldDepositNode goldDepositPrefab,
            AirshipController airshipPrefab,
            BallisticProjectile projectilePrefab,
            int startingGold = 250)
        {
            _cardCatalog = cardCatalog;
            _seatPrefab = seatPrefab;
            _gameModePrefab = gameModePrefab;
            _draftControllerPrefab = draftControllerPrefab;
            _economyControllerPrefab = economyControllerPrefab;
            _territoryControllerPrefab = territoryControllerPrefab;
            _buildControllerPrefab = buildControllerPrefab;
            _greyboxStructurePrefab = greyboxStructurePrefab;
            _goldDepositPrefab = goldDepositPrefab;
            _airshipPrefab = airshipPrefab;
            _projectilePrefab = projectilePrefab;
            _startingGold = startingGold;

            if (Context != null)
            {
                Context.CardCatalog = _cardCatalog;
                Context.ProjectilePrefab = _projectilePrefab;
            }
        }

        public bool IsSeatLocallyOwned(byte seatId)
        {
            return Context != null
                && Context.TryGetSeat(seatId, out PlayerSeatController seat)
                && seat != null
                && seat.IsOwner;
        }

        public bool TryGetLocalOwnedSeatId(out byte seatId)
        {
            if (Context != null)
            {
                for (int i = 0; i < Context.Seats.Count; i++)
                {
                    PlayerSeatController seat = Context.Seats[i];
                    if (seat != null && seat.IsOwner)
                    {
                        seatId = seat.SeatId;
                        return true;
                    }
                }
            }

            seatId = 0;
            return false;
        }

        public void SetLocalUnlockedCardProvider(IUnlockedCardProvider provider)
        {
            _localUnlockedCardProvider = provider;
            if (Context != null)
            {
                Context.LocalUnlockedCards = provider;
            }
        }

        public bool IsSeatDraftReady(byte seatId)
        {
            return DraftController != null && DraftController.IsSeatReady(seatId);
        }

        public void SubmitSeatLoadout(byte seatId, ushort[] fiveCards, bool isReady)
        {
            DraftController?.RequestSubmitLoadout(seatId, fiveCards, isReady);
        }

        public bool TryCopyActiveLoadout(byte seatId, List<ushort> destination)
        {
            if (destination == null || Context == null || !Context.TryGetSeat(seatId, out PlayerSeatController seat))
            {
                return false;
            }

            destination.Clear();
            IReadOnlyList<ushort> active = seat.ActiveLoadout;
            for (int i = 0; i < active.Count; i++)
            {
                destination.Add(active[i]);
            }

            return destination.Count > 0;
        }

        public bool TryGetSeatInfo(int index, out byte seatId, out TeamId team, out SeatControlType controlType, out int gold)
        {
            return TryGetSeatEconomyInfo(index, out seatId, out team, out controlType, out gold, out _);
        }

        public bool TryGetSeatEconomyInfo(
            int index,
            out byte seatId,
            out TeamId team,
            out SeatControlType controlType,
            out int gold,
            out float incomePerSecond)
        {
            if (Context != null && index >= 0 && index < Context.Seats.Count && Context.Seats[index] != null)
            {
                PlayerSeatController seat = Context.Seats[index];
                seatId = seat.SeatId;
                team = seat.Team;
                controlType = seat.ControlType;
                gold = seat.Gold;
                incomePerSecond = seat.IncomePerSecond;
                return true;
            }

            seatId = 0;
            team = TeamId.Neutral;
            controlType = SeatControlType.HumanLocal;
            gold = 0;
            incomePerSecond = 0f;
            return false;
        }

        public bool TryGetTeamTerritorySummary(TeamId team, out int zoneCount, out float minX, out float maxX)
        {
            if (TerritoryController != null)
            {
                zoneCount = TerritoryController.GetZoneCount(team);
                return TerritoryController.TryGetHorizontalExtent(team, out minX, out maxX);
            }

            zoneCount = 0;
            minX = 0f;
            maxX = 0f;
            return false;
        }

        public int GetStructureCountForTeam(TeamId team)
        {
            if (Context == null || team == TeamId.Neutral)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<IStructure> structures = Context.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                IStructure structure = structures[i];
                if (structure != null && structure.IsAlive && structure.IsConstructed && ((IDamageable)structure).Team == team)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetLastBuildFeedback(byte seatId, out BuildFeedback feedback)
        {
            return _lastBuildFeedbackBySeat.TryGetValue(seatId, out feedback);
        }

        public void RequestBuildFromSlot(byte seatId, int slotIndex)
        {
            if (TryGetAirshipCarryInfo(seatId, out bool isCarrying, out _, out int carriedSlot, out _)
                && isCarrying
                && carriedSlot == slotIndex)
            {
                RequestDeployCarried(seatId);
                return;
            }

            RequestBeginCarryFromSlot(seatId, slotIndex);
        }

        public void RequestBeginCarryFromSlot(byte seatId, int slotIndex)
        {
            StructureBuildController controller = BuildController != null
                ? BuildController
                : ActiveBuildControllerRegistry.Current;
            controller?.RequestBeginCarryFromSlot(seatId, slotIndex);
        }

        public void RequestDeployCarried(byte seatId)
        {
            StructureBuildController controller = BuildController != null
                ? BuildController
                : ActiveBuildControllerRegistry.Current;
            if (controller == null)
            {
                return;
            }

            Vector2 targetPos;
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                targetPos = ship.transform.position;
            }
            else if (Context != null && Context.TryGetSeat(seatId, out PlayerSeatController seat) && seat != null)
            {
                targetPos = ResolveTerritoryOrigin(seat.Team);
            }
            else
            {
                targetPos = Vector2.zero;
            }

            controller.RequestDeployCarried(seatId, targetPos);
        }

        public void RequestCancelCarry(byte seatId)
        {
            StructureBuildController controller = BuildController != null
                ? BuildController
                : ActiveBuildControllerRegistry.Current;
            controller?.RequestCancelCarry(seatId);
        }

        public bool TryGetAirshipCarryInfo(
            byte seatId,
            out bool isCarrying,
            out ushort cardId,
            out int slotIndex,
            out int reservedGold)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null && ship.IsCarrying)
            {
                AirshipCarryState carry = ship.CarryState;
                isCarrying = carry.IsCarrying;
                cardId = carry.CardId;
                slotIndex = carry.SlotIndex;
                reservedGold = carry.ReservedGold;
                return true;
            }

            isCarrying = false;
            cardId = 0;
            slotIndex = -1;
            reservedGold = 0;
            return false;
        }

        public int AvailableGoldDepositCount
        {
            get
            {
                int count = 0;
                IReadOnlyList<GoldDepositNode> deposits = ActiveGoldDepositsRegistry.ActiveDeposits;
                for (int i = 0; i < deposits.Count; i++)
                {
                    if (deposits[i] != null && !deposits[i].IsOccupied)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public bool TryGetAirshipInfo(byte seatId, out Vector2 position, out AirshipDockMode dockMode)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                position = ship.transform.position;
                dockMode = ship.DockMode;
                return true;
            }

            position = Vector2.zero;
            dockMode = AirshipDockMode.Flying;
            return false;
        }

        public bool TryGetHqHealth(TeamId team, out float currentHealth, out float maxHealth)
        {
            if (Context != null && Context.TryGetHq(team, out IStructure hq) && hq != null)
            {
                currentHealth = hq.CurrentHealth;
                maxHealth = hq.MaxHealth;
                return true;
            }

            currentHealth = 0f;
            maxHealth = 0f;
            return false;
        }

        public bool TryGetLastActionNotice(byte seatId, out string notice)
        {
            return _lastActionNoticeBySeat.TryGetValue(seatId, out notice);
        }

        public bool TryGetDockedInteractionInfo(
            byte seatId,
            out DockedTargetKind targetKind,
            out DockedInteractionSubMode subMode,
            out int selectedDeckSlot,
            out DockedStructureAction selectedAction,
            out ushort targetCardId,
            out int sellRefundGold,
            out bool canUseTarget,
            out bool supportsUseAction,
            out float targetWeaponCooldownRemaining,
            out bool targetIsConstructed,
            out float targetBuildProgress,
            out Vector2 dockedWorldAnchor)
        {
            if (Context != null
                && Context.TryGetAirship(seatId, out AirshipController ship)
                && ship != null
                && ship.Docking != null
                && ship.Docking.IsDocked)
            {
                AirshipDockState dock = ship.Docking.State;
                targetKind = dock.TargetKind;
                subMode = dock.SubMode;
                selectedDeckSlot = dock.SelectedDeckSlot;
                selectedAction = dock.SelectedAction;
                targetCardId = dock.TargetCardId;
                sellRefundGold = dock.TargetSellRefundGold;
                canUseTarget = dock.CanUseTarget;
                supportsUseAction = dock.SupportsUseAction;
                targetWeaponCooldownRemaining = ship.Docking.TargetWeaponCooldownRemaining;
                targetIsConstructed = dock.TargetKind == DockedTargetKind.Base || ship.Docking.IsDockedTargetConstructed;
                targetBuildProgress = dock.TargetKind == DockedTargetKind.Base ? 1f : ship.Docking.TargetBuildProgress;
                dockedWorldAnchor = ship.Docking.CurrentAnchorPosition;
                return true;
            }

            targetKind = DockedTargetKind.None;
            subMode = DockedInteractionSubMode.None;
            selectedDeckSlot = 0;
            selectedAction = DockedStructureAction.Use;
            targetCardId = 0;
            sellRefundGold = 0;
            canUseTarget = false;
            supportsUseAction = false;
            targetWeaponCooldownRemaining = 0f;
            targetIsConstructed = true;
            targetBuildProgress = 1f;
            dockedWorldAnchor = Vector2.zero;
            return false;
        }

        public void RequestCycleDockedSelection(byte seatId, sbyte delta)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                ship.Docking?.RequestCycleDockedSelection(delta);
            }
        }

        public void RequestConfirmDockedSelection(byte seatId)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                ship.Docking?.RequestConfirmDockedSelection();
            }
        }

        public void RequestToggleDock(byte seatId)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                ship.Docking?.RequestToggleDock();
            }
        }

        public void RequestExitWeaponControl(byte seatId)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                ship.Docking?.RequestExitWeaponControl();
            }
        }

        public void ReturnToMainMenu()
        {
            DetachMatchObjectCallbacks();
            UnsubscribeNetworkEvents();

            IActiveOnlineSessionBridge bridge = _onlineBridge;
            _onlineBridge = null;

            if (bridge != null)
            {
                bridge.LeaveAndDestroyForMenuReturn();
            }
            else if (_session != null)
            {
                _session.Stop();
            }

            Context?.ClearSeats();
            MatchLaunchConfig.Reset();

            if (Application.CanStreamedLevelBeLoaded(GameSceneNames.MainMenu))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameSceneNames.MainMenu, LoadSceneMode.Single);
            }
        }

        private void EnsureMatchContextInitialized()
        {
            if (Context != null)
            {
                Context.Mode = ActiveMode;
                if (_cardCatalog != null) Context.CardCatalog = _cardCatalog;
                if (_localUnlockedCardProvider != null) Context.LocalUnlockedCards = _localUnlockedCardProvider;
                if (_projectilePrefab != null) Context.ProjectilePrefab = _projectilePrefab;
                return;
            }

            Rect playableAirspace = _arenaLayout != null ? _arenaLayout.PlayableAirspace : new Rect(-18f, -2f, 36f, 16f);
            Context = new MatchContext
            {
                Mode = ActiveMode,
                CardCatalog = _cardCatalog,
                LocalUnlockedCards = _localUnlockedCardProvider,
                Terrain = new ArenaGroundTerrainService(playableAirspace),
                ProjectilePrefab = _projectilePrefab,
                PlayableAirspace = playableAirspace
            };
        }

        public void RegisterReplicatedGameMode(GameModeController mode)
        {
            if (mode == null || ReferenceEquals(GameMode, mode))
            {
                return;
            }

            if (GameMode != null)
            {
                GameMode.PhaseChanged -= HandleGameModePhaseChanged;
                GameMode.MatchEnded -= HandleGameModeMatchEnded;
            }

            EnsureMatchContextInitialized();
            GameMode = mode;
            GameMode.PhaseChanged += HandleGameModePhaseChanged;
            GameMode.MatchEnded += HandleGameModeMatchEnded;
            Context.CanPilotAirships = () => GameMode == null || GameMode.Phase == MatchPhase.Playing;
            TryCompleteClientMatchInitialization();
        }

        public void UnregisterReplicatedGameMode(GameModeController mode)
        {
            if (mode == null || !ReferenceEquals(GameMode, mode))
            {
                return;
            }

            GameMode.PhaseChanged -= HandleGameModePhaseChanged;
            GameMode.MatchEnded -= HandleGameModeMatchEnded;
            GameMode = null;
        }

        public void RegisterReplicatedDraftController(DraftLoadoutController draft)
        {
            if (draft == null || ReferenceEquals(DraftController, draft))
            {
                return;
            }

            if (DraftController != null)
            {
                DraftController.SubmissionFeedbackReceived -= HandleDraftFeedback;
                DraftController.DraftStateChanged -= HandleDraftStateChanged;
                DraftController.DraftCompleted -= HandleDraftCompleted;
            }

            DraftController = draft;
            DraftController.SubmissionFeedbackReceived += HandleDraftFeedback;
            DraftController.DraftStateChanged += HandleDraftStateChanged;
            DraftController.DraftCompleted += HandleDraftCompleted;
            TryCompleteClientMatchInitialization();
        }

        public void UnregisterReplicatedDraftController(DraftLoadoutController draft)
        {
            if (draft == null || !ReferenceEquals(DraftController, draft))
            {
                return;
            }

            DraftController.SubmissionFeedbackReceived -= HandleDraftFeedback;
            DraftController.DraftStateChanged -= HandleDraftStateChanged;
            DraftController.DraftCompleted -= HandleDraftCompleted;
            DraftController = null;
        }

        public void RegisterReplicatedEconomyController(EconomyController economy)
        {
            if (economy == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            EconomyController = economy;
            Context.Economy = economy;
        }

        public void UnregisterReplicatedEconomyController(EconomyController economy)
        {
            if (economy != null && ReferenceEquals(EconomyController, economy))
            {
                EconomyController = null;
                if (Context != null && ReferenceEquals(Context.Economy, economy))
                {
                    Context.Economy = null;
                }
            }
        }

        public void RegisterReplicatedTerritoryController(TerritoryController territory)
        {
            if (territory == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            TerritoryController = territory;
            Context.Territory = territory;
        }

        public void UnregisterReplicatedTerritoryController(TerritoryController territory)
        {
            if (territory != null && ReferenceEquals(TerritoryController, territory))
            {
                TerritoryController = null;
                if (Context != null && ReferenceEquals(Context.Territory, territory))
                {
                    Context.Territory = null;
                }
            }
        }

        public void RegisterReplicatedBuildController(StructureBuildController buildCtrl)
        {
            if (buildCtrl == null || ReferenceEquals(BuildController, buildCtrl))
            {
                return;
            }

            if (BuildController != null)
            {
                BuildController.BuildFeedbackReceived -= HandleBuildFeedback;
                BuildController.StructureSold -= HandleStructureSold;
            }

            EnsureMatchContextInitialized();
            BuildController = buildCtrl;
            Context.BuildController = buildCtrl;
            BuildController.BuildFeedbackReceived += HandleBuildFeedback;
            BuildController.StructureSold += HandleStructureSold;
        }

        public void UnregisterReplicatedBuildController(StructureBuildController buildCtrl)
        {
            if (buildCtrl == null || !ReferenceEquals(BuildController, buildCtrl))
            {
                return;
            }

            BuildController.BuildFeedbackReceived -= HandleBuildFeedback;
            BuildController.StructureSold -= HandleStructureSold;
            BuildController = null;
            if (Context != null && ReferenceEquals(Context.BuildController, buildCtrl))
            {
                Context.BuildController = null;
            }
        }

        public void RegisterReplicatedSeat(PlayerSeatController seat)
        {
            if (seat == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            Context.RegisterSeat(seat);
            TryCompleteClientMatchInitialization();
        }

        public void UnregisterReplicatedSeat(PlayerSeatController seat)
        {
            if (seat != null)
            {
                Context?.UnregisterSeat(seat);
            }
        }

        public void RegisterReplicatedAirship(AirshipController airship)
        {
            if (airship == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            Context.RegisterAirship(airship);
        }

        public void UnregisterReplicatedAirship(AirshipController airship)
        {
            if (airship != null)
            {
                Context?.UnregisterAirship(airship);
            }
        }

        public void RegisterReplicatedStructure(StructureBase structure)
        {
            if (structure == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            Context.RegisterStructure(structure);
            if (structure.Category == StructureCategory.Core && structure.Team != TeamId.Neutral)
            {
                Context.RegisterHq(structure.Team, structure);
            }
        }

        public void UnregisterReplicatedStructure(StructureBase structure)
        {
            if (structure == null)
            {
                return;
            }

            Context?.UnregisterStructure(structure);
            Context?.UnregisterDockable(structure);
        }

        public void RegisterReplicatedGoldDeposit(GoldDepositNode deposit)
        {
            if (deposit == null)
            {
                return;
            }

            EnsureMatchContextInitialized();
            Context.RegisterGoldDeposit(deposit);
        }

        public void UnregisterReplicatedGoldDeposit(GoldDepositNode deposit)
        {
            if (deposit != null)
            {
                Context?.UnregisterGoldDeposit(deposit);
            }
        }

        private void TryCompleteClientMatchInitialization()
        {
            if (IsMatchInitialized
                || _networkManager == null
                || _networkManager.IsServerStarted
                || GameMode == null
                || Context == null
                || Context.Seats.Count == 0)
            {
                return;
            }

            IsMatchInitialized = true;
            MatchInitialized?.Invoke();
        }

        private void SubscribeNetworkEvents()
        {
            if (_networkManager == null || !_networkManager.Initialized)
            {
                return;
            }

            if (_networkManager.SceneManager != null)
            {
                if (!_subscribedStartScenes)
                {
                    _networkManager.SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
                    _subscribedStartScenes = true;
                }

                if (!_subscribedScenePresence)
                {
                    _networkManager.SceneManager.OnClientPresenceChangeEnd += HandleClientPresenceChangeEnd;
                    _subscribedScenePresence = true;
                }
            }

            if (_networkManager.ServerManager != null && !_subscribedRemoteConnectionState)
            {
                _networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
                _subscribedRemoteConnectionState = true;
            }
        }

        private void UnsubscribeNetworkEvents()
        {
            if (_networkManager == null)
            {
                _subscribedStartScenes = false;
                _subscribedScenePresence = false;
                _subscribedRemoteConnectionState = false;
                return;
            }

            if (_networkManager.SceneManager != null)
            {
                if (_subscribedStartScenes)
                {
                    _networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
                    _subscribedStartScenes = false;
                }

                if (_subscribedScenePresence)
                {
                    _networkManager.SceneManager.OnClientPresenceChangeEnd -= HandleClientPresenceChangeEnd;
                    _subscribedScenePresence = false;
                }
            }

            if (_networkManager.ServerManager != null && _subscribedRemoteConnectionState)
            {
                _networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
                _subscribedRemoteConnectionState = false;
            }
        }

        private void HandleSessionChanged()
        {
            if (IsMatchInitialized || _session == null)
            {
                return;
            }

            if (_session.IsServer && _session.IsAuthenticated)
            {
                TryInitializeServerMatch();
            }
        }

        private void HandleClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer || IsMatchInitialized || _session == null)
            {
                return;
            }

            if (_session.IsServer && _session.IsAuthenticated)
            {
                TryInitializeServerMatch();
            }
        }

        private void HandleClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            if (IsMatchInitialized || _session == null || !args.Added)
            {
                return;
            }

            if (_session.IsServer && _session.IsAuthenticated)
            {
                TryInitializeServerMatch();
            }
        }

        private void HandleRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (IsMatchInitialized || _session == null || args.ConnectionState != RemoteConnectionState.Started)
            {
                return;
            }

            if (_session.IsServer && _session.IsAuthenticated)
            {
                TryInitializeServerMatch();
            }
        }

        private void TryInitializeServerMatch()
        {
            if (IsMatchInitialized || _networkManager == null || !_networkManager.IsServerStarted)
            {
                return;
            }

            if (_seatPrefab == null)
            {
                Debug.LogWarning("[M1-1] ArenaSessionBootstrap üzerinde PlayerSeat prefab atanmadı.");
                return;
            }

            NetworkConnection hostConnection = ResolveHostConnection();
            if (hostConnection == null || !hostConnection.LoadedStartScenes(true))
            {
                return;
            }

            Scene targetScene = gameObject.scene;
            NetworkConnection remoteConnection = null;
            if (ActiveMode == MatchMode.OnlinePvP)
            {
                if (_onlineBridge != null && !hostConnection.Scenes.Contains(targetScene))
                {
                    return;
                }

                remoteConnection = ResolveRemoteConnection(hostConnection, _onlineBridge != null, targetScene);
                if (remoteConnection == null)
                {
                    return;
                }
            }

            DetachMatchObjectCallbacks();

            Rect playableAirspace = _arenaLayout != null ? _arenaLayout.PlayableAirspace : new Rect(-18f, -2f, 36f, 16f);

            _lastBuildFeedbackBySeat.Clear();
            _lastActionNoticeBySeat.Clear();

            Context = new MatchContext
            {
                Mode = ActiveMode,
                CardCatalog = _cardCatalog,
                LocalUnlockedCards = _localUnlockedCardProvider,
                Terrain = new ArenaGroundTerrainService(playableAirspace),
                ProjectilePrefab = _projectilePrefab,
                PlayableAirspace = playableAirspace
            };

            MatchSeatFactory.SpawnSeatsServer(
                _networkManager,
                _seatPrefab,
                ActiveMode,
                hostConnection,
                remoteConnection,
                Context,
                _startingGold,
                targetScene);

            if (_gameModePrefab != null)
            {
                GameModeController modeInstance = Instantiate(_gameModePrefab);
                modeInstance.name = "GameModeController";
                _networkManager.ServerManager.Spawn(modeInstance.NetworkObject, null, targetScene);
                modeInstance.InitializeMatchServer(ActiveMode, new HqDestructionMatchRule(), Context);
                GameMode = modeInstance;
                GameMode.PhaseChanged += HandleGameModePhaseChanged;
                GameMode.MatchEnded += HandleGameModeMatchEnded;
                Context.CanPilotAirships = () => GameMode == null || GameMode.Phase == MatchPhase.Playing;
            }
            else
            {
                Context.CanPilotAirships = () => true;
            }

            if (_economyControllerPrefab != null)
            {
                EconomyController economyInstance = Instantiate(_economyControllerPrefab);
                economyInstance.name = "EconomyController";
                _networkManager.ServerManager.Spawn(economyInstance.NetworkObject, null, targetScene);
                economyInstance.InitializeServer(Context, GameMode, _baseIncomePerSecond);
                EconomyController = economyInstance;
            }

            if (_territoryControllerPrefab != null)
            {
                TerritoryController territoryInstance = Instantiate(_territoryControllerPrefab);
                territoryInstance.name = "TerritoryController";
                _networkManager.ServerManager.Spawn(territoryInstance.NetworkObject, null, targetScene);
                territoryInstance.InitializeServer(
                    Context,
                    ResolveTerritoryOrigin(TeamId.TeamA),
                    ResolveTerritoryOrigin(TeamId.TeamB),
                    _initialTerritoryRadius);
                TerritoryController = territoryInstance;
            }

            if (_buildControllerPrefab != null)
            {
                StructureBuildController buildInstance = Instantiate(_buildControllerPrefab);
                buildInstance.name = "StructureBuildController";
                _networkManager.ServerManager.Spawn(buildInstance.NetworkObject, null, targetScene);
                buildInstance.InitializeServer(Context, GameMode, _cardCatalog, _greyboxStructurePrefab);
                BuildController = buildInstance;
                BuildController.BuildFeedbackReceived += HandleBuildFeedback;
                BuildController.StructureSold += HandleStructureSold;
            }

            if (_draftControllerPrefab != null)
            {
                DraftLoadoutController draftInstance = Instantiate(_draftControllerPrefab);
                draftInstance.name = "DraftLoadoutController";
                _networkManager.ServerManager.Spawn(draftInstance.NetworkObject, null, targetScene);
                draftInstance.InitializeServer(Context, GameMode);
                DraftController = draftInstance;
                DraftController.SubmissionFeedbackReceived += HandleDraftFeedback;
                DraftController.DraftStateChanged += HandleDraftStateChanged;
                DraftController.DraftCompleted += HandleDraftCompleted;
            }

            SpawnInitialArenaEntities(playableAirspace, targetScene);

            IsMatchInitialized = true;
            MatchInitialized?.Invoke();
        }

        private void SpawnInitialArenaEntities(Rect playableAirspace, Scene targetScene)
        {
            if (_greyboxStructurePrefab != null)
            {
                SpawnHqStructure(TeamId.TeamA, targetScene);
                SpawnHqStructure(TeamId.TeamB, targetScene);
            }
            else if (_dockablePadPrefab != null)
            {
                SpawnDockPad(TeamId.TeamA, targetScene);
                SpawnDockPad(TeamId.TeamB, targetScene);
            }

            if (_goldDepositPrefab != null)
            {
                SpawnGoldDeposits(targetScene);
            }

            if (_airshipPrefab == null)
            {
                return;
            }

            for (int i = 0; i < Context.Seats.Count; i++)
            {
                PlayerSeatController seat = Context.Seats[i];
                if (seat == null)
                {
                    continue;
                }

                Vector3 spawnPos = ResolveAirshipSpawnPosition(seat.Team);
                AirshipController ship = Instantiate(_airshipPrefab, spawnPos, Quaternion.identity);
                ship.name = $"Airship_{seat.SeatId}_{seat.Team}";
                _networkManager.ServerManager.Spawn(ship.NetworkObject, seat.Owner, targetScene);
                ship.InitializeServer(seat.SeatId, seat.Team, Context, playableAirspace);
            }
        }

        private void SpawnGoldDeposits(Scene targetScene)
        {
            IReadOnlyList<Transform> layoutSpawns = _arenaLayout != null ? _arenaLayout.GoldDepositSpawns : null;
            if (layoutSpawns != null && layoutSpawns.Count > 0)
            {
                int depositId = 1;
                for (int i = 0; i < layoutSpawns.Count; i++)
                {
                    if (layoutSpawns[i] == null)
                    {
                        continue;
                    }

                    SpawnGoldDepositNode(depositId, layoutSpawns[i].position, targetScene);
                    depositId++;
                }
                return;
            }

            Vector3[] fallbackPositions =
            {
                new(-9.5f, -0.5f, 0f),
                new(-2.0f, -1.25f, 0f),
                new(2.0f, -1.25f, 0f),
                new(9.5f, -0.5f, 0f)
            };

            for (int i = 0; i < fallbackPositions.Length; i++)
            {
                SpawnGoldDepositNode(i + 1, fallbackPositions[i], targetScene);
            }
        }

        private void SpawnGoldDepositNode(int depositId, Vector3 worldPos, Scene targetScene)
        {
            GoldDepositNode node = Instantiate(_goldDepositPrefab, worldPos, Quaternion.identity);
            node.name = $"GoldDeposit_{depositId}";
            _networkManager.ServerManager.Spawn(node.NetworkObject, null, targetScene);
            node.InitializeServer(depositId, Context);
        }

        private void SpawnHqStructure(TeamId team, Scene targetScene)
        {
            StructureCardSO hqCard = null;
            _cardCatalog?.TryGetCard(99, out hqCard);

            ushort cardId = hqCard != null ? hqCard.CardId : (ushort)99;
            float maxHealth = hqCard != null ? hqCard.MaxHealth : 500f;
            Vector2 footprint = hqCard != null ? hqCard.FootprintSize : new Vector2(2.4f, 2.2f);
            byte seatId = ResolveTeamSeatId(team);

            Vector3 rawPos = ResolveHqSpawnPosition(team);
            float surfaceY = Context?.Terrain != null ? Context.Terrain.SampleGroundSurfaceY(rawPos.x) : rawPos.y;
            Vector3 hqPos = new(rawPos.x, surfaceY + footprint.y * 0.5f, 0f);

            StructureBase hq = Instantiate(_greyboxStructurePrefab, hqPos, Quaternion.identity);
            hq.name = $"HQ_{team}";
            _networkManager.ServerManager.Spawn(hq.NetworkObject, null, targetScene);
            hq.InitializeServer(
                cardId,
                seatId,
                team,
                StructureCategory.Core,
                maxHealth,
                footprint,
                territoryRadius: 0f,
                incomeBonus: 0f,
                Context);
            Context?.RegisterHq(team, hq);
        }

        private byte ResolveTeamSeatId(TeamId team)
        {
            if (Context != null)
            {
                for (int i = 0; i < Context.Seats.Count; i++)
                {
                    PlayerSeatController seat = Context.Seats[i];
                    if (seat != null && seat.Team == team)
                    {
                        return seat.SeatId;
                    }
                }
            }

            return team == TeamId.TeamB ? (byte)1 : (byte)0;
        }

        private void SpawnDockPad(TeamId team, Scene targetScene)
        {
            Vector3 padPos = ResolveHqSpawnPosition(team);
            DockableAnchorNode pad = Instantiate(_dockablePadPrefab, padPos, Quaternion.identity);
            pad.name = $"DockableAnchorPad_{team}";
            _networkManager.ServerManager.Spawn(pad.NetworkObject, null, targetScene);
            pad.InitializeServer(team, Context);
        }

        private Vector2 ResolveTerritoryOrigin(TeamId team)
        {
            Transform target = _arenaLayout != null ? _arenaLayout.GetTerritoryOrigin(team) : null;
            if (target != null)
            {
                return target.position;
            }

            return ResolveHqSpawnPosition(team);
        }

        private Vector3 ResolveHqSpawnPosition(TeamId team)
        {
            Transform target = _arenaLayout != null ? _arenaLayout.GetHqSpawn(team) : null;
            if (target != null)
            {
                return target.position;
            }

            return team == TeamId.TeamA ? new Vector3(-12.5f, -0.5f, 0f) : new Vector3(12.5f, -0.5f, 0f);
        }

        private Vector3 ResolveAirshipSpawnPosition(TeamId team)
        {
            Transform target = _arenaLayout != null ? _arenaLayout.GetAirshipSpawn(team) : null;
            if (target != null)
            {
                return target.position;
            }

            return team == TeamId.TeamA ? new Vector3(-11f, 4.5f, 0f) : new Vector3(11f, 4.5f, 0f);
        }

        private void DetachMatchObjectCallbacks()
        {
            if (GameMode != null)
            {
                GameMode.PhaseChanged -= HandleGameModePhaseChanged;
                GameMode.MatchEnded -= HandleGameModeMatchEnded;
            }

            if (DraftController != null)
            {
                DraftController.SubmissionFeedbackReceived -= HandleDraftFeedback;
                DraftController.DraftStateChanged -= HandleDraftStateChanged;
                DraftController.DraftCompleted -= HandleDraftCompleted;
            }

            if (BuildController != null)
            {
                BuildController.BuildFeedbackReceived -= HandleBuildFeedback;
                BuildController.StructureSold -= HandleStructureSold;
            }
        }

        private void HandleBuildFeedback(BuildFeedback feedback)
        {
            _lastBuildFeedbackBySeat[feedback.SeatId] = feedback;
            BuildFeedbackReceived?.Invoke(feedback);
        }

        private void HandleStructureSold(byte seatId, ushort cardId, int refundedGold)
        {
            string cardName = _cardCatalog != null && _cardCatalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.DisplayName
                : $"#{cardId}";
            _lastActionNoticeBySeat[seatId] = $"{cardName} satıldı (+{refundedGold}g)";
        }

        private void HandleGameModePhaseChanged(MatchPhase prev, MatchPhase next)
        {
            MatchPhaseChanged?.Invoke();
        }

        private void HandleGameModeMatchEnded(MatchOutcome outcome)
        {
            MatchPhaseChanged?.Invoke();
            MatchEnded?.Invoke(outcome);
        }

        private void HandleDraftFeedback(
            byte slotId,
            bool accepted,
            DraftRejectReason rejectReason,
            LoadoutValidationError loadoutError,
            int failingSlotIndex)
        {
            DraftFeedbackReceived?.Invoke(slotId, accepted, rejectReason, loadoutError, failingSlotIndex);
        }

        private void HandleDraftStateChanged()
        {
            DraftStateChanged?.Invoke();
        }

        private void HandleDraftCompleted()
        {
            DraftCompleted?.Invoke();
        }

        private NetworkConnection ResolveHostConnection()
        {
            if (_networkManager.ClientManager != null
                && _networkManager.ClientManager.Connection != null
                && _networkManager.ClientManager.Connection.ClientId >= 0
                && _networkManager.ServerManager.Clients.TryGetValue(
                    _networkManager.ClientManager.Connection.ClientId,
                    out NetworkConnection localHostConn))
            {
                return localHostConn;
            }

            foreach (NetworkConnection conn in _networkManager.ServerManager.Clients.Values)
            {
                if (conn != null && conn.IsActive)
                {
                    return conn;
                }
            }

            return null;
        }

        private NetworkConnection ResolveRemoteConnection(NetworkConnection hostConnection, bool requireTargetScenePresence, Scene targetScene)
        {
            if (_networkManager == null || _networkManager.ServerManager == null)
            {
                return null;
            }

            foreach (NetworkConnection conn in _networkManager.ServerManager.Clients.Values)
            {
                if (conn == null || !conn.IsActive || !conn.IsAuthenticated || !conn.LoadedStartScenes(true))
                {
                    continue;
                }

                if (hostConnection != null && conn.ClientId == hostConnection.ClientId)
                {
                    continue;
                }

                if (requireTargetScenePresence && !conn.Scenes.Contains(targetScene))
                {
                    continue;
                }

                return conn;
            }

            return null;
        }
    }
}
