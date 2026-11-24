using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;
using UnityEngine;

namespace PirateGame.Networking
{
    // Steam matchmaking membership is deliberately independent of FishNet connectivity.
    public sealed class SteamLobbyService : IDisposable
    {
        private const string GameKey = "game", ProtocolKey = "protocol", TransportKey = "transport";
        private const string HostKey = "host_steam_id", ReadyKey = "host_ready";
        private readonly SteamRuntime runtime;
        private readonly SteamNetworkSettings settings;
        private readonly List<LobbyMember> members = new();
        private readonly List<Operation> operations = new();
        private readonly Callback<GameLobbyJoinRequested_t> inviteCallback;
        private readonly Callback<LobbyChatUpdate_t> membersCallback;
        private readonly Callback<LobbyDataUpdate_t> dataCallback;
        private readonly Callback<PersonaStateChange_t> personaCallback;
        private Operation pending;
        private bool disposed;
        private float nextRefresh;

        private sealed class Operation : IDisposable
        {
            public float Deadline;
            public bool Abandoned;
            public CallResult<LobbyCreated_t> Create;
            public CallResult<LobbyEnter_t> Join;
            public void Dispose() { Create?.Dispose(); Join?.Dispose(); }
        }

        public ulong LobbyId { get; private set; }
        public ulong OwnerId { get; private set; }
        public ulong HostId { get; private set; }
        public bool HostReady { get; private set; }
        public bool InLobby => LobbyId != 0;
        public bool IsOwner => InLobby && OwnerId == runtime.UserId;
        public bool IsBusy => pending != null;
        public string Status { get; private set; } = "Lobby dışında.";
        public string LastError { get; private set; } = "";
        public IReadOnlyList<LobbyMember> Members => members;
        public event Action Changed;
        public event Action<ulong> JoinRequested;

        public SteamLobbyService(SteamRuntime runtime, SteamNetworkSettings settings)
        {
            this.runtime = runtime;
            this.settings = settings;
            inviteCallback = Callback<GameLobbyJoinRequested_t>.Create(value => JoinRequested?.Invoke(value.m_steamIDLobby.m_SteamID));
            membersCallback = Callback<LobbyChatUpdate_t>.Create(value => { if (value.m_ulSteamIDLobby == LobbyId) Refresh(); });
            dataCallback = Callback<LobbyDataUpdate_t>.Create(value => { if (value.m_ulSteamIDLobby == LobbyId && value.m_bSuccess != 0) Refresh(); });
            personaCallback = Callback<PersonaStateChange_t>.Create(value => { if (ContainsMember(value.m_ulSteamID)) Refresh(); });
        }

