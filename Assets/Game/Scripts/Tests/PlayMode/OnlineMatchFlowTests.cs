using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class OnlineMatchFlowTests
    {
        private readonly List<Object> _cleanupObjects = new();
        private readonly List<NetworkManager> _activeManagers = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ActiveOnlineSessionRegistry.Clear();
            MatchLaunchConfig.Reset();

            for (int i = _activeManagers.Count - 1; i >= 0; i--)
            {
                NetworkManager nm = _activeManagers[i];
                if (nm == null)
                {
                    continue;
                }

                if (nm.IsClientStarted && nm.ClientManager != null)
                {
                    nm.ClientManager.StopConnection();
                }

                if (nm.IsServerStarted && nm.ServerManager != null)
                {
                    nm.ServerManager.StopConnection(true);
                }
            }
            _activeManagers.Clear();

            yield return null;

            for (int i = _cleanupObjects.Count - 1; i >= 0; i--)
            {
                if (_cleanupObjects[i] != null)
                {
                    Object.Destroy(_cleanupObjects[i]);
                }
            }
            _cleanupObjects.Clear();

            yield return null;
        }

        [UnityTest]
        public IEnumerator OnlineBridgeHandoffAvoidsDuplicateNetworkManagerAndTearsDownCleanly()
        {
            if (!Application.CanStreamedLevelBeLoaded(GameSceneNames.CannonBrawlArena))
            {
                Assert.Ignore("CannonBrawlArena Build Settings içinde bulunamadı.");
            }

            PrefabObjects spawnablePrefabs = null;
#if UNITY_EDITOR
            spawnablePrefabs = UnityEditor.AssetDatabase.LoadAssetAtPath<PrefabObjects>("Assets/DefaultPrefabObjects.asset");
#endif
            if (spawnablePrefabs == null)
            {
                var fallbackPrefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                _cleanupObjects.Add(fallbackPrefabs);
                spawnablePrefabs = fallbackPrefabs;
            }

            GameObject template = CreateOffSceneTemplate("FakePersistentOnlineRoot_Template");

            var templateTugboat = template.AddComponent<Tugboat>();
            var templateTm = template.AddComponent<TransportManager>();
            templateTm.Transport = templateTugboat;
            template.AddComponent<ServerManager>();
            template.AddComponent<ClientManager>();
            template.AddComponent<TimeManager>();
            NetworkManager templateNm = template.AddComponent<NetworkManager>();
            templateNm.SpawnablePrefabs = spawnablePrefabs;
            SetPrivateField(templateNm, "_dontDestroyOnLoad", false);
            SetPrivateField(templateNm, "_persistence", NetworkManager.PersistenceType.AllowMultiple);

            GameObject externalRoot = Object.Instantiate(template);
            externalRoot.name = "FakePersistentOnlineRoot";
            Object.DestroyImmediate(template);
            ClearHideFlags(externalRoot);
            Object.DontDestroyOnLoad(externalRoot);
            _cleanupObjects.Add(externalRoot);

            externalRoot.GetComponent<TransportManager>().Transport = externalRoot.GetComponent<Tugboat>();
            NetworkManager externalNm = externalRoot.GetComponent<NetworkManager>();
            externalNm.SpawnablePrefabs = spawnablePrefabs;
            SetPrivateField(externalNm, "_dontDestroyOnLoad", false);
            SetPrivateField(externalNm, "_persistence", NetworkManager.PersistenceType.AllowMultiple);
            externalRoot.SetActive(true);
            _activeManagers.Add(externalNm);

            Assert.That(externalNm.Initialized, Is.True, "FakePersistentOnlineRoot NetworkManager başlatılamadı.");

            var fakeSession = new FakeOnlineNetworkSession();
            var fakeBridge = new FakeOnlineSessionBridge(fakeSession, externalNm);
            ActiveOnlineSessionRegistry.Register(fakeBridge);

            Scene previousScene = SceneManager.GetActiveScene();
            yield return SceneManager.LoadSceneAsync(GameSceneNames.CannonBrawlArena, LoadSceneMode.Single);
            yield return null;

            ArenaSessionBootstrap bootstrap = Object.FindFirstObjectByType<ArenaSessionBootstrap>();
            Assert.That(bootstrap, Is.Not.Null, "CannonBrawlArena içinde ArenaSessionBootstrap bulunamadı.");

            if (bootstrap.GetComponent<NetworkManager>() != null)
            {
                Assert.Ignore("CannonBrawlArena sahnesini yeni split rig düzenine geçirmek için Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            Assert.That(bootstrap.ActiveMode, Is.EqualTo(MatchMode.OnlinePvP));
            Assert.That(bootstrap.IsUsingPersistentOnlineSession, Is.True);
            Assert.That(bootstrap.BoundNetworkManager, Is.SameAs(externalNm));

            NetworkManager[] activeManagers = Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(activeManagers.Length, Is.EqualTo(1), "Online PvP sırasında sahnede ikinci aktif NetworkManager olmamalıdır.");
            Assert.That(activeManagers[0], Is.SameAs(externalNm));

            Scene teardownScene = SceneManager.CreateScene("OnlineHandoffTeardown_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(teardownScene);
            bootstrap.ReturnToMainMenu();
            yield return null;

            Assert.That(fakeBridge.LeaveAndDestroyCalled, Is.True, "ReturnToMainMenu kalıcı online köprüyü temizlemelidir.");
            Assert.That(MatchLaunchConfig.HasExplicitSelection, Is.False);

            if (previousScene.IsValid() && previousScene.name != GameSceneNames.MainMenu)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator OnlineTwoPeerMatchSyncsSeatOwnershipDraftAirshipAndHqDestruction()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var draftPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/DraftLoadoutController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var depositPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GoldDeposit.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var projectilePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/BallisticProjectile.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || draftPrefabGo == null || economyPrefabGo == null
                || territoryPrefabGo == null || buildPrefabGo == null || structurePrefabGo == null
                || depositPrefabGo == null || airshipPrefabGo == null || projectilePrefabGo == null)
            {
                Assert.Ignore("M1 prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            SinglePrefabObjects prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _cleanupObjects.Add(prefabs);
            prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(draftPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(economyPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(territoryPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(buildPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(structurePrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(depositPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);
            prefabs.AddObject(projectilePrefabGo.GetComponent<NetworkObject>(), true, false);

            CardCatalogSO catalog = CreateOnlineTestCatalog(structurePrefabGo.GetComponent<NetworkObject>());

            const ushort testPort = 7788;
            PeerRig hostRig = BuildHostPeerRig(prefabs, testPort);
            PeerRig clientRig = BuildClientPeerRig(prefabs, testPort);
            yield return null;

            ArenaSessionBootstrap hostBootstrap = CreateTestBootstrap(
                "HostBootstrap",
                hostRig,
                catalog,
                seatPrefabGo,
                modePrefabGo,
                draftPrefabGo,
                economyPrefabGo,
                territoryPrefabGo,
                buildPrefabGo,
                structurePrefabGo,
                depositPrefabGo,
                airshipPrefabGo,
                projectilePrefabGo,
                startingGold: 300);

            ArenaSessionBootstrap clientBootstrap = CreateTestBootstrap(
                "ClientBootstrap",
                clientRig,
                catalog,
                seatPrefabGo,
                modePrefabGo,
                draftPrefabGo,
                economyPrefabGo,
                territoryPrefabGo,
                buildPrefabGo,
                structurePrefabGo,
                depositPrefabGo,
                airshipPrefabGo,
                projectilePrefabGo,
                startingGold: 300);

            yield return null;

            // Start Host (Server + LocalClient) and Remote Client over loopback Tugboat.
            hostRig.NetworkManager.ServerManager.StartConnection();
            hostRig.Session.MarkStarted(SessionRole.Host, isServer: true, isClient: false);
            yield return Until(() => hostRig.NetworkManager.IsServerStarted, 5f, "Host server did not start.");

            hostRig.NetworkManager.ClientManager.StartConnection("127.0.0.1");
            hostRig.Session.MarkStarted(SessionRole.Host, isServer: true, isClient: true);
            yield return Until(
                () => hostRig.NetworkManager.IsClientStarted
                      && hostRig.NetworkManager.ClientManager.Connection != null
                      && hostRig.NetworkManager.ClientManager.Connection.ClientId >= 0
                      && hostRig.NetworkManager.ClientManager.Connection.LoadedStartScenes(true),
                8f,
                "Host local client did not authenticate.");

            clientRig.NetworkManager.ClientManager.StartConnection("127.0.0.1");
            clientRig.Session.MarkStarted(SessionRole.Client, isServer: false, isClient: true);
            yield return Until(
                () => clientRig.NetworkManager.IsClientStarted
                      && clientRig.NetworkManager.ClientManager.Connection != null
                      && clientRig.NetworkManager.ClientManager.Connection.ClientId >= 0
                      && clientRig.NetworkManager.ClientManager.Connection.LoadedStartScenes(false)
                      && AreServerClientsAuthenticatedAndLoaded(hostRig.NetworkManager, expectedCount: 2),
                8f,
                "Remote client did not authenticate and finish start scenes.");

            hostRig.Session.MarkAuthenticated();
            clientRig.Session.MarkAuthenticated();

            yield return Until(
                () => hostBootstrap.IsMatchInitialized
                      && clientBootstrap.IsMatchInitialized
                      && hostBootstrap.SeatCount == 2
                      && clientBootstrap.SeatCount == 2
                      && hostBootstrap.Context.Airships.Count == 2
                      && clientBootstrap.Context.Airships.Count == 2
                      && clientBootstrap.TryGetHqHealth(TeamId.TeamA, out _, out _)
                      && clientBootstrap.TryGetHqHealth(TeamId.TeamB, out _, out _),
                10f,
                "Online PvP match did not replicate seats, airships, and HQs to both host and remote client.");

            Assert.That(hostBootstrap.TryGetLocalOwnedSeatId(out byte hostOwnedSeat), Is.True);
            Assert.That(hostOwnedSeat, Is.EqualTo(0));
            Assert.That(hostBootstrap.IsSeatLocallyOwned(0), Is.True);
            Assert.That(hostBootstrap.IsSeatLocallyOwned(1), Is.False);

            Assert.That(clientBootstrap.TryGetLocalOwnedSeatId(out byte clientOwnedSeat), Is.True);
            Assert.That(clientOwnedSeat, Is.EqualTo(1));
            Assert.That(clientBootstrap.IsSeatLocallyOwned(1), Is.True);
            Assert.That(clientBootstrap.IsSeatLocallyOwned(0), Is.False);

            // Both peers submit their 5-card deck loadouts in PreMatchDraft.
            ushort[] validDeck = { 1, 10, 11, 12, 20 };
            hostBootstrap.SubmitSeatLoadout(0, validDeck, true);
            clientBootstrap.SubmitSeatLoadout(1, validDeck, true);

            yield return Until(
                () => hostBootstrap.CurrentPhase == MatchPhase.Playing
                      && clientBootstrap.CurrentPhase == MatchPhase.Playing,
                6f,
                "Match did not transition to Playing on both peers after both seats readied up.");

            // Remote Client docks to TeamB Base (HQ), buys/carries Cannon (slot 1), and deploys it inside TeamB territory.
            Assert.That(clientBootstrap.Context.TryGetAirship(1, out AirshipController clientShip), Is.True);
            Assert.That(hostBootstrap.Context.TryGetAirship(1, out AirshipController serverShip1), Is.True);

            serverShip1.transform.position = new Vector3(11.5f, 0.2f, 0f);
            yield return null;

            clientBootstrap.RequestToggleDock(1);
            yield return Until(() => clientShip.Docking.IsDockedAtBase, 5f, "Remote client airship did not dock at TeamB Base.");

            // Select Slot 1 (Cannon) and confirm purchase & carry.
            clientBootstrap.RequestCycleDockedSelection(1, 1);
            yield return Until(() => clientShip.Docking.State.SelectedDeckSlot == 1, 4f, "Remote client did not cycle to Slot 1.");

            clientBootstrap.RequestConfirmDockedSelection(1);
            yield return Until(() => clientShip.IsCarrying && clientShip.CarryState.CardId == 10, 5f, "Remote client did not begin carrying Cannon.");

            // Move Seat 1 airship away from HQ_TeamB footprint inside TeamB territory before deploying Cannon.
            serverShip1.transform.position = new Vector3(9.2f, 0.2f, 0f);
            yield return Until(
                () => Mathf.Abs(clientShip.transform.position.x - 9.2f) < 0.35f,
                4f,
                "Remote client ship did not sync position away from TeamB HQ.");

            clientBootstrap.RequestDeployCarried(1);
            yield return Until(
                () => !clientShip.IsCarrying
                      && hostBootstrap.BuiltStructureCount >= 3
                      && clientBootstrap.BuiltStructureCount >= 3,
                5f,
                "Deployed structure from remote client did not replicate to both peers.");

            // Newly deployed Cannon (0.35s build duration) starts under construction and replicates completion to both peers.
            var clientCannon = clientBootstrap.Context.Structures[2] as StructureBase;
            Assert.That(clientCannon, Is.Not.Null);
            yield return Until(
                () => clientCannon.IsConstructed
                      && hostBootstrap.GetStructureCountForTeam(TeamId.TeamB) >= 2
                      && clientBootstrap.GetStructureCountForTeam(TeamId.TeamB) >= 2,
                5f,
                "Deployed structure did not complete construction across both Host and Remote Client.");

            // Destroying TeamA HQ on server ends the match cleanly for both Host and Remote Client.
            Assert.That(hostBootstrap.Context.TryGetHq(TeamId.TeamA, out IStructure serverHqA), Is.True);
            serverHqA.ApplyDamageServer(new DamageInfo(9999f, TeamId.TeamB, 1, Vector2.zero));

            yield return Until(
                () => hostBootstrap.CurrentPhase == MatchPhase.Ended
                      && clientBootstrap.CurrentPhase == MatchPhase.Ended
                      && hostBootstrap.WinnerTeam == TeamId.TeamB
                      && clientBootstrap.WinnerTeam == TeamId.TeamB,
                6f,
                "MatchEnded and WinnerTeam (TeamB) did not synchronize to both Host and Remote Client.");

            clientRig.NetworkManager.ClientManager.StopConnection();
            hostRig.NetworkManager.ClientManager.StopConnection();
            hostRig.NetworkManager.ServerManager.StopConnection(true);
            yield return null;
#else
            Assert.Ignore("Editor prefab paths required.");
            yield break;
#endif
        }

        private ArenaSessionBootstrap CreateTestBootstrap(
            string name,
            PeerRig rig,
            CardCatalogSO catalog,
            GameObject seatPrefabGo,
            GameObject modePrefabGo,
            GameObject draftPrefabGo,
            GameObject economyPrefabGo,
            GameObject territoryPrefabGo,
            GameObject buildPrefabGo,
            GameObject structurePrefabGo,
            GameObject depositPrefabGo,
            GameObject airshipPrefabGo,
            GameObject projectilePrefabGo,
            int startingGold)
        {
            var go = new GameObject(name);
            _cleanupObjects.Add(go);
            ArenaSessionBootstrap bootstrap = go.AddComponent<ArenaSessionBootstrap>();
            bootstrap.ConfigureAssetsForTest(
                catalog,
                seatPrefabGo.GetComponent<PlayerSeatController>(),
                modePrefabGo.GetComponent<GameModeController>(),
                draftPrefabGo.GetComponent<DraftLoadoutController>(),
                economyPrefabGo.GetComponent<EconomyController>(),
                territoryPrefabGo.GetComponent<TerritoryController>(),
                buildPrefabGo.GetComponent<StructureBuildController>(),
                structurePrefabGo.GetComponent<StructureBase>(),
                depositPrefabGo.GetComponent<GoldDepositNode>(),
                airshipPrefabGo.GetComponent<AirshipController>(),
                projectilePrefabGo.GetComponent<BallisticProjectile>(),
                startingGold);
            bootstrap.ConfigureExternalSessionForTest(rig.Session, rig.NetworkManager, MatchMode.OnlinePvP, deactivateLocalRig: true);
            return bootstrap;
        }

        private CardCatalogSO CreateOnlineTestCatalog(NetworkObject structurePrefabNob)
        {
            StructureCardSO CreateCard(ushort id, string title, StructureCategory category, bool lockedZero, bool selectable, int cost, float buildDuration = 0f)
            {
                var card = ScriptableObject.CreateInstance<StructureCardSO>();
                card.ConfigureForTest(id, title, category, lockedZero, selectable, true, cost, 0);
                card.ConfigureGameplayStats(
                    category == StructureCategory.Territory ? 7.5f : 0f,
                    category == StructureCategory.Economy ? 8f : 0f,
                    0.1f,
                    category != StructureCategory.Territory,
                    120f,
                    new Vector2(1.8f, 1.4f),
                    structurePrefabNob,
                    requiresGoldDeposit: category == StructureCategory.Economy,
                    buildDurationSeconds: buildDuration);
                _cleanupObjects.Add(card);
                return card;
            }

            var balloon = CreateCard(1, "Territory Balloon", StructureCategory.Territory, true, false, 75);
            var cannon = CreateCard(10, "Cannon", StructureCategory.Offense, false, true, 100, buildDuration: 0.35f);
            cannon.ConfigureWeaponStats(true, 45f, 10f, 24f, 19f);
            var shield = CreateCard(11, "Shield Generator", StructureCategory.Defense, false, true, 120);
            var flak = CreateCard(12, "Flak Tower", StructureCategory.Offense, false, true, 110);
            var mine = CreateCard(20, "Gold Mine", StructureCategory.Economy, false, true, 80);

            var catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, mine });
            _cleanupObjects.Add(catalog);
            return catalog;
        }

        private PeerRig BuildHostPeerRig(SinglePrefabObjects prefabs, ushort port)
        {
            return BuildPeerRigInternal("TestOnlineHostRoot", prefabs, port, isServer: true);
        }

        private PeerRig BuildClientPeerRig(SinglePrefabObjects prefabs, ushort port)
        {
            return BuildPeerRigInternal("TestOnlineClientRoot", prefabs, port, isServer: false);
        }

        private PeerRig BuildPeerRigInternal(string name, SinglePrefabObjects prefabs, ushort port, bool isServer)
        {
            GameObject template = CreateOffSceneTemplate(name + "_Template");

            var tugboat = template.AddComponent<Tugboat>();
            var tm = template.AddComponent<TransportManager>();
            tm.Transport = tugboat;
            var sm = template.AddComponent<ServerManager>();
            template.AddComponent<ClientManager>();
            var time = template.AddComponent<TimeManager>();
            time.SetTickRate(30);
            var auth = isServer ? template.AddComponent<TestDualLoopbackAuthenticator>() : null;
            if (isServer)
            {
                SetPrivateField(sm, "_authenticator", auth);
            }

            var nm = template.AddComponent<NetworkManager>();
            nm.SpawnablePrefabs = prefabs;
            SetPrivateField(nm, "_dontDestroyOnLoad", false);
            SetPrivateField(nm, "_runInBackground", true);
            SetPrivateField(nm, "_persistence", NetworkManager.PersistenceType.AllowMultiple);

            GameObject root = Object.Instantiate(template);
            root.name = name;
            Object.DestroyImmediate(template);
            ClearHideFlags(root);
            _cleanupObjects.Add(root);

            var liveTugboat = root.GetComponent<Tugboat>();
            root.GetComponent<TransportManager>().Transport = liveTugboat;
            SetPrivateField(liveTugboat, "_enableIpv6", false);
            liveTugboat.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            liveTugboat.SetServerBindAddress("::1", IPAddressType.IPv6);
            liveTugboat.SetMaximumClients(isServer ? 2 : 1);
            liveTugboat.SetClientAddress("127.0.0.1");
            liveTugboat.SetPort(port);

            if (isServer)
            {
                SetPrivateField(root.GetComponent<ServerManager>(), "_authenticator", root.GetComponent<TestDualLoopbackAuthenticator>());
            }

            var liveNm = root.GetComponent<NetworkManager>();
            liveNm.SpawnablePrefabs = prefabs;
            SetPrivateField(liveNm, "_dontDestroyOnLoad", false);
            SetPrivateField(liveNm, "_runInBackground", true);
            SetPrivateField(liveNm, "_persistence", NetworkManager.PersistenceType.AllowMultiple);

            root.SetActive(true);
            _activeManagers.Add(liveNm);
            return new PeerRig
            {
                Root = root,
                NetworkManager = liveNm,
                Tugboat = liveTugboat,
                Session = new TestPeerNetworkSession(liveNm)
            };
        }

        private static GameObject CreateOffSceneTemplate(string name)
        {
#if UNITY_EDITOR
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                name,
                HideFlags.HideAndDontSave);
#else
            var template = new GameObject(name);
#endif
            template.SetActive(false);
            return template;
        }

        private static void ClearHideFlags(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            root.hideFlags = HideFlags.None;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    components[i].hideFlags = HideFlags.None;
                }
            }
        }

        private static bool AreServerClientsAuthenticatedAndLoaded(NetworkManager serverNm, int expectedCount)
        {
            if (serverNm == null || !serverNm.IsServerStarted || serverNm.ServerManager == null)
            {
                return false;
            }

            int readyCount = 0;
            foreach (NetworkConnection conn in serverNm.ServerManager.Clients.Values)
            {
                if (conn != null && conn.IsActive && conn.IsAuthenticated && conn.LoadedStartScenes(true))
                {
                    readyCount++;
                }
            }

            return readyCount >= expectedCount;
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

        private sealed class PeerRig
        {
            public GameObject Root;
            public NetworkManager NetworkManager;
            public Tugboat Tugboat;
            public TestPeerNetworkSession Session;
        }

        private sealed class TestDualLoopbackAuthenticator : Authenticator
        {
            public override event Action<NetworkConnection, bool> OnAuthenticationResult;

            public override void OnRemoteConnection(NetworkConnection connection)
            {
                OnAuthenticationResult?.Invoke(connection, true);
            }
        }

        private sealed class TestPeerNetworkSession : INetworkSession
        {
            private readonly NetworkManager _manager;

            public TestPeerNetworkSession(NetworkManager manager)
            {
                _manager = manager;
            }

            public SessionRole Role { get; private set; }
            public SessionState State { get; private set; } = SessionState.Offline;
            public SessionTransportMode TransportMode => SessionTransportMode.LocalLoopback;
            public bool IsServer { get; private set; }
            public bool IsClient { get; private set; }
            public bool IsAuthenticated { get; private set; }
            public bool HasAuthenticatedRemotePeer => false;
            public string LastError => string.Empty;
            public event Action Changed;

            public void MarkStarted(SessionRole role, bool isServer, bool isClient)
            {
                Role = role;
                IsServer = isServer || (_manager != null && _manager.IsServerStarted);
                IsClient = isClient || (_manager != null && _manager.IsClientStarted);
                State = SessionState.Connecting;
                Changed?.Invoke();
            }

            public void MarkAuthenticated()
            {
                IsServer = _manager != null && _manager.IsServerStarted;
                IsClient = _manager != null && _manager.IsClientStarted;
                IsAuthenticated = true;
                State = SessionState.Connected;
                Changed?.Invoke();
            }

            public bool StartHost(ulong localPlatformId) => false;
            public bool StartClient(ulong hostPlatformId) => false;
            public bool StartLocalHost() => false;

            public void Stop()
            {
                IsAuthenticated = false;
                State = SessionState.Offline;
                Changed?.Invoke();
            }
        }

        private sealed class FakeOnlineNetworkSession : INetworkSession
        {
            public SessionRole Role => SessionRole.Client;
            public SessionState State => SessionState.Connected;
            public SessionTransportMode TransportMode => SessionTransportMode.SteamOnline;
            public bool IsServer => false;
            public bool IsClient => true;
            public bool IsAuthenticated => true;
            public bool HasAuthenticatedRemotePeer => false;
            public string LastError => string.Empty;
            public event Action Changed { add { } remove { } }
            public bool StartHost(ulong localPlatformId) => false;
            public bool StartClient(ulong hostPlatformId) => false;
            public bool StartLocalHost() => false;
            public void Stop() { }
        }

        private sealed class FakeOnlineSessionBridge : IActiveOnlineSessionBridge
        {
            public FakeOnlineSessionBridge(INetworkSession session, NetworkManager manager)
            {
                Session = session;
                NetworkManagerObject = manager;
            }

            public INetworkSession Session { get; }
            public object NetworkManagerObject { get; }
            public bool IsActiveOnlineSession => true;
            public bool LeaveAndDestroyCalled { get; private set; }

            public void LeaveAndDestroyForMenuReturn()
            {
                LeaveAndDestroyCalled = true;
                ActiveOnlineSessionRegistry.Unregister(this);
            }
        }
    }
}
