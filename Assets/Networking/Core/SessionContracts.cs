using System;
using System.Collections.Generic;

namespace PirateGame.Networking
{
    public enum SessionRole { None, Host, Client }
    public enum SessionState { Offline, Starting, Connecting, Connected, Stopping, Faulted }

    // Future gameplay consumes this contract; it never needs Steamworks types.
    public interface INetworkSession
    {
        SessionRole Role { get; }
        SessionState State { get; }
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsAuthenticated { get; }
        string LastError { get; }
        event Action Changed;
        bool StartHost(ulong localPlatformId);
        bool StartClient(ulong hostPlatformId);
        void Stop();
    }

    public readonly struct LobbyMember
    {
        public readonly ulong Id;
        public readonly string Name;
        public LobbyMember(ulong id, string name) { Id = id; Name = name; }
    }

    public static class SessionRules
    {
        public const int PlayerLimit = 2; // Includes the host's local client.
        public const string TransportProtocol = "fishnet-4.7.4-fishysteamworks-4.1.1-m0a1";

        public static bool IsCompatible(string game, string protocol, string transport,
            string expectedGame, int expectedProtocol)
        {
            return game == expectedGame && protocol == expectedProtocol.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && transport == TransportProtocol;
        }

        public static bool CanAdmit(ulong peer, ulong localHost, ulong lobbyOwner,
            IReadOnlyList<LobbyMember> members, int alreadyAccepted, bool duplicateIdentity)
        {
            if (peer == 0 || localHost == 0 || localHost != lobbyOwner || duplicateIdentity
                || alreadyAccepted >= PlayerLimit || members.Count > PlayerLimit)
                return false;
            for (int i = 0; i < members.Count; i++)
                if (members[i].Id == peer) return true;
            return false;
        }

        public static bool TryReadLobbyArgument(string[] args, out ulong lobbyId)
        {
            lobbyId = 0;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby")
                    return ulong.TryParse(args[i + 1], out lobbyId) && lobbyId != 0;
            return false;
        }
    }
}
