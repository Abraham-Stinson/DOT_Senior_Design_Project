using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class LocalHostUnifiedSessionTests
    {
        private GameObject _root;
        private FishNetSession _session;
        private SinglePrefabObjects _prefabs;

        [UnitySetUp]
        public IEnumerator SetUpLocalSession()
        {
            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();

#if UNITY_EDITOR
            // Build on an off-scene hidden object so NetworkManager.Reset() does not log
            // an error before SpawnablePrefabs is assigned in PlayMode editor tests.
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                "LocalUnifiedSessionTestTemplate",
                HideFlags.HideAndDontSave);
            template.SetActive(false);

            template.AddComponent<Tugboat>();
            template.AddComponent<Multipass>();
            template.AddComponent<TransportManager>();
            template.AddComponent<ServerManager>();
            template.AddComponent<ClientManager>();
            template.AddComponent<TimeManager>();
            template.AddComponent<LobbyConnectionAuthenticator>();
            NetworkManager templateNm = template.AddComponent<NetworkManager>();
            templateNm.SpawnablePrefabs = _prefabs;
            template.AddComponent<FishNetSession>();

            _root = Object.Instantiate(template);
            _root.name = "LocalUnifiedSessionTestRoot";
            Object.DestroyImmediate(template);
#else
            _root = new GameObject("LocalUnifiedSessionTestRoot");
            _root.SetActive(false);
            _root.AddComponent<Tugboat>();
            _root.AddComponent<Multipass>();
            _root.AddComponent<TransportManager>();
            _root.AddComponent<ServerManager>();
            _root.AddComponent<ClientManager>();
            _root.AddComponent<TimeManager>();
            _root.AddComponent<LobbyConnectionAuthenticator>();
            NetworkManager runtimeNm = _root.AddComponent<NetworkManager>();
            runtimeNm.SpawnablePrefabs = _prefabs;
            _root.AddComponent<FishNetSession>();
#endif

            var tugboat = _root.GetComponent<Tugboat>();
            SetPrivateField(tugboat, "_enableIpv6", false);
            tugboat.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            tugboat.SetServerBindAddress("::1", IPAddressType.IPv6);
            tugboat.SetMaximumClients(1);
            tugboat.SetClientAddress("127.0.0.1");
            tugboat.SetPort(7779);

            var multipass = _root.GetComponent<Multipass>();
            multipass.GlobalServerActions = false;
            SetPrivateField(multipass, "_transports", new List<Transport> { tugboat });

            var transportManager = _root.GetComponent<TransportManager>();
            transportManager.Transport = multipass;

            var serverManager = _root.GetComponent<ServerManager>();
            var timeManager = _root.GetComponent<TimeManager>();
            timeManager.SetTickRate(30);

            var auth = _root.GetComponent<LobbyConnectionAuthenticator>();
            SetPrivateField(serverManager, "_authenticator", auth);

            var networkManager = _root.GetComponent<NetworkManager>();
            networkManager.SpawnablePrefabs = _prefabs;
            SetPrivateField(networkManager, "_dontDestroyOnLoad", false);
            SetPrivateField(networkManager, "_runInBackground", true);

            _session = _root.GetComponent<FishNetSession>();
            SetPrivateField(_session, "manager", networkManager);
            SetPrivateField(_session, "authenticator", auth);

            _root.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDownLocalSession()
        {
            if (_session != null)
            {
                _session.Stop();
                yield return null;
            }

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_prefabs != null)
            {
                Object.Destroy(_prefabs);
                _prefabs = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalHostStartsOfflineAuthenticatesAndRestartsCleanly()
        {
            for (int cycle = 0; cycle < 2; cycle++)
            {
                bool started = _session.StartLocalHost();
                Assert.That(started, Is.True, "StartLocalHost should succeed offline on cycle " + cycle + ": " + _session.LastError);

                yield return Until(
                    () => _session.IsAuthenticated || _session.State == SessionState.Faulted,
                    10f,
                    "Local loopback host authentication timed out on cycle " + cycle + ". LastError=" + _session.LastError);

                Assert.That(_session.State, Is.EqualTo(SessionState.Connected), _session.LastError);
                Assert.That(_session.Role, Is.EqualTo(SessionRole.Host));
                Assert.That(_session.TransportMode, Is.EqualTo(SessionTransportMode.LocalLoopback));
                Assert.That(_session.IsServer, Is.True);
                Assert.That(_session.IsClient, Is.True);
                Assert.That(_session.IsAuthenticated, Is.True);
                Assert.That(_session.ConnectedClients, Is.EqualTo(1));

                _session.Stop();
                yield return Until(
                    () => !_session.IsServer && !_session.IsClient,
                    5f,
                    "Local host teardown did not stop server/client cleanly on cycle " + cycle);

                Assert.That(_session.State, Is.EqualTo(SessionState.Offline));
                Assert.That(_session.TransportMode, Is.EqualTo(SessionTransportMode.None));
                Assert.That(_session.IsAuthenticated, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator StopOnIdleMultipassSessionProducesNoErrors()
        {
            _session.Stop();
            yield return null;

            Assert.That(_session.State, Is.EqualTo(SessionState.Offline));
            Assert.That(_session.TransportMode, Is.EqualTo(SessionTransportMode.None));
            Assert.That(_session.IsServer, Is.False);
            Assert.That(_session.IsClient, Is.False);
        }

        private static IEnumerator Until(Func<bool> condition, float timeoutSeconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, message);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
