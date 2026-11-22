using UnityEngine;

namespace PirateGame.Networking
{
    // Disposable IMGUI diagnostics; no gameplay or final UI dependencies.
    public sealed class NetworkTestPanel : MonoBehaviour
    {
        [SerializeField] private OnlineSessionCoordinator coordinator;
        [SerializeField] private NetworkProbe probe;
        private string lobbyId = "";
        private string inputError = "";
        private Vector2 scroll;

        private void OnGUI()
        {
            if (coordinator == null) return;
            var steam = coordinator.Steam;
            var lobby = coordinator.Lobby;
            var session = coordinator.Session;
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(760, Screen.width - 32), Screen.height - 32), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Untitled Pirate Game | Network Test (M0-A)");
            GUILayout.Label("Teknik bağlantı testi · 2 oyuncu · Steam listen server");
            GUILayout.Space(8);
            GUILayout.Label("Steam: " + steam.Status);
            GUILayout.Label($"Kullanıcı: {steam.UserName} | Steam ID: {steam.UserId}");
            GUILayout.Label("App ID: " + (steam.Settings == null ? "Eksik" : steam.Settings.AppId.ToString()));
            if (steam.Settings != null && steam.Settings.AppId == 480)
                GUILayout.Label("480 / Spacewar yalnızca geliştirme testleri içindir.");
            if (!steam.IsReady && GUILayout.Button("Retry Steam")) steam.TryInitialize();

            GUILayout.Space(8);
            bool idle = steam.IsReady && lobby != null && !lobby.InLobby && !lobby.IsBusy;
            GUI.enabled = idle;
            if (GUILayout.Button("Create Lobby")) coordinator.CreateLobby();
            GUILayout.BeginHorizontal();
            lobbyId = GUILayout.TextField(lobbyId, 20);
            if (GUILayout.Button("Join Lobby", GUILayout.Width(130)))
            {
                if (ulong.TryParse(lobbyId, out ulong id) && id != 0) { inputError = ""; coordinator.JoinLobby(id); }
                else inputError = "Geçerli bir sayısal Steam Lobby ID girin.";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = steam.IsReady && lobby != null && lobby.InLobby && lobby.Members.Count < SessionRules.PlayerLimit;
            if (GUILayout.Button("Invite Friend (Steam Overlay)")) lobby.InviteFriend();
            GUI.enabled = lobby != null && (lobby.InLobby || lobby.IsBusy);
            if (GUILayout.Button("Leave Lobby")) coordinator.LeaveLobby();
            GUI.enabled = true;

            GUILayout.Label("Lobby: " + (lobby?.Status ?? "Steam bekleniyor."));
            if (lobby != null && lobby.InLobby)
            {
                GUILayout.Label($"Lobby ID: {lobby.LobbyId} | Üyeler: {lobby.Members.Count}/2");
                if (GUILayout.Button("Copy Lobby ID")) GUIUtility.systemCopyBuffer = lobby.LobbyId.ToString();
                GUILayout.Label($"Lobby owner: {lobby.OwnerId} | FishNet host: {lobby.HostId} | Host hazır: {lobby.HostReady}");
                foreach (var member in lobby.Members)
                    GUILayout.Label($"  {member.Name} ({member.Id})" + (member.Id == lobby.OwnerId ? " [Owner / Host]" : " [Client]"));
            }
            GUILayout.Space(8);
            GUILayout.Label($"Network Session: {session.State} | Rol: {session.Role}");
            GUILayout.Label($"Server: {session.ServerStatus} | Client: {session.ClientStatus} | Authenticated: {session.IsAuthenticated}");
            GUILayout.Label("Transport: FishySteamworks · ISteamNetworkingSockets · P2P (virtual port 0)");
            GUILayout.Label($"Probe gönderilen / ACK: {probe.Sent} / {probe.Replies} | Server alınan: {probe.ServerReceived} | Uzak client: {probe.RemoteServerReceived}");
            GUILayout.Label(probe.LastResult);
            if (session.Role == SessionRole.Host)
                GUILayout.Label("Host'un kendi ACK'sı yerel testtir. İki hesap doğrulaması için Uzak client sayacı artmalıdır.");
            GUI.enabled = session.IsAuthenticated;
            if (GUILayout.Button("Send Test Message")) probe.SendProbe();
            GUI.enabled = true;
            GUILayout.Space(8);
            Error(steam.LastError); Error(lobby?.LastError); Error(session.LastError); Error(coordinator.LastError); Error(inputError);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Error(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            GUI.color = new Color(1f, 0.65f, 0.5f);
            GUILayout.Label("Hata: " + value);
            GUI.color = Color.white;
        }
    }
}
