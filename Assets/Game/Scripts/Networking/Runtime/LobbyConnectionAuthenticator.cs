using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace PirateGame.Networking
{
    public enum AuthenticationMode { SteamLobby, LocalLoopbackOnly }

    // Identity comes from the authenticated Steam transport (or loopback in local mode), never from a client payload.
    public sealed class LobbyConnectionAuthenticator : Authenticator
    {
        public AuthenticationMode Mode { get; set; } = AuthenticationMode.SteamLobby;
        public Func<ulong, int, bool, bool> IsAllowedMember { get; set; }
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;
        private readonly Dictionary<int, ulong> accepted = new();
        private readonly Dictionary<NetworkConnection, float> pending = new();
        private float nextMembershipCheck;

        public override void InitializeOnce(NetworkManager manager)
        {
            if (Initialized) return;
            base.InitializeOnce(manager);
            manager.ServerManager.OnRemoteConnectionState += OnRemoteState;
            manager.ServerManager.OnServerConnectionState += OnServerState;
        }

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            pending[connection] = Time.unscaledTime + 3f; // Allow Steam membership propagation.
        }

        private void Update()
        {
            foreach (var entry in pending.ToArray())
            {
                var connection = entry.Key;
                if (!connection.IsActive) { pending.Remove(connection); continue; }
                string address = NetworkManager.TransportManager.Transport.GetConnectionAddress(connection.ClientId);
                if (Mode == AuthenticationMode.LocalLoopbackOnly)
                {
                    bool localValid = SessionRules.CanAdmitLocalLoopback(address, accepted.Count);
                    if (!localValid && string.IsNullOrEmpty(address) && Time.unscaledTime < entry.Value) continue;
                    pending.Remove(connection);
                    if (localValid) accepted[connection.ClientId] = 1ul;
                    else Debug.LogWarning("[M1-1] Local loopback connection rejected.");
                    OnAuthenticationResult?.Invoke(connection, localValid);
                    continue;
                }

                bool valid = ulong.TryParse(address, out ulong id) && id != 0
                    && accepted.Count < SessionRules.PlayerLimit && !accepted.ContainsValue(id)
                    && IsAllowedMember != null && IsAllowedMember(id, accepted.Count, accepted.ContainsValue(id));
                if (!valid && Time.unscaledTime < entry.Value) continue;
                pending.Remove(connection);
                if (valid) accepted[connection.ClientId] = id;
                else Debug.LogWarning("[M0-A] Connection rejected: lobby membership, identity or capacity check failed.");
                OnAuthenticationResult?.Invoke(connection, valid);
            }
            if (Mode != AuthenticationMode.SteamLobby || !Initialized || !NetworkManager.ServerManager.Started || Time.unscaledTime < nextMembershipCheck) return;
            nextMembershipCheck = Time.unscaledTime + 1f;
            foreach (var entry in accepted.ToArray())
            {
                // Revalidate existing members without counting the connection itself as
                // a new admission. A lobby departure must also revoke network access.
                bool allowed = IsAllowedMember != null
                    && IsAllowedMember(entry.Value, Math.Max(0, accepted.Count - 1), false);
                if (!allowed && NetworkManager.ServerManager.Clients.TryGetValue(entry.Key, out var connection))
                    connection.Disconnect(true);
            }
        }

        private void OnRemoteState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            accepted.Remove(args.ConnectionId);
            pending.Remove(connection);
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            accepted.Clear(); pending.Clear();
        }

        private void OnDestroy()
        {
            if (!Initialized || NetworkManager == null) return;
            NetworkManager.ServerManager.OnRemoteConnectionState -= OnRemoteState;
            NetworkManager.ServerManager.OnServerConnectionState -= OnServerState;
            accepted.Clear(); pending.Clear();
        }
    }
}
