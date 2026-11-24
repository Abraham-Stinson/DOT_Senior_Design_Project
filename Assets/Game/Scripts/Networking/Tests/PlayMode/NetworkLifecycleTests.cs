using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateGame.Networking.Tests
{
    public sealed class NetworkLifecycleTests
    {
        private const string TestSceneName = "NetworkTest";
        private OnlineSessionCoordinator coordinator;

        [UnitySetUp]
        public IEnumerator OpenTestScene()
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(TestSceneName),
                Is.True,
                "NetworkTest scene must be enabled in active Build Settings / Build Profiles.");
            yield return SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
            yield return null;
            coordinator = UnityEngine.Object.FindFirstObjectByType<OnlineSessionCoordinator>();
            Assert.That(coordinator, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SceneRemainsOfflineUntilLobbyIsRequested()
        {
            yield return null;
            Assert.That(coordinator.Session.Role, Is.EqualTo(SessionRole.None));
            Assert.That(coordinator.Session.IsServer, Is.False);
            Assert.That(coordinator.Session.IsClient, Is.False);
            Assert.That(coordinator.Session.IsAuthenticated, Is.False);
            if (coordinator.Lobby != null) Assert.That(coordinator.Lobby.InLobby, Is.False);
            else Assert.That(coordinator.Steam.LastError, Is.Not.Empty, "Unavailable Steam must expose a useful error.");
        }

        [UnityTest]
        public IEnumerator SteamHostLocalProbeAndRepeatedLeaveCreate()
        {
            if (!coordinator.Steam.IsReady) Assert.Ignore("Native Steam smoke test unavailable: " + coordinator.Steam.LastError);
            var probe = coordinator.GetComponent<NetworkProbe>();
            for (int cycle = 0; cycle < 2; cycle++)
            {
                coordinator.CreateLobby();
                yield return Until(() => coordinator.Lobby.InLobby || !string.IsNullOrEmpty(coordinator.Lobby.LastError), 30, "Lobby creation timed out.");
                Assert.That(coordinator.Lobby.InLobby, Is.True, coordinator.Lobby.LastError);
                Assert.That(coordinator.Lobby.Members.Count, Is.EqualTo(1));
                Assert.That(coordinator.Lobby.OwnerId, Is.EqualTo(coordinator.Steam.UserId));
                yield return Until(() => coordinator.Session.IsAuthenticated || coordinator.Session.State == SessionState.Faulted, 30, "Host client authentication timed out.");
                Assert.That(coordinator.Session.State, Is.EqualTo(SessionState.Connected), coordinator.Session.LastError);
                Assert.That(coordinator.Session.IsServer && coordinator.Session.IsClient, Is.True);
                yield return Until(() => probe.Replies > 0, 10, "Host-local probe round trip failed.");
                Assert.That(probe.RemoteServerReceived, Is.Zero, "Host-local tests must not be counted as remote Steam peer tests.");
                coordinator.LeaveLobby();
                yield return Until(
                    () => !coordinator.Session.IsServer && !coordinator.Session.IsClient,
                    5,
                    "FishNet session did not stop after LeaveLobby.");
                Assert.That(coordinator.Lobby.InLobby, Is.False);
                Assert.That(coordinator.Session.IsServer || coordinator.Session.IsClient, Is.False);
                Assert.That(coordinator.Session.State, Is.EqualTo(SessionState.Offline));
            }
        }

        private static IEnumerator Until(Func<bool> condition, float timeout, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator CloseTestScene()
        {
            SteamRuntime steam = coordinator != null ? coordinator.Steam : null;
            if (coordinator != null) coordinator.ShutdownSession();
            if (steam != null) steam.Shutdown();
            if (steam != null) Assert.That(steam.IsReady, Is.False);
            coordinator = null;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == TestSceneName && activeScene.isLoaded)
            {
                Scene teardownScene = SceneManager.CreateScene("NetworkLifecycleTeardown_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(teardownScene);
                yield return SceneManager.UnloadSceneAsync(activeScene);
            }

            yield return null;
        }
    }
}
