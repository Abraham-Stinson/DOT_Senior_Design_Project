using System;
using FishNet.Managing.Scened;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateGame.Networking
{
    [DisallowMultipleComponent]
    public sealed class OnlineSessionCoordinator : MonoBehaviour, IActiveOnlineSessionBridge
    {
        public const string DefaultArenaSceneName = "CannonBrawlArena";

        [SerializeField] private SteamRuntime steam;
        [SerializeField] private FishNetSession session;
        private ulong observedLobby;
        private ulong originalOwner;
        private float lobbyDeadline;
        private bool tearingDown;
        private bool launchInviteHandled;
        private bool promotedToDontDestroy;
        private bool sceneEventsSubscribed;

        public SteamRuntime Steam => steam;
        public FishNetSession Session => session;
        public SteamLobbyService Lobby { get; private set; }
        public string LastError { get; private set; } = "";
        public bool IsInMatchScene { get; private set; }
        public bool IsStartingMatch { get; private set; }

        public bool CanStartMatch =>
            !tearingDown
            && !IsStartingMatch
            && Lobby != null
            && session != null
            && SessionRules.CanStartOnlineMatch(
                Lobby.IsOwner,
                Lobby.InLobby,
                session.IsAuthenticated,
                Lobby.Members.Count,
                session.ConnectedClients);

        INetworkSession IActiveOnlineSessionBridge.Session => session;
        object IActiveOnlineSessionBridge.NetworkManagerObject => session != null ? session.Manager : null;
        bool IActiveOnlineSessionBridge.IsActiveOnlineSession =>
            !tearingDown
            && session != null
            && session.Manager != null
            && (IsInMatchScene || IsStartingMatch || session.IsAuthenticated);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnlineSessionStatics()
        {
            ActiveOnlineSessionRegistry.Clear();
        }

        private void Start()
        {
            steam.Changed += OnSteamChanged;
            steam.BeforeShutdown += ShutdownSession;
            SubscribeSceneEvents();
            OnSteamChanged();
        }

        private void OnSteamChanged()
        {
            SubscribeSceneEvents();
            if (!steam.IsReady || Lobby != null) return;
            Lobby = new SteamLobbyService(steam, steam.Settings);
            Lobby.JoinRequested += JoinLobby;
            session.ConfigureAdmission(IsAllowedMember, steam.Settings.ConnectionTimeoutSeconds);
            if (!launchInviteHandled)
            {
                launchInviteHandled = true;
                if (SessionRules.TryReadLobbyArgument(Environment.GetCommandLineArgs(), out ulong id)) JoinLobby(id);
            }
        }

        private bool IsAllowedMember(ulong id, int accepted, bool duplicate)
        {
            // Query native membership at authentication time, not a client-supplied claim.
            return !tearingDown && steam.IsReady && Lobby != null && Lobby.IsOwner
                && Lobby.OwnerId == originalOwner && Lobby.ContainsMember(id)
                && SessionRules.CanAdmit(id, steam.UserId, Lobby.OwnerId, Lobby.Members, accepted, duplicate);
        }

        private void Update()
        {
            if (Lobby == null || !steam.IsReady || tearingDown) return;
            Lobby.Tick();
            if (!Lobby.InLobby) return;
            if (Lobby.LobbyId != observedLobby)
            {
                observedLobby = Lobby.LobbyId;
                originalOwner = Lobby.OwnerId;
                lobbyDeadline = Time.unscaledTime + steam.Settings.ConnectionTimeoutSeconds;
            }

            if (originalOwner == 0 || Lobby.OwnerId != originalOwner)
            { EndWithError("Lobby owner ayrıldı veya değişti. Host migration bu milestone'da desteklenmiyor; yeni lobby oluşturun."); return; }
            if (!Lobby.ContainsMember(steam.UserId))
            { EndWithError("Steam lobby üyeliğiniz sonlandı."); return; }
            if (Lobby.HostId != originalOwner || !Lobby.MetadataCompatible())
            { EndWithError("Lobby owner/host kimliği veya ağ protokolü uyuşmuyor."); return; }
            if (session.State == SessionState.Faulted && session.Role == SessionRole.None)
            { EndWithError(session.LastError); return; }

            if (session.Role == SessionRole.None)
            {
                if (Lobby.IsOwner) session.StartHost(steam.UserId);
                else if (Lobby.HostReady) session.StartClient(Lobby.HostId);
                else if (Time.unscaledTime >= lobbyDeadline)
                    EndWithError("Lobby'ye girildi ancak FishNet host hazır olmadı; bağlantı zaman aşımına uğradı.");
            }
            if (Lobby != null && Lobby.IsOwner && session.State == SessionState.Connected
                && session.IsServer && session.IsClient && session.IsAuthenticated && !Lobby.HostReady)
            {
                if (!Lobby.PublishHostReady(true)) EndWithError(Lobby.LastError);
            }
        }

        public void CreateLobby()
        {
            if (Lobby == null) { LastError = "Önce Steam'e giriş yapın."; return; }
            LastError = "";
            if (!Lobby.InLobby && !Lobby.IsBusy) session.ClearFailure();
            Lobby.CreateLobby();
        }

        public void JoinLobby(ulong id)
        {
            if (Lobby == null || !steam.IsReady) { LastError = "Önce Steam'e giriş yapın."; return; }
            if (!new CSteamID(id).IsLobby()) { LastError = "Geçerli bir Steam Lobby ID girin."; return; }
            if (Lobby.LobbyId == id) return;
            LeaveLobby();
            LastError = "";
            session.ClearFailure();
            Lobby.JoinLobby(id);
        }

        public bool TryStartOnlineMatch(string arenaSceneName = DefaultArenaSceneName)
        {
            if (string.IsNullOrWhiteSpace(arenaSceneName))
            {
                LastError = "Geçersiz arena sahnesi adı.";
                return false;
            }

            if (!CanStartMatch)
            {
                LastError = "Maçı başlatmak için host olmanız ve 2/2 oyuncunun bağlı olması gerekir.";
                return false;
            }

            if (session == null || session.Manager == null || !session.Manager.Initialized || session.Manager.SceneManager == null)
            {
                LastError = "FishNet SceneManager hazır değil.";
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(arenaSceneName))
            {
                LastError = $"Sahne Build Settings içinde bulunamadı: {arenaSceneName}.";
                return false;
            }

            SubscribeSceneEvents();
            LastError = "";
            IsStartingMatch = true;

            if (Lobby != null && Lobby.InLobby && Lobby.IsOwner && steam != null && steam.IsReady)
            {
                SteamMatchmaking.SetLobbyJoinable(new CSteamID(Lobby.LobbyId), false);
            }

            PromoteToPersistentMatchSession();

            var sld = new SceneLoadData(arenaSceneName)
            {
                ReplaceScenes = ReplaceOption.All
            };
            session.Manager.SceneManager.LoadGlobalScenes(sld);
            return true;
        }

        public void LeaveAndDestroyForMenuReturn()
        {
            ActiveOnlineSessionRegistry.Unregister(this);
            IsInMatchScene = false;
            IsStartingMatch = false;
            ShutdownSession();
            if (steam != null)
            {
                steam.Shutdown();
            }
            Destroy(gameObject);
        }

        public void LeaveLobby()
        {
            if (tearingDown) return;
            tearingDown = true;
            try
            {
                IsStartingMatch = false;
                if (!IsInMatchScene)
                {
                    ActiveOnlineSessionRegistry.Unregister(this);
                }
                if (Lobby != null && Lobby.IsOwner)
                {
                    Lobby.PublishHostReady(false);
                    SteamMatchmaking.SetLobbyJoinable(new CSteamID(Lobby.LobbyId), false);
                }
                session.Stop();
                Lobby?.LeaveLobby();
                observedLobby = originalOwner = 0;
            }
            finally { tearingDown = false; }
        }

        private void EndWithError(string error)
        {
            LastError = error;
            LeaveLobby();
            Debug.LogWarning("[M0-A] " + error);
        }

        public void ShutdownSession()
        {
            LeaveLobby();
            if (Lobby == null) return;
            Lobby.JoinRequested -= JoinLobby;
            Lobby.Dispose();
            Lobby = null;
        }

        private void PromoteToPersistentMatchSession()
        {
            if (!promotedToDontDestroy)
            {
                if (transform.parent != null)
                {
                    transform.SetParent(null);
                }
                DontDestroyOnLoad(gameObject);
                promotedToDontDestroy = true;
            }

            IsInMatchScene = true;
            ActiveOnlineSessionRegistry.Register(this);
        }

        private void SubscribeSceneEvents()
        {
            if (sceneEventsSubscribed || session == null || session.Manager == null || !session.Manager.Initialized || session.Manager.SceneManager == null)
            {
                return;
            }

            session.Manager.SceneManager.OnLoadStart += HandleSceneLoadStart;
            session.Manager.SceneManager.OnLoadEnd += HandleSceneLoadEnd;
            sceneEventsSubscribed = true;
        }

        private void UnsubscribeSceneEvents()
        {
            if (!sceneEventsSubscribed || session == null || session.Manager == null || session.Manager.SceneManager == null)
            {
                sceneEventsSubscribed = false;
                return;
            }

            session.Manager.SceneManager.OnLoadStart -= HandleSceneLoadStart;
            session.Manager.SceneManager.OnLoadEnd -= HandleSceneLoadEnd;
            sceneEventsSubscribed = false;
        }

        private void HandleSceneLoadStart(SceneLoadStartEventArgs args)
        {
            SceneLookupData[] lookups = args.QueueData?.SceneLoadData?.SceneLookupDatas;
            if (lookups == null || lookups.Length == 0)
            {
                return;
            }

            for (int i = 0; i < lookups.Length; i++)
            {
                SceneLookupData lookup = lookups[i];
                string rawName = lookup != null && !string.IsNullOrWhiteSpace(lookup.FullNameOnly)
                    ? lookup.FullNameOnly
                    : lookup?.FullName;
                if (IsMatchSceneName(rawName))
                {
                    PromoteToPersistentMatchSession();
                    return;
                }
            }
        }

        private void HandleSceneLoadEnd(SceneLoadEndEventArgs args)
        {
            IsStartingMatch = false;
            string activeName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.Equals(activeName, "NetworkTest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(activeName, "MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                IsInMatchScene = false;
                ActiveOnlineSessionRegistry.Unregister(this);
            }
        }

        private static bool IsMatchSceneName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            return !rawName.EndsWith("NetworkTest", StringComparison.OrdinalIgnoreCase)
                && !rawName.EndsWith("NetworkTest.unity", StringComparison.OrdinalIgnoreCase)
                && !rawName.EndsWith("MainMenu", StringComparison.OrdinalIgnoreCase)
                && !rawName.EndsWith("MainMenu.unity", StringComparison.OrdinalIgnoreCase);
        }

        private void OnDestroy()
        {
            ActiveOnlineSessionRegistry.Unregister(this);
            UnsubscribeSceneEvents();
            ShutdownSession();
            if (steam == null) return;
            steam.Changed -= OnSteamChanged;
            steam.BeforeShutdown -= ShutdownSession;
        }
    }
}