        public void CreateLobby()
        {
            if (!CanStartOperation()) return;
            var op = NewOperation("İki kişilik Steam lobby oluşturuluyor.");
            op.Create = CallResult<LobbyCreated_t>.Create((value, ioFailure) => OnCreated(op, value, ioFailure));
            op.Create.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, SessionRules.PlayerLimit));
        }

        public void JoinLobby(ulong id)
        {
            if (!new CSteamID(id).IsLobby()) { SetError("Geçerli bir Steam Lobby ID girin."); return; }
            if (id == LobbyId) return;
            if (!CanStartOperation()) return;
            var op = NewOperation("Steam lobby'ye katılınıyor; FishNet bağlantısı henüz kurulmadı.");
            op.Join = CallResult<LobbyEnter_t>.Create((value, ioFailure) => OnEntered(op, value, ioFailure));
            op.Join.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
        }

        private bool CanStartOperation()
        {
            if (disposed || !runtime.IsReady) { SetError("Önce Steam oturumu başlatılmalı."); return false; }
            if (IsBusy || InLobby) { SetError("Önce mevcut lobby'den veya bekleyen işlemden ayrılın."); return false; }
            if (operations.Count >= 4) { SetError("Steam'den yanıt bekleyen çok fazla işlem var. Steam bağlantısını kontrol edin."); return false; }
            return true;
        }

        private Operation NewOperation(string status)
        {
            LastError = "";
            Status = status;
            pending = new Operation { Deadline = Time.unscaledTime + settings.ConnectionTimeoutSeconds };
            operations.Add(pending);
            Changed?.Invoke();
            return pending;
        }

        private void OnCreated(Operation op, LobbyCreated_t result, bool ioFailure)
        {
            bool success = !ioFailure && result.m_eResult == EResult.k_EResultOK;
            bool abandoned = op.Abandoned || disposed || op != pending;
            Complete(op);
            if (abandoned)
            {
                if (success) SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                return;
            }
            if (!success) { SetError($"Lobby oluşturulamadı: {(ioFailure ? "Steam I/O hatası" : result.m_eResult.ToString())}."); return; }
            LobbyId = result.m_ulSteamIDLobby;
            var lobby = new CSteamID(LobbyId);
            bool metadata = SteamMatchmaking.SetLobbyData(lobby, GameKey, settings.GameKey)
                & SteamMatchmaking.SetLobbyData(lobby, ProtocolKey, settings.ProtocolVersion.ToString(CultureInfo.InvariantCulture))
                & SteamMatchmaking.SetLobbyData(lobby, TransportKey, SessionRules.TransportProtocol)
                & SteamMatchmaking.SetLobbyData(lobby, HostKey, runtime.UserId.ToString(CultureInfo.InvariantCulture))
                & SteamMatchmaking.SetLobbyData(lobby, ReadyKey, "0")
                & SteamMatchmaking.SetLobbyMemberLimit(lobby, SessionRules.PlayerLimit);
            if (!metadata) { LeaveLobby(); SetError("Lobby bağlantı bilgileri Steam'e yazılamadı."); return; }
            Refresh();
            Debug.Log("[M0-A] Lobby created; waiting for FishNet host.");
        }

        private void OnEntered(Operation op, LobbyEnter_t result, bool ioFailure)
        {
            bool success = !ioFailure && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            bool abandoned = op.Abandoned || disposed || op != pending;
            Complete(op);
            if (abandoned)
            {
                if (success) SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                return;
            }
            if (!success) { SetError($"Lobby'ye katılınamadı: {(ioFailure ? "Steam I/O hatası" : ((EChatRoomEnterResponse)result.m_EChatRoomEnterResponse).ToString())}."); return; }
            LobbyId = result.m_ulSteamIDLobby;
            if (!MetadataCompatible()) { LeaveLobby(); SetError("Lobby başka bir oyuna veya uyumsuz ağ sürümüne ait."); return; }
            Refresh();
            if (members.Count > SessionRules.PlayerLimit) { LeaveLobby(); SetError("Lobby iki oyuncu sınırını aşıyor."); }
            Debug.Log("[M0-A] Lobby entered; FishNet connectivity must be established separately.");
        }

        private void Complete(Operation op)
        {
            if (pending == op) pending = null;
            operations.Remove(op);
            op.Dispose();
        }

        public bool MetadataCompatible()
        {
            if (!InLobby || !runtime.IsReady) return false;
            var lobby = new CSteamID(LobbyId);
            return SessionRules.IsCompatible(SteamMatchmaking.GetLobbyData(lobby, GameKey),
                SteamMatchmaking.GetLobbyData(lobby, ProtocolKey), SteamMatchmaking.GetLobbyData(lobby, TransportKey),
                settings.GameKey, settings.ProtocolVersion);
        }

        public bool ContainsMember(ulong userId)
        {
            if (!InLobby || !runtime.IsReady) return false;
            var lobby = new CSteamID(LobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == userId) return true;
            return false;
        }

        public void Refresh()
        {
            if (!InLobby || !runtime.IsReady) return;
            var lobby = new CSteamID(LobbyId);
            OwnerId = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, HostKey), out ulong host);
            HostId = host;
            HostReady = SteamMatchmaking.GetLobbyData(lobby, ReadyKey) == "1";
            members.Clear();
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                members.Add(new LobbyMember(id.m_SteamID, SteamFriends.GetFriendPersonaName(id)));
            }
            Status = "Steam lobby içinde. FishNet durumu aşağıda ayrıca gösterilir.";
            Changed?.Invoke();
        }

        public bool PublishHostReady(bool ready)
        {
            if (!IsOwner || !runtime.IsReady) return false;
            bool success = SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId), ReadyKey, ready ? "1" : "0");
            if (success) HostReady = ready;
            else SetError("Host bağlantı durumu Steam lobby'ye yazılamadı.");
            Changed?.Invoke();
            return success;
        }

        public void InviteFriend()
        {
            if (!InLobby || !runtime.IsReady) { SetError("Davet göndermek için bir lobby'de olmalısınız."); return; }
            if (members.Count >= SessionRules.PlayerLimit) { SetError("Lobby dolu (2/2)."); return; }
            if (!SteamUtils.IsOverlayEnabled()) { SetError("Steam Overlay etkin değil. Standalone build'i Steam üzerinden açın ve Overlay ayarını kontrol edin."); return; }
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        }

        public void LeaveLobby()
        {
            if (pending != null) { pending.Abandoned = true; pending = null; }
            if (InLobby && runtime.IsReady) SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));
            LobbyId = OwnerId = HostId = 0;
            HostReady = false;
            members.Clear();
            Status = "Lobby dışında.";
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (pending != null && Time.unscaledTime >= pending.Deadline)
            {
                pending.Abandoned = true;
                pending = null;
                // Keep the CallResult alive: a late successful response must leave its lobby.
                SetError("Steam lobby işlemi zaman aşımına uğradı. İnternet/Steam bağlantısını kontrol edin.");
            }
            if (InLobby && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f;
                Refresh();
            }
        }

        private void SetError(string error) { LastError = error; Status = error; Changed?.Invoke(); Debug.LogWarning("[M0-A] " + error); }

        public void Dispose()
        {
            if (disposed) return;
            LeaveLobby();
            disposed = true;
            inviteCallback.Dispose(); membersCallback.Dispose(); dataCallback.Dispose(); personaCallback.Dispose();
            foreach (var op in operations) op.Dispose();
            operations.Clear();
        }
    }
}
