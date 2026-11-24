using System;
using Steamworks;
using UnityEngine;

namespace PirateGame.Networking
{
    [DisallowMultipleComponent]
    public sealed class OnlineSessionCoordinator : MonoBehaviour
    {
        [SerializeField] private SteamRuntime steam;
        [SerializeField] private FishNetSession session;
        private ulong observedLobby;
        private ulong originalOwner;
        private float lobbyDeadline;
        private bool tearingDown;
        private bool launchInviteHandled;
        public SteamRuntime Steam => steam;
        public FishNetSession Session => session;
        public SteamLobbyService Lobby { get; private set; }
        public string LastError { get; private set; } = "";

        private void Start()
        {
            steam.Changed += OnSteamChanged;
            steam.BeforeShutdown += ShutdownSession;
            OnSteamChanged();
        }

        private void OnSteamChanged()
        {
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

        public void LeaveLobby()
        {
            if (tearingDown) return;
            tearingDown = true;
            try
            {
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

        private void OnDestroy()
        {
            ShutdownSession();
            if (steam == null) return;
            steam.Changed -= OnSteamChanged;
            steam.BeforeShutdown -= ShutdownSession;
        }
    }
}
