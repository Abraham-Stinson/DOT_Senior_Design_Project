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
    public sealed class LocalCombatAndDestructionTests
    {
        private GameObject _root;
        private FishNetSession _session;
        private NetworkManager _networkManager;
        private PlayerInputRouter _inputRouter;
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

            // Player docks Seat 0 airship into friendlyCannon -> enters StructureActionSelect (Use / Sell),
            // confirms Use -> WeaponManualControl, aims at enemyMine, and fires.
            ship0.transform.position = friendlyCannon.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(
                () => ship0.Docking.IsDocked
                      && ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect
                      && ship0.Docking.State.SelectedAction == DockedStructureAction.Use
                      && ship0.Docking.State.CanUseTarget,
                3f,
                "Seat 0 airship failed to dock into StructureActionSelect on friendly cannon.");

            ship0.Docking.RequestConfirmDockedSelection();
            yield return Until(
                () => ship0.Docking.SubMode == DockedInteractionSubMode.WeaponManualControl
                      && friendlyCannon.IsWeaponControlled,
                3f,
                "Confirming Use did not enter WeaponManualControl.");

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

            // Re-docking onto the cooling-down cannon opens StructureActionSelect with CanUseTarget == false (cannot enter WeaponManualControl).
            ship0.Docking.RequestToggleDock();
            yield return Until(
                () => ship0.Docking.IsDocked
                      && ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect
                      && !ship0.Docking.State.CanUseTarget,
                3f,
                "Re-docking onto cooling-down cannon should open StructureActionSelect with CanUseTarget=false.");

            ship0.Docking.RequestConfirmDockedSelection();
            yield return new WaitForSeconds(0.12f);
            Assert.That(ship0.Docking.SubMode, Is.EqualTo(DockedInteractionSubMode.StructureActionSelect), "Cooling-down cannon must not enter WeaponManualControl.");

            ship0.Docking.RequestToggleDock();
            yield return Until(() => !ship0.Docking.IsDocked, 2f, "Airship failed to undock from cooling-down cannon.");

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

            // Airships are invulnerable (not IDamageable, no death, no respawn).
            Assert.That(ship1 is IDamageable, Is.False, "Airships must not implement IDamageable.");

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

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-13.5f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            StructureBase hqA = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 99,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Core,
                pos: new Vector3(-13.5f, -0.9f, 0f),
                maxHealth: 500f,
                footprint: new Vector2(2.4f, 2.2f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            context.RegisterHq(TeamId.TeamA, hqA);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && territory.IsClientInitialized && economy.IsClientInitialized && hqA.IsClientInitialized,
                5f,
                "Ground proximity test objects did not finish client initialization.");

            BuildFeedback? lastFeedback = null;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;

            // 1. Dock at Base HQ to pick up Cannon (Slot 1), fly high at y = 7.5 (ground is y = -2.0 -> 9.5m above ground).
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to dock onto TeamA Base HQ.");

            builder.RequestBeginCarryFromSlot(0, 1);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not enter carry state for Cannon.");

            ship0.transform.position = new Vector3(-9.5f, 7.5f, 0f);
            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue, 3f, "No rejection feedback when deploying Cannon from high altitude.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.TooFarFromGround));
            Assert.That(ship0.IsCarrying, Is.True);

            // 2. Fly down close to ground (y = 0.0 -> 2.0m above ground <= MaxGroundPlacementDistance 3.25m) and deploy -> succeeds.
            ship0.transform.position = new Vector3(-9.5f, 0f, 0f);
            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Cannon deploy near ground did not complete.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(2));

            yield return new WaitForSeconds(0.15f);

            // 3. Re-dock at Base HQ to carry Territory Balloon (RequiresGroundSupport == false), fly high (y = 7.5) -> deploys cleanly.
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to dock onto TeamA Base HQ for Balloon.");

            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 0);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not enter carry state for Territory Balloon.");

            ship0.transform.position = new Vector3(-10f, 7.5f, 0f);
            lastFeedback = null;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Territory Balloon deploy at high altitude did not complete.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected Balloon reject: {lastFeedback.Value.RejectReason}");
            Assert.That(context.Structures.Count, Is.EqualTo(3));
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DockedBaseDeckCyclingAndNonBaseSellRefundsReleasesDepositShrinksTerritoryAndProtectsBase()
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

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null || territoryPrefabGo == null
                || buildPrefabGo == null || structurePrefabGo == null || depositPrefabGo == null || airshipPrefabGo == null)
            {
                Assert.Ignore("M1.5+ prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(economyPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(territoryPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(buildPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(depositPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7786);
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
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 20, 11, 12 });

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

            var deposit = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(-9.5f, -2f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(deposit.NetworkObject);
            deposit.InitializeServer(1, context);

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-13.5f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            StructureBase hqA = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 99,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Core,
                pos: new Vector3(-13.5f, -0.9f, 0f),
                maxHealth: 500f,
                footprint: new Vector2(2.4f, 2.2f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            context.RegisterHq(TeamId.TeamA, hqA);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && territory.IsClientInitialized
                      && economy.IsClientInitialized && deposit.IsClientInitialized && hqA.IsClientInitialized,
                5f,
                "Docked interaction test objects did not initialize in time.");

            // 1. Base HQ cannot be sold on server.
            Assert.That(builder.TrySellStructureServer(0, hqA, hostConn, out _), Is.False);
            Assert.That(hqA.IsAlive, Is.True);

            // 2. Dock to Base HQ -> Left/Right deck cycling with wrap-around -> Confirm buys & carries Cannon (slot 1, cost 100).
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(
                () => ship0.Docking.IsDockedAtBase
                      && ship0.Docking.SubMode == DockedInteractionSubMode.BaseDeckSelect
                      && ship0.Docking.State.SelectedDeckSlot == 0,
                3f,
                "Docking at Base HQ did not open BaseDeckSelect at slot 0.");

            ship0.Docking.RequestCycleDockedSelection(-1); // wraps 0 -> 4
            yield return Until(() => ship0.Docking.State.SelectedDeckSlot == 4, 2f, "Left cycle did not wrap 0 -> 4.");

            ship0.Docking.RequestCycleDockedSelection(1); // wraps 4 -> 0
            yield return Until(() => ship0.Docking.State.SelectedDeckSlot == 0, 2f, "Right cycle did not wrap 4 -> 0.");

            ship0.Docking.RequestCycleDockedSelection(1); // 0 -> 1 (Cannon)
            yield return Until(() => ship0.Docking.State.SelectedDeckSlot == 1 && ship0.Docking.State.TargetCardId == 10, 2f, "Right cycle did not highlight Cannon at slot 1.");

            ship0.Docking.RequestConfirmDockedSelection();
            yield return Until(() => ship0.IsCarrying && !ship0.Docking.IsDocked, 3f, "Confirming Cannon at Base did not enter carry and undock.");
            Assert.That(ship0.CarryState.CardId, Is.EqualTo(10));
            Assert.That(seat0.Gold, Is.EqualTo(400));

            ship0.transform.position = new Vector3(-6.5f, 0f, 0f);
            BuildFeedback? lastFeedback = null;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;
            builder.RequestDeployCarried(0, ship0.transform.position);
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Cannon did not deploy.");
            Assert.That(lastFeedback.Value.Accepted, Is.True);

            // 3. Dock onto Gold Mine (Economy) -> only Sell is offered (SupportsUseAction == false, no bogus Use);
            // cycling Left/Right keeps Sell selected, and confirming refunds gold, releases deposit, undocks, despawns.
            StructureBase goldMine = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 20,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Economy,
                pos: new Vector3(-9.5f, -1.3f, 0f),
                maxHealth: 110f,
                footprint: new Vector2(1.8f, 1.4f),
                territoryRadius: 0f,
                incomeBonus: 8f,
                context);
            deposit.OccupyServer(goldMine.NetworkObject.ObjectId);
            goldMine.BindGoldDepositServer(deposit);
            Assert.That(deposit.IsOccupied, Is.True);
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(8f).Within(0.01f));

            ship0.transform.position = goldMine.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(
                () => ship0.Docking.IsDocked
                      && ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect
                      && !ship0.Docking.State.SupportsUseAction
                      && ship0.Docking.State.SelectedAction == DockedStructureAction.Sell,
                3f,
                "Docking onto Gold Mine did not open sell-only StructureActionSelect.");

            ship0.Docking.RequestCycleDockedSelection(1);
            ship0.Docking.RequestCycleDockedSelection(-1);
            yield return null;
            Assert.That(ship0.Docking.State.SelectedAction, Is.EqualTo(DockedStructureAction.Sell), "Gold Mine must not switch to bogus Use action.");

            int goldBeforeSell = seat0.Gold;
            int soldRefund = 0;
            builder.StructureSold += (_, _, refunded) => soldRefund = refunded;

            ship0.Docking.RequestConfirmDockedSelection();
            yield return Until(
                () => !ship0.Docking.IsDocked && (goldMine == null || !goldMine.IsAlive) && soldRefund > 0,
                3f,
                "Confirming Sell on Gold Mine did not undock and despawn structure.");

            Assert.That(soldRefund, Is.EqualTo(40)); // 50% of 80g Gold Mine
            Assert.That(seat0.Gold, Is.EqualTo(goldBeforeSell + 40));
            Assert.That(deposit.IsOccupied, Is.False);
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(0f).Within(0.01f));

            // 4. Dock onto Territory Balloon -> Sell -> refunds gold, shrinks territory, undocks, despawns.
            StructureBase balloon = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 1,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Territory,
                pos: new Vector3(-6f, 3f, 0f),
                maxHealth: 120f,
                footprint: new Vector2(1.6f, 1.6f),
                territoryRadius: 7.5f,
                incomeBonus: 0f,
                context);
            Vector2 expandedPoint = new(-1.5f, -1.3f);
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.True);

            ship0.transform.position = balloon.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect, 3f, "Failed to dock onto Balloon.");
            ship0.Docking.RequestCycleDockedSelection(-1);
            yield return Until(() => ship0.Docking.State.SelectedAction == DockedStructureAction.Sell, 2f, "Left cycle did not select Sell on Balloon.");

            int goldBeforeBalloonSell = seat0.Gold;
            soldRefund = 0;
            ship0.Docking.RequestConfirmDockedSelection();
            yield return Until(
                () => !ship0.Docking.IsDocked && !territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint) && soldRefund > 0,
                3f,
                "Selling Territory Balloon did not shrink territory and undock.");

            Assert.That(soldRefund, Is.EqualTo(38)); // Round(75 * 0.5)
            Assert.That(seat0.Gold, Is.EqualTo(goldBeforeBalloonSell + 38));
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator StructureConstructionTakesDurationBlocksUseUntilCompleteAndActivatesServices()
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
                Assert.Ignore("Prefablar eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();
            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(economyPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(territoryPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(buildPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(depositPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(projectilePrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7789);
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
            CardCatalogSO catalog = CreateCombatCatalog(structureNob, buildDurationSeconds: 0.55f);

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

            Assert.That(context.TryGetSeat(0, out PlayerSeatController seat0), Is.True);
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 20, 11, 12 });

            var mode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(mode.NetworkObject);
            mode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            mode.TransitionPhaseServer(MatchPhase.Playing);
            context.CanPilotAirships = () => mode.Phase == MatchPhase.Playing;

            var economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, mode, 0f);

            var territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f), 8.5f);

            var builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, mode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            var deposit = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(-9.5f, -2f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(deposit.NetworkObject);
            deposit.InitializeServer(1, context);

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-7f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && territory.IsClientInitialized && economy.IsClientInitialized && deposit.IsClientInitialized,
                5f,
                "Construction test objects did not initialize.");

            // 1. Build Cannon (0.55s buildDuration) -> starts under construction, cannot enter WeaponManualControl until finished.
            BuildFeedback? lastFeedback = null;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;

            builder.RequestBuildFromSlot(0, 1, new Vector2(-7f, -1.3f));
            yield return Until(() => lastFeedback.HasValue && lastFeedback.Value.Accepted, 3f, "Cannon placement was rejected.");
            var cannon = context.Structures[0] as StructureBase;
            Assert.That(cannon, Is.Not.Null);
            Assert.That(cannon.IsUnderConstruction, Is.True);
            Assert.That(cannon.IsConstructed, Is.False);
            Assert.That(cannon.BuildProgress, Is.LessThan(1f));
            Assert.That(cannon.IsWeaponReadyForControl, Is.False);

            // Docking while under construction blocks Use -> remains in StructureActionSelect until construction finishes.
            ship0.transform.position = cannon.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDocked && ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect, 2f, "Airship failed to dock on building cannon.");
            Assert.That(ship0.Docking.State.CanUseTarget, Is.False);

            ship0.Docking.RequestConfirmDockedSelection();
            yield return null;
            Assert.That(ship0.Docking.SubMode, Is.EqualTo(DockedInteractionSubMode.StructureActionSelect), "Under-construction weapon must not allow Use.");

            yield return Until(() => cannon.IsConstructed && cannon.BuildProgress >= 0.99f && ship0.Docking.State.CanUseTarget, 3f, "Cannon did not complete construction and enable Use.");
            ship0.Docking.RequestConfirmDockedSelection();
            yield return Until(() => ship0.Docking.SubMode == DockedInteractionSubMode.WeaponManualControl, 2f, "Completed cannon did not enter WeaponManualControl.");
            ship0.Docking.RequestToggleDock();
            yield return Until(() => !ship0.Docking.IsDocked, 2f, "Airship failed to undock.");

            // 2. Build Territory Balloon (0.55s) -> territory must NOT expand until construction finishes.
            Vector2 expandedPoint = new(-1.5f, -1.3f);
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.False);
            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 0, new Vector2(-6f, 2f));
            yield return Until(() => lastFeedback.HasValue && lastFeedback.Value.Accepted, 3f, "Balloon placement failed.");
            var balloon = context.Structures[1] as StructureBase;
            Assert.That(balloon, Is.Not.Null);
            Assert.That(balloon.IsUnderConstruction, Is.True);
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.False, "Territory must not expand before Balloon construction completes.");

            yield return Until(() => balloon.IsConstructed && territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), 3f, "Territory did not expand once Balloon construction finished.");

            // 3. Build Gold Mine (0.55s) -> income stays 0 until completed, and selling mid-construction refunds cleanly & frees deposit.
            lastFeedback = null;
            builder.RequestBuildFromSlot(0, 2, new Vector2(-9.5f, -1.3f));
            yield return Until(() => lastFeedback.HasValue && lastFeedback.Value.Accepted, 3f, "Gold Mine placement failed.");
            var mine = context.Structures[2] as StructureBase;
            Assert.That(mine, Is.Not.Null);
            Assert.That(mine.IsUnderConstruction, Is.True);
            Assert.That(deposit.IsOccupied, Is.True);
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(0f).Within(0.01f), "Gold Mine must not grant income before construction completes.");

            yield return Until(() => mine.IsConstructed && Mathf.Abs(seat0.IncomePerSecond - 8f) < 0.01f, 3f, "Gold Mine did not grant +8/sec income after construction finished.");
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator AirshipIsInvulnerableToDirectAndSplashProjectileDamageAndNeverRespawns()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var projectilePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/BallisticProjectile.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || structurePrefabGo == null
                || airshipPrefabGo == null || projectilePrefabGo == null)
            {
                Assert.Ignore("Prefablar eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();
            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(projectilePrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7790);
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
                startingGold: 400);

            var mode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(mode.NetworkObject);
            mode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            mode.TransitionPhaseServer(MatchPhase.Playing);
            context.CanPilotAirships = () => mode.Phase == MatchPhase.Playing;

            var ship1 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(0f, 1f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship1.NetworkObject, hostConn);
            ship1.InitializeServer(1, TeamId.TeamB, context, arenaBounds);

            StructureBase enemyWall = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 11,
                seatId: 1,
                team: TeamId.TeamB,
                category: StructureCategory.Defense,
                pos: new Vector3(4f, 1f, 0f),
                maxHealth: 100f,
                footprint: new Vector2(1.8f, 2.2f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);

            yield return Until(() => ship1.IsClientInitialized && enemyWall.IsClientInitialized, 5f, "Objects did not initialize.");

            Assert.That(ship1 is IDamageable, Is.False);

            // Launch heavy splash projectile directly through TeamB airship toward TeamB structure behind it.
            BallisticProjectile projectile = Object.Instantiate(projectilePrefabGo.GetComponent<BallisticProjectile>(), new Vector3(-3f, 1f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(projectile.NetworkObject);
            projectile.InitializeServer(
                TeamId.TeamA,
                0,
                launchVelocity: new Vector2(20f, 0f),
                gravityScale: 0f,
                damage: 150f,
                splashRadius: 3f,
                context);

            yield return Until(() => enemyWall == null || !enemyWall.IsAlive, 4f, "Projectile should pass through invulnerable airship and destroy structure behind it.");

            // Airship remains fully operational and never respawns or freezes.
            float xBefore = ship1.transform.position.x;
            ship1.Movement.SubmitMoveInput(Vector2.right);
            yield return Until(() => ship1.transform.position.x > xBefore + 0.1f, 2f, "Invulnerable airship should continue moving normally after projectile pass-through.");
#else
            Assert.Ignore("Editor-only prefab path test.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ConfirmingUseWithHeldFireTransitionsToWeaponControlWithoutAccidentalFire()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var projectilePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/BallisticProjectile.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || structurePrefabGo == null
                || airshipPrefabGo == null || projectilePrefabGo == null)
            {
                Assert.Ignore("M1.5 prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structurePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(projectilePrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7787, includeInputRouter: true);
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
            CardCatalogSO catalog = CreateCombatCatalog(structurePrefabGo.GetComponent<NetworkObject>());

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
                startingGold: 400);

            var mode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(mode.NetworkObject);
            mode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            mode.TransitionPhaseServer(MatchPhase.Playing);
            context.CanPilotAirships = () => mode.Phase == MatchPhase.Playing;

            var cannon = SpawnStructureServer(
                structurePrefabGo.GetComponent<StructureBase>(),
                cardId: 10,
                seatId: 0,
                team: TeamId.TeamA,
                category: StructureCategory.Offense,
                pos: new Vector3(-6f, -1.3f, 0f),
                maxHealth: 140f,
                footprint: new Vector2(1.8f, 1.4f),
                territoryRadius: 0f,
                incomeBonus: 0f,
                context);
            cannon.InitializeWeaponServer(new WeaponConfig
            {
                CanFire = true,
                Damage = 45f,
                FireCooldownSeconds = 10f,
                Range = 24f,
                ProjectileSpeed = 19f,
                GravityScale = 1f
            }, projectilePrefabGo.GetComponent<BallisticProjectile>());

            var ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), cannon.DockAnchorWorld, Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, arenaBounds);

            yield return Until(() => cannon.IsClientInitialized && ship0.IsClientInitialized && mode.IsClientInitialized, 5f, "Objects did not initialize.");

            // 1. Dock onto cannon via synthetic input -> enters StructureActionSelect with Use ready.
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { DockPressed = true, SelectedBuildSlot = -1 });
            yield return Until(
                () => ship0.Docking.IsDocked && ship0.Docking.SubMode == DockedInteractionSubMode.StructureActionSelect,
                3f,
                "Airship did not dock onto cannon.");

            // 2. Confirm Use with Space (ConfirmPlacePressed + FirePressed + FireHeld) -> transitions to WeaponManualControl WITHOUT firing!
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot
            {
                ConfirmPlacePressed = true,
                FirePressed = true,
                FireHeld = true,
                SelectedBuildSlot = -1
            });
            yield return Until(
                () => ship0.Docking.SubMode == DockedInteractionSubMode.WeaponManualControl && cannon.IsWeaponControlled,
                3f,
                "Confirming Use did not transition to WeaponManualControl.");

            // 3. Keep holding fire for several frames/ticks -> must NOT accidentally fire!
            float holdUntil = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < holdUntil)
            {
                _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { FireHeld = true, SelectedBuildSlot = -1 });
                yield return null;
            }

            Assert.That(cannon.WeaponController.ShotsFiredServer, Is.EqualTo(0), "Confirming Use or continuing to hold button must NOT fire accidentally.");
            Assert.That(ship0.Docking.SubMode, Is.EqualTo(DockedInteractionSubMode.WeaponManualControl));

            // 4. Release button cleanly, then perform a fresh FirePressed -> fires once and exits control.
            uint releaseStartTick = _networkManager.TimeManager.Tick;
            float releaseDeadline = Time.realtimeSinceStartup + 2f;
            while (_networkManager.TimeManager.Tick < releaseStartTick + 3 && Time.realtimeSinceStartup < releaseDeadline)
            {
                _inputRouter.InjectSyntheticInput(0, PlayerInputSnapshot.Empty);
                yield return null;
            }

            Assert.That(cannon.WeaponController.ShotsFiredServer, Is.EqualTo(0));

            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot
            {
                FirePressed = true,
                FireHeld = true,
                ConfirmPlacePressed = true,
                SelectedBuildSlot = -1
            });

            yield return Until(
                () => cannon.WeaponController.ShotsFiredServer == 1 && !ship0.Docking.IsDocked,
                3f,
                "Fresh FirePressed after release did not fire and release cannon control.");
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

        private CardCatalogSO CreateCombatCatalog(NetworkObject structurePrefabNob, float buildDurationSeconds = 0f)
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
                    structurePrefabNob,
                    requiresGoldDeposit: category == StructureCategory.Economy,
                    buildDurationSeconds: buildDurationSeconds);
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

        private void BuildLocalSessionRig(ushort port, bool includeInputRouter = false)
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
            if (includeInputRouter)
            {
                template.AddComponent<PlayerInputRouter>();
            }

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

            if (includeInputRouter)
            {
                _inputRouter = _root.GetComponent<PlayerInputRouter>();
                _inputRouter.EnableHardwarePolling = false;
                _inputRouter.ConfigureMode(InputRoutingMode.LocalDualPlayer);
            }
            else
            {
                _inputRouter = null;
            }

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
