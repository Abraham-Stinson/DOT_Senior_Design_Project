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
using Game.Data;
using Game.Gameplay;
using Game.Input;
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class LocalEconomyTerritoryAndBuildTests
    {
        private GameObject _root;
        private FishNetSession _session;
        private NetworkManager _networkManager;
        private SinglePrefabObjects _prefabs;
        private readonly List<ScriptableObject> _runtimeAssets = new();

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

            for (int i = 0; i < _runtimeAssets.Count; i++)
            {
                if (_runtimeAssets[i] != null)
                {
                    Object.Destroy(_runtimeAssets[i]);
                }
            }
            _runtimeAssets.Clear();

            yield return null;
        }

        [UnityTest]
        public IEnumerator EconomyTicksTerritoryExpandsAndBuildsStructuresFromFiveSlotDeck()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null
                || territoryPrefabGo == null || buildPrefabGo == null || structurePrefabGo == null || airshipPrefabGo == null)
            {
                Assert.Ignore("M1.4 prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject seatNob = seatPrefabGo.GetComponent<NetworkObject>();
            NetworkObject modeNob = modePrefabGo.GetComponent<NetworkObject>();
            NetworkObject economyNob = economyPrefabGo.GetComponent<NetworkObject>();
            NetworkObject territoryNob = territoryPrefabGo.GetComponent<NetworkObject>();
            NetworkObject buildNob = buildPrefabGo.GetComponent<NetworkObject>();
            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();
            NetworkObject airshipNob = airshipPrefabGo.GetComponent<NetworkObject>();

            Assert.That(seatNob, Is.Not.Null);
            Assert.That(modeNob, Is.Not.Null);
            Assert.That(economyNob, Is.Not.Null);
            Assert.That(territoryNob, Is.Not.Null);
            Assert.That(buildNob, Is.Not.Null);
            Assert.That(structureNob, Is.Not.Null);
            Assert.That(airshipNob, Is.Not.Null);

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatNob, true, false);
            _prefabs.AddObject(modeNob, true, false);
            _prefabs.AddObject(economyNob, true, false);
            _prefabs.AddObject(territoryNob, true, false);
            _prefabs.AddObject(buildNob, true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(airshipNob, true, false);

            BuildLocalSessionRig(7781);
            yield return null;

            Assert.That(_session.StartLocalHost(), Is.True, _session.LastError);
            yield return Until(
                () => _session.IsAuthenticated && _networkManager.ClientManager.Connection != null && _networkManager.ClientManager.Connection.LoadedStartScenes(true),
                10f,
                "Local host did not finish authentication/start scenes in time.");

            Assert.That(_networkManager.ServerManager.Clients.TryGetValue(_networkManager.ClientManager.Connection.ClientId, out NetworkConnection hostConn), Is.True);

            CardCatalogSO catalog = BuildRuntimeCatalog(structureNob);
            Rect bounds = new(-18f, -2f, 36f, 16f);

            var context = new MatchContext
            {
                Mode = MatchMode.LocalPvP,
                CardCatalog = catalog,
                Terrain = new ArenaGroundTerrainService(bounds),
                PlayableAirspace = bounds
            };

            MatchSeatFactory.SpawnSeatsServer(
                _networkManager,
                seatPrefabGo.GetComponent<PlayerSeatController>(),
                MatchMode.LocalPvP,
                hostConn,
                null,
                context,
                startingGold: 165);

            Assert.That(context.TryGetSeat(0, out PlayerSeatController seat0), Is.True);
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 20, 11, 12 });

            GameModeController gameMode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(gameMode.NetworkObject);
            gameMode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            gameMode.TransitionPhaseServer(MatchPhase.Playing);

            EconomyController economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, gameMode, baseIncomeOverride: 10f);

            TerritoryController territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f), initialRadius: 8.5f);

            StructureBuildController builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, gameMode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            AirshipController ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-6f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, bounds);

            BuildFeedback? lastFeedback = null;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;

            // 1. Passive income tick increases Seat 0 gold during Playing phase.
            yield return Until(() => seat0.Gold >= 168, 3f, "Economy tick did not increment Seat 0 gold.");
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(10f).Within(0.01f));

            // 2. Outside territory rejection before Territory Balloon expansion.
            Vector2 expandedPoint = new(-1.5f, -1.3f);
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.False);

            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 1, expandedPoint); // Slot 1 = Cannon
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback received for outside-territory request.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.OutsideTerritory));

            // 3. Card not in 5-slot loadout rejection.
            lastFeedback = null;
            builder.RequestBuildCard(0, 21, new Vector2(-9f, -1.3f)); // Card 21 (Heavy Mortar) not drafted
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback received for non-loadout card.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.CardNotInLoadout));

            // 4. Build Slot 0 (Territory Balloon) inside initial TeamA territory -> expands zone to cover expandedPoint.
            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 0, new Vector2(-6f, 0f));
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback received for Territory Balloon build.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(1));
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.True);
            var balloonStructure = context.Structures[0] as StructureBase;
            Assert.That(balloonStructure, Is.Not.Null);

            // Wait past test build cooldown (0.1s).
            yield return new WaitForSeconds(0.15f);

            // 5. Build Slot 2 (Gold Mine) in the newly expanded territory -> increases Seat 0 income by +8/sec.
            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 2, expandedPoint);
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback received for Gold Mine build.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(2));
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(18f).Within(0.01f));
            var mineStructure = context.Structures[1] as StructureBase;
            Assert.That(mineStructure, Is.Not.Null);

            // Wait past test build cooldown before testing insufficient gold.
            yield return new WaitForSeconds(0.15f);

            // 6. Insufficient gold rejection (Seat 0 spent 75 + 80 = 155, cannot afford 100g Cannon yet).
            Assert.That(seat0.Gold, Is.LessThan(100));
            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 1, new Vector2(-9f, -1.3f));
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback received for insufficient-gold check.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.InsufficientGold));

            // 7. Destroying/despawning Balloon shrinks territory back, despawning Gold Mine reverts income to 10/sec.
            _networkManager.ServerManager.Despawn(balloonStructure.NetworkObject);
            yield return Until(() => !territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), 3f, "Territory did not shrink after Balloon despawn.");

            _networkManager.ServerManager.Despawn(mineStructure.NetworkObject);
            yield return Until(() => Mathf.Abs(seat0.IncomePerSecond - 10f) < 0.01f, 3f, "Income rate did not revert after Gold Mine despawn.");
