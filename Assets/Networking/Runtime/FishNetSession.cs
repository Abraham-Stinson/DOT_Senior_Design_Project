using System;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace PirateGame.Networking
{
    [DisallowMultipleComponent]
    public sealed class FishNetSession : MonoBehaviour, INetworkSession
    {
        [SerializeField] private NetworkManager manager;
        [SerializeField] private LobbyConnectionAuthenticator authenticator;
        private bool stopping;
        private bool localClientRequested;
        private bool needsStop;
        private float deadline;
        private float timeoutSeconds = 25f;
        private ulong localHostId;
        private LocalConnectionState serverState = LocalConnectionState.Stopped;
        private LocalConnectionState clientState = LocalConnectionState.Stopped;

        public SessionRole Role { get; private set; }
        public SessionState State { get; private set; }
        public string LastError { get; private set; } = "";
        public bool IsServer => manager != null && manager.ServerManager.Started;
        public bool IsClient => manager != null && manager.ClientManager.Started;
        public bool IsAuthenticated { get; private set; }
        public string ServerStatus => serverState.ToString();
        public string ClientStatus => clientState.ToString();
        public NetworkManager Manager => manager;
        public int ConnectedClients => IsServer ? manager.ServerManager.Clients.Count : (IsClient ? manager.ClientManager.Clients.Count : 0);
        public event Action Changed;

        private void Awake()
        {
            if (manager == null) manager = GetComponent<NetworkManager>();
            if (authenticator == null) authenticator = GetComponent<LobbyConnectionAuthenticator>();
            manager.ServerManager.OnServerConnectionState += OnServerState;
            manager.ClientManager.OnClientConnectionState += OnClientState;
            manager.ClientManager.OnAuthenticated += OnAuthenticated;
            manager.ClientManager.OnClientTimeOut += OnClientTimeout;
        }

        public void ConfigureAdmission(Func<ulong, int, bool, bool> membershipCheck, float timeout)
        {
            authenticator.IsAllowedMember = membershipCheck;
            timeoutSeconds = timeout;
        }

        private bool Begin(SessionRole role, ulong address)
        {
            if (needsStop) Stop();
            if (State != SessionState.Offline && State != SessionState.Faulted) return false;
            if (address == 0 || manager == null || !manager.Initialized
                || !(manager.TransportManager.Transport is FishySteamworks.FishySteamworks))
            { Fault("NetworkManager, Steam transport veya Steam kimliği geçersiz."); return false; }
            LastError = "";
            needsStop = stopping = localClientRequested = false;
            IsAuthenticated = false;
            Role = role;
            State = role == SessionRole.Host ? SessionState.Starting : SessionState.Connecting;
            deadline = Time.unscaledTime + timeoutSeconds;
            Changed?.Invoke();
            return true;
        }

        public bool StartHost(ulong localPlatformId)
        {
            if (!Begin(SessionRole.Host, localPlatformId)) return false;
            localHostId = localPlatformId;
            if (manager.ServerManager.StartConnection()) return true;
            Fault("Steam listen server başlatılamadı.");
            return false;
        }

        public bool StartClient(ulong hostPlatformId)
        {
            if (!Begin(SessionRole.Client, hostPlatformId)) return false;
            if (manager.ClientManager.StartConnection(hostPlatformId.ToString(System.Globalization.CultureInfo.InvariantCulture))) return true;
            Fault("Host'un Steam ID'sine bağlantı başlatılamadı.");
            return false;
        }

        private void Update()
        {
            if (needsStop) { Stop(); return; }
            if (Role == SessionRole.Host && State == SessionState.Starting
                && serverState == LocalConnectionState.Started && !localClientRequested)
            {
                localClientRequested = true;
                if (!manager.ClientManager.StartConnection(localHostId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    Fault("Host'un yerel client bağlantısı başlatılamadı.");
            }
            if ((State == SessionState.Starting || State == SessionState.Connecting) && Time.unscaledTime >= deadline)
                Fault("FishNet bağlantısı veya kimlik doğrulaması zaman aşımına uğradı. Host'u ve Steam bağlantısını kontrol edin.");
        }

        private void OnAuthenticated()
        {
            if (Role == SessionRole.None || stopping) return;
            IsAuthenticated = true;
            State = SessionState.Connected;
            Changed?.Invoke();
            Debug.Log("[M0-A] FishNet client authenticated (" + Role + ").");
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            serverState = args.ConnectionState;
            if (serverState == LocalConnectionState.Stopped && Role == SessionRole.Host && !stopping)
                Fault("Host server bağlantısı kapandı.");
            Changed?.Invoke();
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            clientState = args.ConnectionState;
            if (clientState == LocalConnectionState.Stopped)
            {
                IsAuthenticated = false;
                if (Role != SessionRole.None && !stopping) Fault("Host bağlantısı kesildi veya host bağlantıyı reddetti.");
            }
            Changed?.Invoke();
        }

        private void OnClientTimeout() { Fault("Host yanıt vermiyor; FishNet bağlantısı zaman aşımına uğradı."); }

        public void Fault(string error)
        {
            if (stopping) return;
            LastError = error;
            State = SessionState.Faulted;
            needsStop = true; // Dispose outside transport/callback iteration.
            Changed?.Invoke();
            Debug.LogWarning("[M0-A] " + error);
        }

        public void Stop()
        {
            if (stopping || manager == null) return;
            stopping = true;
            State = SessionState.Stopping;
            try
            {
                if (manager.Initialized)
                {
                    manager.ClientManager.StopConnection();
                    manager.ServerManager.StopConnection(true);
                    manager.TransportManager.Transport.Shutdown();
                }
            }
            finally
            {
                Role = SessionRole.None;
                IsAuthenticated = localClientRequested = needsStop = false;
                serverState = clientState = LocalConnectionState.Stopped;
                State = string.IsNullOrEmpty(LastError) ? SessionState.Offline : SessionState.Faulted;
                stopping = false;
                Changed?.Invoke();
            }
        }

        public void ClearFailure()
        {
            if (Role != SessionRole.None) return;
            if (needsStop) Stop();
            LastError = "";
            State = SessionState.Offline;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            Stop();
            if (manager == null || !manager.Initialized) return;
            manager.ServerManager.OnServerConnectionState -= OnServerState;
            manager.ClientManager.OnClientConnectionState -= OnClientState;
            manager.ClientManager.OnAuthenticated -= OnAuthenticated;
            manager.ClientManager.OnClientTimeOut -= OnClientTimeout;
        }
    }
}
