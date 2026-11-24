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
using NUnit.Framework;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class LocalCombatAndDestructionTests
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
        public IEnumerator LocalHostCombatFiresBallisticProjectilesAppliesDamageCleansUpAndEndsMatchOnHqDestruction()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var depositPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GoldDeposit.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var projectilePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/BallisticProjectile.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null || territoryPrefabGo == null
                || buildPrefabGo == null || structurePrefabGo == null || depositPrefabGo == null
                || airshipPrefabGo == null || projectilePrefabGo == null)
            {
                Assert.Ignore("M1.5 prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject seatNob = seatPrefabGo.GetComponent<NetworkObject>();
            NetworkObject modeNob = modePrefabGo.GetComponent<NetworkObject>();
            NetworkObject economyNob = economyPrefabGo.GetComponent<NetworkObject>();
            NetworkObject territoryNob = territoryPrefabGo.GetComponent<NetworkObject>();
            NetworkObject buildNob = buildPrefabGo.GetComponent<NetworkObject>();
            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();
            NetworkObject depositNob = depositPrefabGo.GetComponent<NetworkObject>();
            NetworkObject airshipNob = airshipPrefabGo.GetComponent<NetworkObject>();
            NetworkObject projectileNob = projectilePrefabGo.GetComponent<NetworkObject>();

            Assert.That(seatNob, Is.Not.Null);
            Assert.That(modeNob, Is.Not.Null);
            Assert.That(economyNob, Is.Not.Null);
            Assert.That(territoryNob, Is.Not.Null);
            Assert.That(buildNob, Is.Not.Null);
            Assert.That(structureNob, Is.Not.Null);
            Assert.That(depositNob, Is.Not.Null);
            Assert.That(airshipNob, Is.Not.Null);
            Assert.That(projectileNob, Is.Not.Null);

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatNob, true, false);
            _prefabs.AddObject(modeNob, true, false);
            _prefabs.AddObject(economyNob, true, false);
            _prefabs.AddObject(territoryNob, true, false);
            _prefabs.AddObject(buildNob, true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(depositNob, true, false);
            _prefabs.AddObject(airshipNob, true, false);
            _prefabs.AddObject(projectileNob, true, false);

            BuildLocalSessionRig(7784);
            yield return null;

            Assert.That(_session.StartLocalHost(), Is.True, _session.LastError);
            yield return Until(
                () => _session.IsAuthenticated
                      && _networkManager.ClientManager.Connection != null
                      && _networkManager.ClientManager.Connection.ClientId >= 0
                      && _networkManager.ClientManager.Connection.LoadedStartScenes(true),
                10f,
                "Local host did not finish authentication/start scenes in time.");

            Assert.That(
                _networkManager.ServerManager.Clients.TryGetValue(_networkManager.ClientManager.Connection.ClientId, out NetworkConnection hostConn),
                Is.True,
                "Authenticated local host connection was not found in ServerManager.Clients.");

            var arenaBounds = new Rect(-22f, -2f, 44f, 18f);
            CardCatalogSO catalog = CreateCombatCatalog(structureNob);

            var context = new MatchContext
            {
                Mode = MatchMode.LocalPvP,
                CardCatalog = catalog,
                Terrain = new ArenaGroundTerrainService(arenaBounds),
                ProjectilePrefab = projectilePrefabGo.GetComponent<BallisticProjectile>(),
                PlayableAirspace = arenaBounds
            };

            MatchSeatFactory.SpawnSeatsServer(
                _networkManager,
                seatPrefabGo.GetComponent<PlayerSeatController>(),
                MatchMode.LocalPvP,
                hostConn,
                null,
                context,
                startingGold: 600);

            var mode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(mode.NetworkObject);
            mode.InitializeMatchServer(MatchMode.LocalPvP, new HqDestructionMatchRule(), context);
            mode.TransitionPhaseServer(MatchPhase.Playing);
            context.CanPilotAirships = () => mode.Phase == MatchPhase.Playing;

            bool matchEndedEventFired = false;
            MatchOutcome observedOutcome = default;
            mode.MatchEnded += outcome =>
            {
                matchEndedEventFired = true;
                observedOutcome = outcome;
            };

            const float baseIncome = 10f;
            var economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, mode, baseIncome);

            var territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12f, -0.5f), new Vector2(12f, -0.5f), 8.5f);

            var builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, mode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            var deposit = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(8f, -1.25f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(deposit.NetworkObject);
            deposit.InitializeServer(1, context);

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-11f, 3.5f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            var ship1 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(12f, 3.5f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship1.NetworkObject, hostConn);
            ship1.InitializeServer(1, TeamId.TeamB, context, arenaBounds);

            StructureBase hqA = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 99,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Core,
                pos: new Vector3(-15f, -0.9f, 0f),
                maxHealth: 500f,
                footprint: new Vector2(2.4f, 2.2f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            context.RegisterHq(TeamId.TeamA, hqA);

            StructureBase hqB = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 99,
                seatId: 1,
                team: TeamId.TeamB,
                category: StructureCategory.Core,
                pos: new Vector3(15f, -0.9f, 0f),
                maxHealth: 200f,
                footprint: new Vector2(2.4f, 2.2f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            context.RegisterHq(TeamId.TeamB, hqB);

            yield return Until(
                () => context.TryGetSeat(0, out PlayerSeatController s0)
                      && context.TryGetSeat(1, out PlayerSeatController s1)
                      && s0.IsClientInitialized && s1.IsClientInitialized
                      && mode.IsClientInitialized && economy.IsClientInitialized
                      && territory.IsClientInitialized && builder.IsClientInitialized
                      && deposit.IsClientInitialized
                      && ship0.IsClientInitialized && ship1.IsClientInitialized
                      && hqA.IsClientInitialized && hqB.IsClientInitialized,
                5f,
                "Combat test objects did not finish client initialization.");

            // Assign TeamB loadout and build a Gold Mine on the deposit + Territory Balloon expanding east
            PlayerSeatController seat1 = context.Seats[1];
            seat1.ApplyValidatedLoadoutServer(new ushort[] { 1, 20, 10, 11, 12 });

            StructureBase enemyMine = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 20,
                seatId: 1,
                team: TeamId.TeamB,
                category: StructureCategory.Economy,
                pos: new Vector3(8f, -1.25f, 0f),
                maxHealth: 60f,
                footprint: new Vector2(1.8f, 1.4f),
                territoryRadius: 0f,
                incomeBonus: 8f,
                context);
            deposit.OccupyServer(enemyMine.NetworkObject.ObjectId);
            enemyMine.BindGoldDepositServer(deposit);

            StructureBase enemyBalloon = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 1,
                seatId: 1,
                team: TeamId.TeamB,
                category: StructureCategory.Territory,
                pos: new Vector3(16f, 2.5f, 0f),
                maxHealth: 80f,
                footprint: new Vector2(1.6f, 1.6f),
                territoryRadius: 7.5f,
                incomeBonus: 0f,
                context);

            Vector2 expandedPoint = new(22.5f, -0.5f);
            Assert.That(deposit.IsOccupied, Is.True);
            Assert.That(seat1.IncomePerSecond, Is.EqualTo(baseIncome + 8f).Within(0.1f));
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamB, expandedPoint), Is.True);

            // Dock ship1 onto the enemy balloon so destroying it verifies auto-undock
            ship1.transform.position = enemyBalloon.DockAnchorWorld;
            ship1.Docking.RequestToggleDock();
            yield return Until(() => ship1.Docking.IsDocked, 3f, "TeamB airship failed to dock onto balloon.");

            // Spawn an offensive TeamA Cannon at (-4, -1.3) and verify it does NOT auto-fire.
            StructureBase friendlyCannon = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 10,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Offense,
                pos: new Vector3(-4f, -1.3f, 0f),
                maxHealth: 140f,
                footprint: new Vector2(1.8f, 1.4f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            friendlyCannon.InitializeWeaponServer(new WeaponConfig
            {
                CanFire = true,
                Damage = 80f,
                FireCooldownSeconds = 10f,
                Range = 22f,
                ProjectileSpeed = 16f,
                GravityScale = 1f,
                SplashRadius = 0f,
                CanTargetStructures = true,
                CanTargetAirships = false,
                PreferHighArc = false,
                MinAimAngleDegrees = 5f,
                MaxAimAngleDegrees = 85f
            }, projectilePrefabGo.GetComponent<BallisticProjectile>());

            // Spawn a second TeamA Cannon to verify cooldowns are strictly per-structure.
            StructureBase secondCannon = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 10,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Offense,
                pos: new Vector3(-8f, -1.3f, 0f),
                maxHealth: 140f,
                footprint: new Vector2(1.8f, 1.4f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            secondCannon.InitializeWeaponServer(new WeaponConfig
            {
                CanFire = true,
                Damage = 80f,
                FireCooldownSeconds = 10f,
                Range = 22f,
                ProjectileSpeed = 16f,
                GravityScale = 1f
            }, projectilePrefabGo.GetComponent<BallisticProjectile>());

            yield return new WaitForSeconds(0.45f);
            Assert.That(friendlyCannon.WeaponController.ShotsFiredServer, Is.EqualTo(0), "Cannons must NOT auto-fire without player control.");

            // Player docks Seat 0 airship into friendlyCannon, takes manual control, aims at enemyMine, and fires.
            ship0.transform.position = friendlyCannon.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDocked, 3f, "Seat 0 airship failed to dock onto friendly cannon.");

            Vector2 muzzle = (Vector2)friendlyCannon.transform.position + new Vector2(0f, 0.5f);
            bool solved = Ballistics2D.TrySolveLaunchVelocity(
                muzzle,
                enemyMine.FootprintBounds.center,
                16f,
                Ballistics2D.DefaultGravity,
                preferHighArc: false,
                minAngleDegrees: 5f,
                maxAngleDegrees: 85f,
                out Vector2 launchVelocity,
                out _);
            Assert.That(solved, Is.True);

            ship0.Docking.RequestDockedFire(launchVelocity.normalized);

            yield return Until(
                () => friendlyCannon.WeaponController.ShotsFiredServer == 1 && !ship0.Docking.IsDocked,
                3f,
                "Manual fire should launch projectile and immediately release airship control.");

            // Fired cannon enters per-structure cooldown while secondCannon remains ready, and airship can move freely.
            Assert.That(friendlyCannon.WeaponController.IsOnCooldown, Is.True);
            Assert.That(friendlyCannon.WeaponController.CooldownRemaining, Is.GreaterThan(5f));
            Assert.That(secondCannon.WeaponController.IsOnCooldown, Is.False);

            // Attempting to re-enter the cooling-down cannon is rejected; moving away succeeds immediately.
            ship0.Docking.RequestToggleDock();
            yield return new WaitForSeconds(0.15f);
            Assert.That(ship0.Docking.IsDocked, Is.False, "Airship must not dock onto a cannon that is still cooling down.");

            float xBeforeMove = ship0.transform.position.x;
            ship0.Movement.SubmitMoveInput(Vector2.left);
            yield return Until(() => ship0.transform.position.x < xBeforeMove - 0.1f, 2f, "Airship should move freely while cannon cools down.");
            ship0.Movement.SubmitMoveInput(Vector2.zero);

            yield return Until(() => enemyMine == null || !enemyMine.IsAlive, 6f, "Manually fired ballistic projectile did not destroy enemy Gold Mine.");

            // Verify deposit released & income reverted
            Assert.That(deposit.IsOccupied, Is.False, "GoldDeposit should be released after Gold Mine destruction.");
            Assert.That(seat1.IncomePerSecond, Is.EqualTo(baseIncome).Within(0.1f), "Income should revert after Gold Mine destruction.");

            // Destroy enemy balloon -> verify territory shrinks AND docked airship auto-undocks
            enemyBalloon.ApplyDamageServer(new DamageInfo(500f, TeamId.TeamA, 0, enemyBalloon.transform.position));
            yield return Until(() => !ship1.Docking.IsDocked, 3f, "Airship should auto-undock when docked structure is destroyed.");
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamB, expandedPoint), Is.False, "Territory zone should shrink after Territory Balloon destruction.");

            // Damage enemy airship -> verify health SyncVar drops & downed state triggers at 0 HP
            float initialShipHp = ship1.CurrentHealth;
            ship1.ApplyDamageServer(new DamageInfo(40f, TeamId.TeamA, 0, ship1.transform.position));
            yield return null;
            Assert.That(ship1.CurrentHealth, Is.EqualTo(initialShipHp - 40f).Within(0.1f));

            ship1.ApplyDamageServer(new DamageInfo(200f, TeamId.TeamA, 0, ship1.transform.position));
            yield return Until(() => ship1.IsDowned && !ship1.IsAlive, 2f, "Airship should become downed at 0 HP.");

            // Destroy TeamB HQ -> verify MatchPhase.Ended & TeamA winner
            hqB.ApplyDamageServer(new DamageInfo(999f, TeamId.TeamA, 0, hqB.transform.position));
            yield return Until(
                () => mode.Phase == MatchPhase.Ended && matchEndedEventFired,
                3f,
                "Match should transition to Ended and broadcast MatchEnded when TeamB HQ is destroyed.");

            Assert.That(mode.WinnerTeam, Is.EqualTo(TeamId.TeamA));
            Assert.That(matchEndedEventFired, Is.True);
            Assert.That(observedOutcome.WinnerTeam, Is.EqualTo(TeamId.TeamA));
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator GroundPlacementRejectsHighAltitudeAndSucceedsNearGround()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null || territoryPrefabGo == null
                || buildPrefabGo == null || structurePrefabGo == null || airshipPrefabGo == null)
            {
                Assert.Ignore("M1.5 prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(economyPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(territoryPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(buildPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7785);
            yield return null;

            Assert.That(_session.StartLocalHost(), Is.True, _session.LastError);
            yield return Until(
                () => _session.IsAuthenticated
                      && _networkManager.ClientManager.Connection != null
                      && _networkManager.ClientManager.Connection.ClientId >= 0
                      && _networkManager.ClientManager.Connection.LoadedStartScenes(true),
                10f,
                "Local host did not finish authentication/start scenes in time.");

            Assert.That(_networkManager.ServerManager.Clients.TryGetValue(_networkManager.ClientManager.Connection.ClientId, out NetworkConnection hostConn), Is.True);

            var arenaBounds = new Rect(-18f, -2f, 36f, 16f);
            CardCatalogSO catalog = CreateCombatCatalog(structureNob);

            var context = new MatchContext
            {
                Mode = MatchMode.LocalPvP,
                CardCatalog = catalog,
                Terrain = new ArenaGroundTerrainService(arenaBounds),
                PlayableAirspace = arenaBounds
            };

            MatchSeatFactory.SpawnSeatsServer(
                _networkManager,
                seatPrefabGo.GetComponent<PlayerSeatController>(),
                MatchMode.LocalPvP,
                hostConn,
                null,
                context,
                startingGold: 500);

            Assert.That(context.TryGetSeat(0, out PlayerSeatController seat0), Is.True);
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 11, 12, 20 });

            var mode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(mode.NetworkObject);
            mode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            mode.TransitionPhaseServer(MatchPhase.Playing);

            var economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, mode, 0f);

            var territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f), 8.5f);

            var builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, mode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-11f, 7.5f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && territory.IsClientInitialized && economy.IsClientInitialized,
                5f,
                "Ground proximity test objects did not finish client initialization.");

            BuildFeedback? lastFeedback = null;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;

            // 1. Carry Cannon (ground-supported) while hovering high at y = 7.5 (ground is y = -2.0 -> 9.5m above ground).
            builder.RequestBeginCarryFromSlot(0, 1);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not enter carry state for Cannon.");

            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue, 3f, "No rejection feedback when deploying Cannon from high altitude.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.TooFarFromGround));
            Assert.That(ship0.IsCarrying, Is.True);

            // 2. Fly down close to ground (y = 0.0 -> 2.0m above ground <= MaxGroundPlacementDistance 3.25m) and deploy -> succeeds.
            ship0.transform.position = new Vector3(-11f, 0f, 0f);
            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Cannon deploy near ground did not complete.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(1));

            yield return new WaitForSeconds(0.15f);

            // 3. Territory Balloon (RequiresGroundSupport == false) deploys cleanly even at high altitude (y = 7.5).
            ship0.transform.position = new Vector3(-9f, 7.5f, 0f);
            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 0);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not enter carry state for Territory Balloon.");

            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Territory Balloon deploy at high altitude did not complete.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected Balloon reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(2));
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        private StructureBase SpawnStructureServer(
            StructureBase prefab,
            ushort cardId,
            byte seatId,
            TeamId team,
            StructureCategory category,
            Vector3 pos,
            float maxHealth,
            Vector2 footprint,
            float territoryRadius,
            float incomeBonus,
            MatchContext context)
        {
            StructureBase instance = Object.Instantiate(prefab, pos, Quaternion.identity);
            _networkManager.ServerManager.Spawn(instance.NetworkObject);
            instance.InitializeServer(
                cardId,
                seatId,
                team,
                category,
                maxHealth,
                footprint,
                territoryRadius,
                incomeBonus,
                context);
            return instance;
        }

        private CardCatalogSO CreateCombatCatalog(NetworkObject structurePrefabNob)
        {
            StructureCardSO CreateCard(
                ushort id,
                string name,
                StructureCategory category,
                bool lockedZero,
                bool selectable,
                int cost)
            {
                var card = ScriptableObject.CreateInstance<StructureCardSO>();
                card.ConfigureForTest(id, name, category, lockedZero, selectable, true, cost, 0);
                card.ConfigureGameplayStats(
                    category == StructureCategory.Territory ? 7.5f : 0f,
                    category == StructureCategory.Economy ? 8f : 0f,
                    0.1f,
                    category != StructureCategory.Territory,
                    120f,
                    new Vector2(1.8f, 1.4f),
                    structurePrefabNob);
                _runtimeAssets.Add(card);
                return card;
            }

            var balloon = CreateCard(1, "Territory Balloon", StructureCategory.Territory, true, false, 75);
            var cannon = CreateCard(10, "Cannon", StructureCategory.Offense, false, true, 100);
            cannon.ConfigureWeaponStats(true, 45f, 10f, 24f, 19f);
            var shield = CreateCard(11, "Shield Generator", StructureCategory.Defense, false, true, 120);
            var flak = CreateCard(12, "Flak Tower", StructureCategory.Offense, false, true, 110);
            var mine = CreateCard(20, "Gold Mine", StructureCategory.Economy, false, true, 80);

            var catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, mine });
            _runtimeAssets.Add(catalog);
            return catalog;
        }

        private void BuildLocalSessionRig(ushort port)
        {
#if UNITY_EDITOR
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                "LocalCombatTestTemplate",
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
            _root.name = "LocalCombatTestRoot";
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