#else
            Assert.Ignore("Prefab asset lookup requires Unity Editor.");
            yield break;
#endif
        }

        private CardCatalogSO BuildRuntimeCatalog(NetworkObject structurePrefabNob)
        {
            StructureCardSO CreateCard(
                ushort id,
                string name,
                StructureCategory category,
                bool lockedZero,
                bool selectable,
                bool unlocked,
                int cost,
                float territoryRadius,
                float incomeBonus,
                bool requiresGround,
                Vector2 footprint)
            {
                var card = ScriptableObject.CreateInstance<StructureCardSO>();
                card.ConfigureForTest(id, name, category, lockedZero, selectable, unlocked, cost, 0);
                card.ConfigureGameplayStats(territoryRadius, incomeBonus, 0.1f, requiresGround, 120f, footprint, structurePrefabNob);
                _runtimeAssets.Add(card);
                return card;
            }

            var balloon = CreateCard(1, "Territory Balloon", StructureCategory.Territory, true, false, true, 75, 7.5f, 0f, false, new Vector2(1.6f, 1.6f));
            var cannon = CreateCard(10, "Cannon", StructureCategory.Offense, false, true, true, 100, 0f, 0f, true, new Vector2(1.8f, 1.4f));
            var shield = CreateCard(11, "Shield Generator", StructureCategory.Defense, false, true, true, 120, 0f, 0f, true, new Vector2(1.8f, 1.4f));
            var flak = CreateCard(12, "Flak Tower", StructureCategory.Offense, false, true, true, 110, 0f, 0f, true, new Vector2(1.8f, 1.4f));
            var mine = CreateCard(20, "Gold Mine", StructureCategory.Economy, false, true, true, 80, 0f, 8f, true, new Vector2(1.8f, 1.4f));
            var mortar = CreateCard(21, "Heavy Mortar", StructureCategory.Offense, false, true, true, 175, 0f, 0f, true, new Vector2(2f, 1.5f));

            var catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, mine, mortar });
            _runtimeAssets.Add(catalog);
            return catalog;
        }

        private void BuildLocalSessionRig(ushort port)
        {
#if UNITY_EDITOR
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                "LocalM14TestTemplate",
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
            _root.name = "LocalM14TestRoot";
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
