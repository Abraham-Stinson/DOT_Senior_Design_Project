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
    public sealed class LocalCarryAndPlacementTests
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
        public IEnumerator AirshipCarriesPlacesOnGoldDepositCancelsWithRefundAndExpandsTerritory()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var depositPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GoldDeposit.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null || territoryPrefabGo == null
                || buildPrefabGo == null || structurePrefabGo == null || airshipPrefabGo == null || depositPrefabGo == null)
            {
                Assert.Ignore("Carry & Place prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            NetworkObject seatNob = seatPrefabGo.GetComponent<NetworkObject>();
            NetworkObject modeNob = modePrefabGo.GetComponent<NetworkObject>();
            NetworkObject economyNob = economyPrefabGo.GetComponent<NetworkObject>();
            NetworkObject territoryNob = territoryPrefabGo.GetComponent<NetworkObject>();
            NetworkObject buildNob = buildPrefabGo.GetComponent<NetworkObject>();
            NetworkObject structureNob = structurePrefabGo.GetComponent<NetworkObject>();
            NetworkObject airshipNob = airshipPrefabGo.GetComponent<NetworkObject>();
            NetworkObject depositNob = depositPrefabGo.GetComponent<NetworkObject>();

            Assert.That(seatNob, Is.Not.Null);
            Assert.That(modeNob, Is.Not.Null);
            Assert.That(economyNob, Is.Not.Null);
            Assert.That(territoryNob, Is.Not.Null);
            Assert.That(buildNob, Is.Not.Null);
            Assert.That(structureNob, Is.Not.Null);
            Assert.That(airshipNob, Is.Not.Null);
            Assert.That(depositNob, Is.Not.Null);

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatNob, true, false);
            _prefabs.AddObject(modeNob, true, false);
            _prefabs.AddObject(economyNob, true, false);
            _prefabs.AddObject(territoryNob, true, false);
            _prefabs.AddObject(buildNob, true, false);
            _prefabs.AddObject(structureNob, true, false);
            _prefabs.AddObject(airshipNob, true, false);
            _prefabs.AddObject(depositNob, true, false);

            BuildLocalSessionRig(7782);
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
                startingGold: 300);

            Assert.That(context.TryGetSeat(0, out PlayerSeatController seat0), Is.True);
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 20, 11, 12 });

            GameModeController gameMode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(gameMode.NetworkObject);
            gameMode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            gameMode.TransitionPhaseServer(MatchPhase.Playing);

            EconomyController economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, gameMode, baseIncomeOverride: 0f);

            TerritoryController territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f), initialRadius: 8.5f);

            StructureBuildController builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, gameMode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            GoldDepositNode deposit = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(-9.5f, -2.0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(deposit.NetworkObject);
            deposit.InitializeServer(1, context);

            AirshipController ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-13.5f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, bounds);

            StructureBase hqA = Object.Instantiate(structurePrefabGo.GetComponent<StructureBase>(), new Vector3(-13.5f, -0.9f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(hqA.NetworkObject);
            hqA.InitializeServer(99, 0, TeamId.TeamA, StructureCategory.Core, 500f, new Vector2(2.4f, 2.2f), 0f, 0f, context);
            context.RegisterHq(TeamId.TeamA, hqA);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && deposit.IsClientInitialized && territory.IsClientInitialized && hqA.IsClientInitialized,
                5f,
                "Spawned M1.4+ network objects did not initialize on local client in time.");

            BuildFeedback? lastFeedback = null;
            int cancelledRefund = -1;
            builder.BuildFeedbackReceived += fb => lastFeedback = fb;
            builder.CarryCancelled += (_, _, refunded) => cancelledRefund = refunded;

            // 0. Buying/carrying without docking at Base must reject with MustDockAtBase.
            builder.RequestBeginCarryFromSlot(0, 2);
            yield return Until(() => lastFeedback.HasValue, 3f, "No rejection feedback when trying to carry while not docked at Base.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.MustDockAtBase));
            Assert.That(ship0.IsCarrying, Is.False);

            // Dock at Base HQ to begin carrying Gold Mine (Slot 2, cost 80, requiresGoldDeposit = true).
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to dock onto TeamA Base HQ.");

            // 1. Begin carrying Gold Mine (Slot 2) from docked Base -> auto-undocks and enters carry.
            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 2);
            yield return Until(() => ship0.IsCarrying && !ship0.Docking.IsDocked && lastFeedback.HasValue, 3f, "Airship did not enter carry state for Gold Mine.");
            Assert.That(lastFeedback.Value.Accepted, Is.True);
            Assert.That(ship0.CarryState.CardId, Is.EqualTo(20));
            Assert.That(ship0.CarryState.ReservedGold, Is.EqualTo(80));
            Assert.That(seat0.Gold, Is.EqualTo(220));

            // 2. Trying to deploy Gold Mine away from any GoldDeposit must reject with RequiresGoldDeposit and keep carry active.
            lastFeedback = null;
            builder.RequestDeployCarried(0, new Vector2(-13.5f, 0f));
            yield return Until(() => lastFeedback.HasValue, 3f, "No rejection feedback when deploying Gold Mine off deposit.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.RequiresGoldDeposit));
            Assert.That(ship0.IsCarrying, Is.True);

            // 3. Cancelling carry returns reserved gold (220 -> 300) and clears carry state.
            cancelledRefund = -1;
            builder.RequestCancelCarry(0);
            yield return Until(() => !ship0.IsCarrying && cancelledRefund == 80, 3f, "Cancelling carry did not clear state and refund gold.");
            Assert.That(seat0.Gold, Is.EqualTo(300));

            // 4. Re-dock at Base, pick up Gold Mine and deploy over GoldDeposit (-9.5, -2.0) -> snaps onto deposit, marks occupied, grants +8/sec.
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to re-dock onto Base HQ.");

            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 2);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not re-pick up Gold Mine.");
            Assert.That(seat0.Gold, Is.EqualTo(220));

            lastFeedback = null;
            builder.RequestDeployCarried(0, new Vector2(-9.5f, 0f));
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Gold Mine deploy did not complete on GoldDeposit.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected reject: {lastFeedback.Value.RejectReason}");
            Assert.That(deposit.IsOccupied, Is.True);
            Assert.That(seat0.IncomePerSecond, Is.EqualTo(8f).Within(0.01f));
            Assert.That(context.Structures.Count, Is.EqualTo(2));
            var mineStructure = context.Structures[1] as StructureBase;
            Assert.That(mineStructure, Is.Not.Null);
            Assert.That(mineStructure.transform.position.x, Is.EqualTo(-9.5f).Within(0.05f));

            yield return new WaitForSeconds(0.15f);

            // 5. Carrying another Gold Mine from Base and deploying onto the occupied deposit must fail with DepositOccupied; cancelling refunds.
            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to dock onto Base HQ for second Gold Mine.");

            int goldBeforeSecondMine = seat0.Gold;
            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 2);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not pick up second Gold Mine.");
            Assert.That(seat0.Gold, Is.LessThanOrEqualTo(goldBeforeSecondMine - 70));

            lastFeedback = null;
            builder.RequestDeployCarried(0, new Vector2(-9.5f, 0f));
            yield return Until(() => lastFeedback.HasValue, 3f, "No feedback for occupied deposit check.");
            Assert.That(lastFeedback.Value.Accepted, Is.False);
            Assert.That(lastFeedback.Value.RejectReason, Is.EqualTo(BuildRejectReason.DepositOccupied));
            Assert.That(ship0.IsCarrying, Is.True);

            int goldBeforeCancel = seat0.Gold;
            cancelledRefund = -1;
            builder.RequestCancelCarry(0);
            yield return Until(() => !ship0.IsCarrying && cancelledRefund == 80, 3f, "Cancel after occupied rejection did not refund gold.");
            Assert.That(seat0.Gold, Is.GreaterThanOrEqualTo(goldBeforeCancel + 80));

            // 6. Dock at Base, carry & deploy Territory Balloon (Slot 0) at (-6, 0) -> expands TeamA territory past x = -1.5.
            Vector2 expandedPoint = new(-1.5f, -1.3f);
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.False);

            ship0.transform.position = hqA.DockAnchorWorld;
            ship0.Docking.RequestToggleDock();
            yield return Until(() => ship0.Docking.IsDockedAtBase, 3f, "Airship failed to dock onto Base HQ for Balloon.");

            lastFeedback = null;
            builder.RequestBeginCarryFromSlot(0, 0);
            yield return Until(() => ship0.IsCarrying && lastFeedback.HasValue, 3f, "Airship did not pick up Territory Balloon.");

            lastFeedback = null;
            builder.RequestDeployCarried(0, new Vector2(-6f, 0f));
            yield return Until(() => lastFeedback.HasValue && !ship0.IsCarrying, 3f, "Territory Balloon deploy did not finish.");
            Assert.That(lastFeedback.Value.Accepted, Is.True, $"Unexpected Balloon reject: {lastFeedback.Value.RejectReason}");
            Assert.That(territory.IsInsideTeamTerritory(TeamId.TeamA, expandedPoint), Is.True);

            // 7. Despawning Gold Mine releases GoldDeposit and reverts income rate.
            _networkManager.ServerManager.Despawn(mineStructure.NetworkObject);
            yield return Until(() => !deposit.IsOccupied && Mathf.Abs(seat0.IncomePerSecond) < 0.01f, 3f, "GoldDeposit did not release when Gold Mine despawned.");
