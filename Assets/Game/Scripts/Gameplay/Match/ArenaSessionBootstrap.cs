using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using Game.Core;
using Game.Data;
using Game.Input;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    public sealed class ArenaSessionBootstrap : MonoBehaviour
    {
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
        private INetworkSession _session;
        private IUnlockedCardProvider _localUnlockedCardProvider;
        private bool _subscribedStartScenes;

        public MatchMode ActiveMode { get; private set; } = MatchMode.LocalPvP;
        public MatchContext Context { get; private set; }
        public GameModeController GameMode { get; private set; }
        public DraftLoadoutController DraftController { get; private set; }
        public EconomyController EconomyController { get; private set; }
        public TerritoryController TerritoryController { get; private set; }
        public StructureBuildController BuildController { get; private set; }
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
            _session = _networkSessionComponent as INetworkSession;
            if (_session == null)
            {
                _session = GetComponent<INetworkSession>();
            }

            if (_networkManager == null)
            {
                _networkManager = GetComponent<NetworkManager>();
            }

            if (_inputRouter == null)
            {
                _inputRouter = GetComponent<PlayerInputRouter>();
            }

            if (_session != null)
            {
                _session.Changed += HandleSessionChanged;
            }
        }

        private void Start()
        {
            ActiveMode = MatchLaunchConfig.HasExplicitSelection
                ? MatchLaunchConfig.SelectedMode
                : MatchMode.LocalPvP;

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

            if (_networkManager.Initialized && _networkManager.SceneManager != null && !_subscribedStartScenes)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
                _subscribedStartScenes = true;
            }

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
        }

        private void OnDestroy()
        {
            DetachMatchObjectCallbacks();

            if (_subscribedStartScenes && _networkManager != null && _networkManager.SceneManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
                _subscribedStartScenes = false;
            }

            if (_session != null)
            {
                _session.Changed -= HandleSessionChanged;
                _session.Stop();
            }

            Context?.ClearSeats();
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
                if (structure != null && structure.IsAlive && ((IDamageable)structure).Team == team)
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

        public bool TryGetAirshipHealth(byte seatId, out float currentHealth, out float maxHealth, out bool isDowned)
        {
            if (Context != null && Context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                currentHealth = ship.CurrentHealth;
                maxHealth = ship.MaxHealth;
                isDowned = ship.IsDowned;
                return true;
            }

            currentHealth = 0f;
            maxHealth = 0f;
            isDowned = false;
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

        public void ReturnToMainMenu()
        {
            if (_session != null)
            {
                _session.Stop();
            }

            Context?.ClearSeats();
            MatchLaunchConfig.Reset();

            if (Application.CanStreamedLevelBeLoaded(GameSceneNames.MainMenu))
            {
                SceneManager.LoadScene(GameSceneNames.MainMenu, LoadSceneMode.Single);
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

            DetachMatchObjectCallbacks();

            Rect playableAirspace = _arenaLayout != null ? _arenaLayout.PlayableAirspace : new Rect(-18f, -2f, 36f, 16f);

            _lastBuildFeedbackBySeat.Clear();

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
                null,
                Context,
                _startingGold);

            if (_gameModePrefab != null)
            {
                GameModeController modeInstance = Instantiate(_gameModePrefab);
                modeInstance.name = "GameModeController";
                _networkManager.ServerManager.Spawn(modeInstance.NetworkObject);
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
                _networkManager.ServerManager.Spawn(economyInstance.NetworkObject);
                economyInstance.InitializeServer(Context, GameMode, _baseIncomePerSecond);
                EconomyController = economyInstance;
            }

            if (_territoryControllerPrefab != null)
            {
                TerritoryController territoryInstance = Instantiate(_territoryControllerPrefab);
                territoryInstance.name = "TerritoryController";
                _networkManager.ServerManager.Spawn(territoryInstance.NetworkObject);
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
                _networkManager.ServerManager.Spawn(buildInstance.NetworkObject);
                buildInstance.InitializeServer(Context, GameMode, _cardCatalog, _greyboxStructurePrefab);
                BuildController = buildInstance;
                BuildController.BuildFeedbackReceived += HandleBuildFeedback;
            }

            if (_draftControllerPrefab != null)
            {
                DraftLoadoutController draftInstance = Instantiate(_draftControllerPrefab);
                draftInstance.name = "DraftLoadoutController";
                _networkManager.ServerManager.Spawn(draftInstance.NetworkObject);
                draftInstance.InitializeServer(Context, GameMode);
                DraftController = draftInstance;
                DraftController.SubmissionFeedbackReceived += HandleDraftFeedback;
                DraftController.DraftStateChanged += HandleDraftStateChanged;
                DraftController.DraftCompleted += HandleDraftCompleted;
            }

            SpawnInitialArenaEntities(playableAirspace);

            IsMatchInitialized = true;
            MatchInitialized?.Invoke();
        }

        private void SpawnInitialArenaEntities(Rect playableAirspace)
        {
            if (_greyboxStructurePrefab != null)
            {
                SpawnHqStructure(TeamId.TeamA);
                SpawnHqStructure(TeamId.TeamB);
            }
            else if (_dockablePadPrefab != null)
            {
                SpawnDockPad(TeamId.TeamA);
                SpawnDockPad(TeamId.TeamB);
            }

            if (_goldDepositPrefab != null)
            {
                SpawnGoldDeposits();
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
                _networkManager.ServerManager.Spawn(ship.NetworkObject, seat.Owner);
                ship.InitializeServer(seat.SeatId, seat.Team, Context, playableAirspace);
            }
        }

        private void SpawnGoldDeposits()
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

                    SpawnGoldDepositNode(depositId, layoutSpawns[i].position);
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
                SpawnGoldDepositNode(i + 1, fallbackPositions[i]);
            }
        }

        private void SpawnGoldDepositNode(int depositId, Vector3 worldPos)
        {
            GoldDepositNode node = Instantiate(_goldDepositPrefab, worldPos, Quaternion.identity);
            node.name = $"GoldDeposit_{depositId}";
            _networkManager.ServerManager.Spawn(node.NetworkObject);
            node.InitializeServer(depositId, Context);
        }

        private void SpawnHqStructure(TeamId team)
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
            _networkManager.ServerManager.Spawn(hq.NetworkObject);
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

        private void SpawnDockPad(TeamId team)
        {
            Vector3 padPos = ResolveHqSpawnPosition(team);
            DockableAnchorNode pad = Instantiate(_dockablePadPrefab, padPos, Quaternion.identity);
            pad.name = $"DockableAnchorPad_{team}";
            _networkManager.ServerManager.Spawn(pad.NetworkObject);
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
            }
        }

        private void HandleBuildFeedback(BuildFeedback feedback)
        {
            _lastBuildFeedbackBySeat[feedback.SeatId] = feedback;
            BuildFeedbackReceived?.Invoke(feedback);
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
    }
}
