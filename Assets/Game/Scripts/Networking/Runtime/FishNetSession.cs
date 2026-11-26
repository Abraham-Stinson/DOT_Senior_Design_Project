using System;
using System.Globalization;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
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
        private bool usingMultipass;
        private int activeTransportIndex = -1;
        private LocalConnectionState serverState = LocalConnectionState.Stopped;
        private LocalConnectionState clientState = LocalConnectionState.Stopped;

        public SessionRole Role { get; private set; }
        public SessionState State { get; private set; }
        public SessionTransportMode TransportMode { get; private set; }
        public string LastError { get; private set; } = "";
        public bool IsServer => manager != null && manager.ServerManager.Started;
        public bool IsClient => manager != null && manager.ClientManager.Started;
        public bool IsAuthenticated { get; private set; }
        public bool HasAuthenticatedRemotePeer
        {
            get
            {
                if (manager == null || !manager.Initialized || !manager.ServerManager.Started)
                {
                    return false;
                }

                foreach (var peer in manager.ServerManager.Clients.Values)
                {
                    if (peer != null && !peer.IsLocalClient && peer.IsAuthenticated)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        public string ServerStatus => serverState.ToString();
        public string ClientStatus => clientState.ToString();
        public NetworkManager Manager => manager;
        public int ConnectedClients => IsServer ? manager.ServerManager.Clients.Count : (IsClient ? manager.ClientManager.Clients.Count : 0);
        public event Action Changed;

        private void Awake()
        {
            if (manager == null) manager = GetComponent<NetworkManager>();
            if (authenticator == null) authenticator = GetComponent<LobbyConnectionAuthenticator>();
            if (manager == null || !manager.Initialized) return;
            if (manager.TransportManager != null && manager.TransportManager.Transport is Multipass mp && mp.Transports.Count > 0)
                mp.SetClientTransport(0);
            manager.ServerManager.OnServerConnectionState += OnServerState;
            manager.ClientManager.OnClientConnectionState += OnClientState;
            manager.ClientManager.OnAuthenticated += OnAuthenticated;
            manager.ClientManager.OnClientTimeOut += OnClientTimeout;
        }

        public void ConfigureAdmission(Func<ulong, int, bool, bool> membershipCheck, float timeout)
        {
            if (authenticator != null) authenticator.IsAllowedMember = membershipCheck;
            timeoutSeconds = timeout;
        }

        private bool TryResolveTransport<T>(out Multipass multipass, out T targetTransport, out int targetIndex) where T : Transport
        {
            multipass = null;
            targetTransport = null;
            targetIndex = -1;
            if (manager == null || manager.TransportManager == null || manager.TransportManager.Transport == null)
                return false;

            Transport root = manager.TransportManager.Transport;
            if (root is Multipass mp)
            {
                multipass = mp;
                for (int i = 0; i < mp.Transports.Count; i++)
                {
                    if (mp.Transports[i] is T found)
                    {
                        targetTransport = found;
                        targetIndex = i;
                        return true;
                    }
                }
                return false;
            }

            if (root is T single)
            {
                targetTransport = single;
                targetIndex = 0;
                return true;
            }

            return false;
        }

        private bool BeginSession(SessionRole role, SessionTransportMode mode)
        {
            if (needsStop) Stop();
            if (State != SessionState.Offline && State != SessionState.Faulted) return false;
            if (manager == null || !manager.Initialized)
            { Fault("NetworkManager hazır değil."); return false; }

            LastError = "";
            needsStop = stopping = localClientRequested = false;
            IsAuthenticated = false;
            usingMultipass = false;
            activeTransportIndex = -1;
            Role = role;
            TransportMode = mode;
            State = role == SessionRole.Host ? SessionState.Starting : SessionState.Connecting;
            deadline = Time.unscaledTime + timeoutSeconds;
            Changed?.Invoke();
            return true;
        }

        public bool StartHost(ulong localPlatformId)
        {
            if (localPlatformId == 0)
            { Fault("Steam kimliği geçersiz."); return false; }
            if (!TryResolveTransport<FishySteamworks.FishySteamworks>(out Multipass mp, out _, out int steamIndex))
            { Fault("NetworkManager üzerinde FishySteamworks transport bulunamadı."); return false; }
            if (mp != null && mp.GlobalServerActions)
            { Fault("Multipass üzerinde GlobalServerActions kapalı olmalıdır."); return false; }
            if (!BeginSession(SessionRole.Host, SessionTransportMode.SteamOnline)) return false;

            if (authenticator != null) authenticator.Mode = AuthenticationMode.SteamLobby;
            localHostId = localPlatformId;
            usingMultipass = mp != null;
            activeTransportIndex = steamIndex;

            bool started;
            if (mp != null)
            {
                mp.SetClientTransport(steamIndex);
                started = mp.StartConnection(true, steamIndex);
            }
            else
            {
                started = manager.ServerManager.StartConnection();
            }

            if (started) return true;
            Fault("Steam listen server başlatılamadı.");
            return false;
        }

        public bool StartClient(ulong hostPlatformId)
        {
            if (hostPlatformId == 0)
            { Fault("Host Steam kimliği geçersiz."); return false; }
            if (!TryResolveTransport<FishySteamworks.FishySteamworks>(out Multipass mp, out _, out int steamIndex))
            { Fault("NetworkManager üzerinde FishySteamworks transport bulunamadı."); return false; }
            if (mp != null && mp.GlobalServerActions)
            { Fault("Multipass üzerinde GlobalServerActions kapalı olmalıdır."); return false; }
            if (!BeginSession(SessionRole.Client, SessionTransportMode.SteamOnline)) return false;

            if (authenticator != null) authenticator.Mode = AuthenticationMode.SteamLobby;
            usingMultipass = mp != null;
            activeTransportIndex = steamIndex;

            string hostAddress = hostPlatformId.ToString(CultureInfo.InvariantCulture);
            bool started;
            if (mp != null)
            {
                mp.SetClientTransport(steamIndex);
                mp.SetClientAddress(hostAddress, steamIndex);
                started = mp.StartConnection(false);
            }
            else
            {
                started = manager.ClientManager.StartConnection(hostAddress);
            }

            if (started) return true;
            Fault("Host'un Steam ID'sine bağlantı başlatılamadı.");
            return false;
        }

        public bool StartLocalHost()
        {
            if (!TryResolveTransport<Tugboat>(out Multipass mp, out Tugboat tugboat, out int localIndex))
            { Fault("Yerel mod için Tugboat loopback transport bulunamadı."); return false; }
            if (mp != null && mp.GlobalServerActions)
            { Fault("Multipass üzerinde GlobalServerActions kapalı olmalıdır."); return false; }
            if (!BeginSession(SessionRole.Host, SessionTransportMode.LocalLoopback)) return false;

            tugboat.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            tugboat.SetServerBindAddress("::1", IPAddressType.IPv6);
            tugboat.SetMaximumClients(1);
            tugboat.SetClientAddress("127.0.0.1");

            if (authenticator != null) authenticator.Mode = AuthenticationMode.LocalLoopbackOnly;
            usingMultipass = mp != null;
            activeTransportIndex = localIndex;

            bool started;
            if (mp != null)
            {
                mp.SetClientTransport(localIndex);
                started = mp.StartConnection(true, localIndex);
            }
            else
            {
                started = manager.ServerManager.StartConnection();
            }

            if (started) return true;
            Fault("Yerel host server başlatılamadı.");
            return false;
        }

        private void Update()
        {
            if (needsStop) { Stop(); return; }
            if (Role == SessionRole.Host && State == SessionState.Starting
                && serverState == LocalConnectionState.Started && !localClientRequested)
            {
                localClientRequested = true;
                string targetAddress = TransportMode == SessionTransportMode.LocalLoopback
                    ? "127.0.0.1"
                    : localHostId.ToString(CultureInfo.InvariantCulture);

                bool clientStarted;
                if (usingMultipass && manager.TransportManager.Transport is Multipass mp && activeTransportIndex >= 0)
                {
                    mp.SetClientTransport(activeTransportIndex);
                    mp.SetClientAddress(targetAddress, activeTransportIndex);
                    clientStarted = mp.StartConnection(false);
                }
                else
                {
                    clientStarted = manager.ClientManager.StartConnection(targetAddress);
                }

                if (!clientStarted)
                    Fault("Host'un yerel client bağlantısı başlatılamadı.");
            }
            if ((State == SessionState.Starting || State == SessionState.Connecting) && Time.unscaledTime >= deadline)
                Fault("FishNet bağlantısı veya kimlik doğrulaması zaman aşımına uğradı. Host'u ve bağlantıyı kontrol edin.");
        }

        private void OnAuthenticated()
        {
            if (Role == SessionRole.None || stopping) return;
            IsAuthenticated = true;
            State = SessionState.Connected;
            Changed?.Invoke();
            Debug.Log("[M1-1] FishNet client authenticated (" + Role + " / " + TransportMode + ").");
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (usingMultipass && activeTransportIndex >= 0 && args.TransportIndex != activeTransportIndex)
                return;
            serverState = args.ConnectionState;
            if (serverState == LocalConnectionState.Stopped && Role == SessionRole.Host && !stopping)
                Fault("Host server bağlantısı kapandı.");
            Changed?.Invoke();
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (usingMultipass && activeTransportIndex >= 0 && args.TransportIndex >= 0 && args.TransportIndex != activeTransportIndex)
                return;
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
            Debug.LogWarning("[M1-1] " + error);
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
                    Transport rootTransport = manager.TransportManager != null ? manager.TransportManager.Transport : null;
                    if (rootTransport is Multipass mp)
                    {
                        for (int i = 0; i < mp.Transports.Count; i++)
                        {
                            if (mp.GetConnectionState(false, i) != LocalConnectionState.Stopped)
                            {
                                mp.SetClientTransport(i);
                                mp.StopConnection(false);
                            }

                            if (mp.GetConnectionState(true, i) != LocalConnectionState.Stopped)
                            {
                                mp.StopServerConnection(true, i);
                            }
                        }

                        mp.Shutdown();
                    }
                    else if (rootTransport != null)
                    {
                        manager.ClientManager.StopConnection();
                        manager.ServerManager.StopConnection(true);
                        rootTransport.Shutdown();
                    }
                }
            }
            finally
            {
                Role = SessionRole.None;
                TransportMode = SessionTransportMode.None;
                usingMultipass = false;
                activeTransportIndex = -1;
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
