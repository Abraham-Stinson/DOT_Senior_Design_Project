using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using Game.Core;
using Game.Gameplay;
using Game.Input;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class LocalAirshipSpawnAndDockingTests
    {
        private GameObject _root;
        private FishNetSession _session;
        private NetworkManager _networkManager;
        private PlayerInputRouter _inputRouter;
        private SinglePrefabObjects _prefabs;

        [UnityTearDown]
        public IEnumerator TearDown()
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
        public IEnumerator InputRouterIsolatesDualChannelsAtRuntime()
        {
            var routerGo = new GameObject("RuntimeInputRouterTest");
            try
            {
                var router = routerGo.AddComponent<PlayerInputRouter>();
                router.EnableHardwarePolling = false;
                router.ConfigureMode(InputRoutingMode.LocalDualPlayer);

                yield return null;
                Assert.That(ActiveInputRouterRegistry.Current, Is.EqualTo(router));

                router.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = new Vector2(0.8f, 0f), DockPressed = true });
                router.InjectSyntheticInput(1, new PlayerInputSnapshot { Move = new Vector2(-0.6f, 0.2f), DockPressed = false });

                PlayerInputSnapshot s0 = router.GetChannelForSeat(0).ConsumeTickSnapshot();
                PlayerInputSnapshot s1 = router.GetChannelForSeat(1).ConsumeTickSnapshot();

                Assert.That(s0.Move.x, Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(s0.DockPressed, Is.True);
                Assert.That(s1.Move.x, Is.EqualTo(-0.6f).Within(0.001f));
                Assert.That(s1.DockPressed, Is.False);
            }
            finally
            {
                if (routerGo != null) Object.Destroy(routerGo);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalHostSpawnsAirshipsMovesDocksAndAutoUndocksWhenTargetDespawns()
        {
#if UNITY_EDITOR
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var padPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/DockableAnchorPad.prefab");
            if (airshipPrefabGo == null || padPrefabGo == null)
            {
                Assert.Ignore("Airship.prefab veya DockableAnchorPad.prefab henüz oluşturulmadı. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            var airshipPrefab = airshipPrefabGo.GetComponent<AirshipController>();
            var padPrefab = padPrefabGo.GetComponent<DockableAnchorNode>();
            var airshipNob = airshipPrefabGo.GetComponent<NetworkObject>();
            var padNob = padPrefabGo.GetComponent<NetworkObject>();
            Assert.That(airshipPrefab, Is.Not.Null);
            Assert.That(padPrefab, Is.Not.Null);
            Assert.That(airshipNob, Is.Not.Null);
            Assert.That(padNob, Is.Not.Null);
            Assert.That(
                airshipPrefabGo.GetComponents<AirshipDockingController>().Length,
                Is.EqualTo(1),
                "Airship.prefab üzerinde birden fazla AirshipDockingController var. Tools > Game > M1-1 > Create M1-1 Network Prefabs çalıştırın.");

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(airshipNob, true, false);
            _prefabs.AddObject(padNob, true, false);

            BuildLocalSessionRig(7780);
            yield return null;

            Assert.That(_session.StartLocalHost(), Is.True, _session.LastError);
            yield return Until(
                () => _session.IsAuthenticated && _networkManager.ClientManager.Connection != null && _networkManager.ClientManager.Connection.LoadedStartScenes(true),
                10f,
                "Local host did not finish authentication/start scenes in time.");

            Assert.That(_networkManager.ServerManager.Clients.TryGetValue(_networkManager.ClientManager.Connection.ClientId, out NetworkConnection hostConn), Is.True);

            var context = new MatchContext
            {
                Mode = MatchMode.LocalPvP,
                PlayableAirspace = new Rect(-18f, -2f, 36f, 16f),
                CanPilotAirships = () => true
            };

            DockableAnchorNode pad = Object.Instantiate(padPrefab, new Vector3(-10f, 0f, 0f), Quaternion.identity);
            Assert.That(pad.NetworkObject, Is.Not.Null);
            _networkManager.ServerManager.Spawn(pad.NetworkObject);
            pad.InitializeServer(TeamId.TeamA, context);

            AirshipController ship0 = Object.Instantiate(airshipPrefab, new Vector3(-10f, 1.8f, 0f), Quaternion.identity);
            Assert.That(ship0.NetworkObject, Is.Not.Null);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, context.PlayableAirspace);

            AirshipController ship1 = Object.Instantiate(airshipPrefab, new Vector3(10f, 4f, 0f), Quaternion.identity);
            Assert.That(ship1.NetworkObject, Is.Not.Null);
            _networkManager.ServerManager.Spawn(ship1.NetworkObject, hostConn);
            ship1.InitializeServer(1, TeamId.TeamB, context, context.PlayableAirspace);

            yield return null;

            // Inject synthetic movement to Seat 1 and dock toggle to Seat 0.
            _inputRouter.InjectSyntheticInput(1, new PlayerInputSnapshot { Move = new Vector2(1f, 0f), SelectedBuildSlot = -1 });
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = Vector2.zero, DockPressed = true, SelectedBuildSlot = -1 });

            yield return Until(() => ship0.Docking.IsDocked, 5f, "Seat 0 airship did not dock to nearby allied pad.");
            Assert.That(pad.IsOccupied, Is.True);
            yield return Until(
                () => ship1.transform.position.x > 10.05f,
                5f,
                "Seat 1 airship should move rightward from synthetic input.");

            // Despawning the dockable pad must automatically undock the airship on the next server tick.
            _networkManager.ServerManager.Despawn(pad.NetworkObject);
            yield return Until(() => !ship0.Docking.IsDocked, 5f, "Airship did not auto-undock when docked target despawned.");
#else
            Assert.Ignore("Prefab asset lookup requires Unity Editor.");
            yield break;
#endif
        }

        private void BuildLocalSessionRig(ushort port)
        {
#if UNITY_EDITOR
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                "LocalAirshipTestTemplate",
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
            template.AddComponent<PlayerInputRouter>();

            _root = Object.Instantiate(template);
            _root.name = "LocalAirshipTestRoot";
            Object.DestroyImmediate(template);

            var tugboat = _root.GetComponent<Tugboat>();
            SetPrivateField(tugboat, "_enableIpv6", false);
            tugboat.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            tugboat.SetServerBindAddress("::1", IPAddressType.IPv6);
            tugboat.SetMaximumClients(1);
            tugboat.SetClientAddress("127.0.0.1");
            tugboat.SetPort(port);

            var multipass = _root.GetComponent<Multipass>();
            multipass.GlobalServerActions = false;
            SetPrivateField(multipass, "_transports", new List<Transport> { tugboat });

            _root.GetComponent<TransportManager>().Transport = multipass;

            var serverManager = _root.GetComponent<ServerManager>();
            _root.GetComponent<TimeManager>().SetTickRate(30);
            var auth = _root.GetComponent<LobbyConnectionAuthenticator>();
            SetPrivateField(serverManager, "_authenticator", auth);

            _networkManager = _root.GetComponent<NetworkManager>();
            _networkManager.SpawnablePrefabs = _prefabs;
            SetPrivateField(_networkManager, "_dontDestroyOnLoad", false);
            SetPrivateField(_networkManager, "_runInBackground", true);

            _session = _root.GetComponent<FishNetSession>();
            SetPrivateField(_session, "manager", _networkManager);
            SetPrivateField(_session, "authenticator", auth);

            _inputRouter = _root.GetComponent<PlayerInputRouter>();
            _inputRouter.EnableHardwarePolling = false;
            _inputRouter.ConfigureMode(InputRoutingMode.LocalDualPlayer);

            _root.SetActive(true);
#endif
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
