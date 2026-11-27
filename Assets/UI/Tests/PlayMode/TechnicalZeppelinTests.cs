#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateGame.UI.Tests
{
    public sealed class TechnicalZeppelinTests
    {
        OnlineSessionCoordinator network;
        [UnityTest]
        public IEnumerator TechnicalHostStillSpawnsOneOwnedBlueZeppelin()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Gameplay/Scenes/NetworkZeppelinTest.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            network = Object.FindFirstObjectByType<OnlineSessionCoordinator>();
            Assert.IsNotNull(network);
            if (!network.Steam.IsReady) Assert.Ignore(network.Steam.LastError);
            network.CreateLobby();
            float deadline = Time.realtimeSinceStartup + 35;
            ZeppelinNetworkInput[] actors;
            do
            {
                yield return null;
                actors = Object.FindObjectsByType<ZeppelinNetworkInput>(FindObjectsSortMode.None);
            } while (!actors.Any(z => z.IsOwner) && Time.realtimeSinceStartup < deadline);
            Assert.IsTrue(network.Session.IsAuthenticated);
            Assert.AreEqual(1,actors.Length);
            Assert.IsTrue(actors[0].IsOwner && actors[0].IsServerInitialized);
            Assert.That(actors[0].name,Does.Contain("Blue"));
            Assert.AreEqual("NetworkZeppelinTest",actors[0].gameObject.scene.name);
        }
        [UnityTearDown]
        public IEnumerator Close()
        {
            if (network) { network.ShutdownSession(); network.Steam.Shutdown(); }
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }
    }
}
#endif