#else
            Assert.Ignore("Prefab asset lookup requires Unity Editor.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator AirshipMovesFreelyWhileCarryingEvenWhenPickedUpFromDockAndHighlightsOnlyValidDeposits()
        {
#if UNITY_EDITOR
            var seatPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PlayerSeat.prefab");
            var modePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GameModeController.prefab");
            var economyPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/EconomyController.prefab");
            var territoryPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/TerritoryController.prefab");
            var buildPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/StructureBuildController.prefab");
            var structurePrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GreyboxStructure.prefab");
            var airshipPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Airship.prefab");
            var depositPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/GoldDeposit.prefab");
            var padPrefabGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/DockableAnchorPad.prefab");

            if (seatPrefabGo == null || modePrefabGo == null || economyPrefabGo == null || territoryPrefabGo == null
                || buildPrefabGo == null || structurePrefabGo == null || airshipPrefabGo == null
                || depositPrefabGo == null || padPrefabGo == null)
            {
                Assert.Ignore("Carry & Place prefabları eksik. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.");
            }

            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _prefabs.AddObject(seatPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(modePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(economyPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(territoryPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(buildPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(structurePrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(airshipPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(depositPrefabGo.GetComponent<NetworkObject>(), true, false);
            _prefabs.AddObject(padPrefabGo.GetComponent<NetworkObject>(), true, false);

            BuildLocalSessionRig(7783, includeInputRouter: true);
            yield return null;

            Assert.That(_session.StartLocalHost(), Is.True, _session.LastError);
            yield return Until(
                () => _session.IsAuthenticated && _networkManager.ClientManager.Connection != null && _networkManager.ClientManager.Connection.LoadedStartScenes(true),
                10f,
                "Local host did not finish authentication/start scenes in time.");

            Assert.That(_networkManager.ServerManager.Clients.TryGetValue(_networkManager.ClientManager.Connection.ClientId, out NetworkConnection hostConn), Is.True);

            CardCatalogSO catalog = BuildRuntimeCatalog(structurePrefabGo.GetComponent<NetworkObject>());
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
                startingGold: 300);

            Assert.That(context.TryGetSeat(0, out PlayerSeatController seat0), Is.True);
            seat0.ApplyValidatedLoadoutServer(new ushort[] { 1, 10, 20, 11, 12 });

            GameModeController gameMode = Object.Instantiate(modePrefabGo.GetComponent<GameModeController>());
            _networkManager.ServerManager.Spawn(gameMode.NetworkObject);
            gameMode.InitializeMatchServer(MatchMode.LocalPvP, null, context);
            gameMode.TransitionPhaseServer(MatchPhase.Playing);

            EconomyController economy = Object.Instantiate(economyPrefabGo.GetComponent<EconomyController>());
            _networkManager.ServerManager.Spawn(economy.NetworkObject);
            economy.InitializeServer(context, gameMode, baseIncomeOverride: 0f);

            TerritoryController territory = Object.Instantiate(territoryPrefabGo.GetComponent<TerritoryController>());
            _networkManager.ServerManager.Spawn(territory.NetworkObject);
            territory.InitializeServer(context, new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f), initialRadius: 8.5f);

            StructureBuildController builder = Object.Instantiate(buildPrefabGo.GetComponent<StructureBuildController>());
            _networkManager.ServerManager.Spawn(builder.NetworkObject);
            builder.InitializeServer(context, gameMode, catalog, structurePrefabGo.GetComponent<StructureBase>());

            DockableAnchorNode pad = Object.Instantiate(padPrefabGo.GetComponent<DockableAnchorNode>(), new Vector3(-12f, -2f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(pad.NetworkObject);
            pad.InitializeServer(TeamId.TeamA, context);

            GoldDepositNode depositInside = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(-9.5f, -2f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(depositInside.NetworkObject);
            depositInside.InitializeServer(1, context);

            GoldDepositNode depositOutside = Object.Instantiate(depositPrefabGo.GetComponent<GoldDepositNode>(), new Vector3(9.5f, -2f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(depositOutside.NetworkObject);
            depositOutside.InitializeServer(2, context);

            AirshipController ship0 = Object.Instantiate(airshipPrefabGo.GetComponent<AirshipController>(), new Vector3(-12f, 0f, 0f), Quaternion.identity);
            _networkManager.ServerManager.Spawn(ship0.NetworkObject, hostConn);
            ship0.InitializeServer(0, TeamId.TeamA, context, bounds);

            yield return Until(
                () => builder.IsClientInitialized && ship0.IsClientInitialized && depositInside.IsClientInitialized
                    && depositOutside.IsClientInitialized && pad.IsClientInitialized && territory.IsClientInitialized,
                5f,
                "Spawned network objects did not initialize on local client in time.");

            // 1. Dock onto the allied Base pad first -> enters BaseDeckSelect at slot 0.
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { DockPressed = true, SelectedBuildSlot = -1 });
            yield return Until(
                () => ship0.Docking.IsDockedAtBase && ship0.Docking.SubMode == DockedInteractionSubMode.BaseDeckSelect,
                3f,
                "Airship did not dock onto Base pad before deck cycling.");

            // 2. Cycle Right twice (Slot 0 -> 1 -> 2 = Gold Mine) via Left/Right input and Confirm -> auto-undocks and enters carry state.
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { CycleDelta = 1, SelectedBuildSlot = -1 });
            yield return Until(() => ship0.Docking.State.SelectedDeckSlot == 1, 2f, "Cycle Right did not advance from slot 0 to slot 1.");

            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { CycleDelta = 1, SelectedBuildSlot = -1 });
            yield return Until(() => ship0.Docking.State.SelectedDeckSlot == 2 && ship0.Docking.State.TargetCardId == 20, 2f, "Cycle Right did not select Gold Mine at slot 2.");

            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { ConfirmPlacePressed = true, SelectedBuildSlot = -1 });
            yield return Until(() => ship0.IsCarrying && !ship0.Docking.IsDocked, 3f, "Confirming Gold Mine at docked Base did not auto-undock and enter carry state.");

            // 3. Only the in-territory/in-range deposit highlights green (Valid); out-of-territory stays OutsideTerritory.
            yield return Until(
                () => depositInside.CurrentSpotState == GoldDepositSpotState.Valid
                    && depositOutside.CurrentSpotState == GoldDepositSpotState.OutsideTerritory,
                3f,
                $"Unexpected deposit spot states: inside={depositInside.CurrentSpotState}, outside={depositOutside.CurrentSpotState}");

            // 4. Airship moves freely via synthetic Move input while carrying.
            float startX = ship0.transform.position.x;
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = new Vector2(1f, 0f), SelectedBuildSlot = -1 });
            yield return Until(() => ship0.transform.position.x > startX + 0.15f, 3f, "Airship failed to move while carrying structure.");
            Assert.That(ship0.IsCarrying, Is.True);

            // 5. Cancelling carry clears carry state and resets all deposit highlight states to Neutral.
            _inputRouter.InjectSyntheticInput(0, new PlayerInputSnapshot { Move = Vector2.zero, CancelCarryPressed = true, SelectedBuildSlot = -1 });
            yield return Until(
                () => !ship0.IsCarrying
                    && depositInside.CurrentSpotState == GoldDepositSpotState.Neutral
                    && depositOutside.CurrentSpotState == GoldDepositSpotState.Neutral,
                3f,
                "Cancelling carry did not clear carry state or reset deposit highlight states.");
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
                bool requiresGoldDeposit,
                Vector2 footprint)
            {
                var card = ScriptableObject.CreateInstance<StructureCardSO>();
                card.ConfigureForTest(id, name, category, lockedZero, selectable, unlocked, cost, 0);
                card.ConfigureGameplayStats(territoryRadius, incomeBonus, 0.1f, requiresGround, 120f, footprint, structurePrefabNob, requiresGoldDeposit, buildDurationSeconds: 0f);
                _runtimeAssets.Add(card);
                return card;
            }

            var balloon = CreateCard(1, "Territory Balloon", StructureCategory.Territory, true, false, true, 75, 7.5f, 0f, false, false, new Vector2(1.6f, 1.6f));
            var cannon = CreateCard(10, "Cannon", StructureCategory.Offense, false, true, true, 100, 0f, 0f, true, false, new Vector2(1.8f, 1.4f));
            var shield = CreateCard(11, "Shield Generator", StructureCategory.Defense, false, true, true, 120, 0f, 0f, true, false, new Vector2(1.8f, 1.4f));
            var flak = CreateCard(12, "Flak Tower", StructureCategory.Offense, false, true, true, 110, 0f, 0f, true, false, new Vector2(1.8f, 1.4f));
            var mine = CreateCard(20, "Gold Mine", StructureCategory.Economy, false, true, true, 80, 0f, 8f, true, true, new Vector2(1.8f, 1.4f));

            var catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
            catalog.ConfigureForTest(balloon, new[] { balloon, cannon, shield, flak, mine });
            _runtimeAssets.Add(catalog);
            return catalog;
        }

        private void BuildLocalSessionRig(ushort port, bool includeInputRouter = false)
        {
#if UNITY_EDITOR
            GameObject template = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
                "LocalCarryTestTemplate",
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
            _root.name = "LocalCarryTestRoot";
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
