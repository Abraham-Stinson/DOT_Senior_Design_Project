using System.Globalization;
using PirateGame.Networking;
using System.Linq;
using UnityEngine;

namespace PirateGame.UI
{
    // Only this presentation adapter knows about the existing online session.
    public sealed class LobbyPresenter
    {
        readonly OnlineSessionCoordinator network;
        readonly PirateGame.Gameplay.BattleRoom room;
        string notice = "";
        float copiedUntil;
        public LobbyPresenter(OnlineSessionCoordinator network,PirateGame.Gameplay.BattleRoom room) { this.network = network; this.room = room; }
        public ulong LobbyId => network.Lobby?.LobbyId ?? 0;
        public bool Host => network.Lobby?.IsOwner == true;
        public void Create() { notice = ""; network.CreateLobby(); }
        public void Join(string raw)
        {
            notice = "";
            if (!ulong.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
            { notice = "Enter a numeric Steam lobby ID."; return; }
            network.JoinLobby(id);
        }
        public void Retry() { notice = ""; network.Steam.TryInitialize(); }
        public void Invite() { notice = ""; network.Lobby?.InviteFriend(); }
        public void Copy()
        {
            if (LobbyId == 0) return;
            GUIUtility.systemCopyBuffer = LobbyId.ToString(CultureInfo.InvariantCulture);
            notice = "Lobby ID copied.";
            copiedUntil = Time.unscaledTime + 2f;
        }
        public void Leave() { network.LeaveLobby(); notice = ""; }
        public void Lost() { notice = "Lobby connection ended. Create or join another lobby."; }

        public void Render(ScreenView view)
        {
            if (notice == "Lobby ID copied." && Time.unscaledTime >= copiedUntil) notice = "";
            var lobby = network.Lobby;
            var steam = network.Steam;
            bool online = steam.IsReady && lobby != null;
            string error = !string.IsNullOrEmpty(network.LastError) ? network.LastError : lobby?.LastError;
            if (!steam.IsReady) error = steam.LastError;
            string status = !string.IsNullOrEmpty(notice) ? notice : EnglishError(error);
            if (view.page == Page.Multiplayer)
            {
                view.Label("steamState", online ? "STEAM ONLINE" : "STEAM OFFLINE");
                view.Label("steamName", online ? steam.UserName : "Sign in to Steam to play with a friend.");
                bool idle = online && !lobby.IsBusy && !lobby.InLobby;
                view.Enable("create", idle);
                view.Enable("join", idle);
                view.Show("retry", !online);
                view.Label("error", lobby?.IsBusy == true ? "Waiting for Steam..." : status);
                return;
            }

            string friend = "";
            if (lobby != null)
                foreach (var member in lobby.Members) if (member.Id != steam.UserId) friend = string.IsNullOrWhiteSpace(member.Name) ? member.Id.ToString() : member.Name;
            view.Label("opponent", friend.Length == 0 ? "WAITING FOR FRIEND" : Host ? "OPPONENT / GUEST" : "OPPONENT / HOST");
            view.Label("friend", friend.Length == 0 ? "Invite a Steam friend" : friend);
            string peerState = "";
            if (!string.IsNullOrEmpty(friend))
            {
                bool connected = Host
                    ? network.Session.Manager.ServerManager.Clients.Values.Any(peer => !peer.IsLocalClient && peer.IsAuthenticated)
                    : network.Session.IsAuthenticated;
                peerState = connected ? "FishNet connected • " : "Connecting • ";
            }
            view.Label("opponentReady", friend.Length == 0 ? "" : peerState + ((Host ? room.View.RedReady : room.View.BlueReady) ? "Ready" : "Preparing"));
            view.Label("p0title", Host ? "YOU / HOST" : "YOU / GUEST");
            view.Label("p0subtitle", steam.UserName);
            view.Label("protocol", room.View.Map == null ? "Waiting for authenticated host" : "Map: First Voyage");
            string connection = network.Session.IsAuthenticated ? "Authenticated" : network.Session.State.ToString();
            view.Label("lobbyNotice", status.Length > 0 ? status : $"FishNet: {connection}  •  {lobby?.Members.Count ?? 0}/2");
            view.Enable("invite", online && LobbyId != 0 && (lobby?.Members.Count ?? 0) < 2);
            view.Enable("copy", LobbyId != 0);
            view.Enable("hostMap", Host);
            view.Enable("startMatch", room.CanStart);
            view.Enable("ready", network.Session.IsAuthenticated && room.View.Lobby == LobbyId && LobbyId != 0);
        }

        // The M0 technical panel and Unity Console retain the original native diagnostics.
        static string EnglishError(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";
            if (detail.Contains("Overlay")) return "Enable Steam Overlay and launch through Steam.";
            if (detail.Contains("App ID")) return "Check the Steam App ID and sign-in.";
            if (detail.Contains("Lobby ID")) return "Enter a valid Steam lobby ID.";
            if (detail.Contains("uyumsuz") || detail.Contains("uyuşmuyor")) return "Incompatible lobby or network version.";
            if (detail.Contains("zaman aşım")) return "Connection timed out. Try again.";
            if (detail.Contains("owner") || detail.Contains("üyeliğiniz")) return "The host or your lobby connection was lost.";
            if (detail.Contains("dolu") || detail.Contains("sınır")) return "This lobby is full.";
            if (detail.Contains("Steam açık") || detail.Contains("oturum")) return "Open Steam, sign in, then retry.";
            if (detail.Contains("katılınamadı")) return "Could not join. Check the lobby ID or invitation.";
            return "Steam connection failed. Check the Unity or player log.";
        }
    }
}