using System.Reflection;
using FishNet.Transporting;
using NUnit.Framework;
using PirateGame.Networking.Editor;
using Steamworks;

namespace PirateGame.Networking.Tests
{
    public sealed class NetworkFoundationTests
    {
        private readonly LobbyMember[] members = { new(10, "Host"), new(20, "Client") };

        [TestCase(20ul, 10ul, 10ul, 1, false, true)]
        [TestCase(30ul, 10ul, 10ul, 1, false, false)]
        [TestCase(20ul, 10ul, 99ul, 1, false, false)]
        [TestCase(20ul, 10ul, 10ul, 2, false, false)]
        [TestCase(20ul, 10ul, 10ul, 1, true, false)]
        [TestCase(0ul, 10ul, 10ul, 0, false, false)]
        public void AdmissionRejectsUnknownIdentityOwnerChangeDuplicatesAndThirdPlayer(
            ulong peer, ulong host, ulong owner, int accepted, bool duplicate, bool expected)
        {
            Assert.That(SessionRules.CanAdmit(peer, host, owner, members, accepted, duplicate), Is.EqualTo(expected));
        }

        [TestCase("game", "1", true)]
        [TestCase("foreign-game", "1", false)]
        [TestCase("game", "2", false)]
        [TestCase("", "", false)]
        public void SharedApp480LobbiesMustHaveMatchingGameAndProtocol(string game, string protocol, bool expected)
        {
            Assert.That(SessionRules.IsCompatible(game, protocol, SessionRules.TransportProtocol, "game", 1), Is.EqualTo(expected));
            Assert.That(SessionRules.IsCompatible(game, protocol, "old-transport", "game", 1), Is.False);
        }

        [Test]
        public void ColdStartInviteRequiresExplicitValidArgument()
        {
            Assert.That(SessionRules.TryReadLobbyArgument(new[] { "game.exe", "+connect_lobby", "123" }, out ulong id), Is.True);
            Assert.That(id, Is.EqualTo(123ul));
            Assert.That(SessionRules.TryReadLobbyArgument(new[] { "+connect_lobby", "invalid" }, out _), Is.False);
            Assert.That(SessionRules.TryReadLobbyArgument(new[] { "+connect_lobby" }, out _), Is.False);
            Assert.That(SessionRules.TryReadLobbyArgument(new[] { "+connect_lobby", "0" }, out _), Is.False);
        }

        [Test]
        public void UnrelatedNativeClientCallbackCannotStartSocket()
        {
            var socket = new FishySteamworks.Client.ClientSocket();
            var callback = new SteamNetConnectionStatusChangedCallback_t
            {
                m_hConn = new HSteamNetConnection(42),
                m_info = new SteamNetConnectionInfo_t { m_eState = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected }
            };
            typeof(FishySteamworks.Client.ClientSocket).GetMethod("OnLocalConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(socket, new object[] { callback });
            var state = typeof(FishySteamworks.CommonSocket).GetMethod("GetLocalConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(socket, null);
            Assert.That(state, Is.EqualTo(LocalConnectionState.Stopped));
        }

        [Test]
        public void SerializedFoundationHasExplicitSteamTransportAndAuthenticator()
        {
            Assert.DoesNotThrow(NetworkFoundationEditor.Validate);
        }

        [TestCase(true, true, true, 2, 2, true)]
        [TestCase(false, true, true, 2, 2, false)]
        [TestCase(true, false, true, 2, 2, false)]
        [TestCase(true, true, false, 2, 2, false)]
        [TestCase(true, true, true, 1, 2, false)]
        [TestCase(true, true, true, 2, 1, false)]
        public void CanStartOnlineMatchRequiresHostLobbyAuthAndTwoConnectedClients(
            bool isHost, bool inLobby, bool isAuthenticated, int lobbyMembers, int connectedClients, bool expected)
        {
            Assert.That(
                SessionRules.CanStartOnlineMatch(isHost, inLobby, isAuthenticated, lobbyMembers, connectedClients),
                Is.EqualTo(expected));
        }
    }
}
