using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishNet.Component.Transforming;
using FishNet.Editing;
using FishNet.Managing.Object;
using FishNet.Object;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class M1SetupEditor
    {
        public const string PrefabDirectory = "Assets/Game/Prefabs";
        public const string PlayerSeatPrefabPath = "Assets/Game/Prefabs/PlayerSeat.prefab";
        public const string GameModeControllerPrefabPath = "Assets/Game/Prefabs/GameModeController.prefab";
        public const string DraftLoadoutControllerPrefabPath = "Assets/Game/Prefabs/DraftLoadoutController.prefab";
        public const string EconomyControllerPrefabPath = "Assets/Game/Prefabs/EconomyController.prefab";
        public const string TerritoryControllerPrefabPath = "Assets/Game/Prefabs/TerritoryController.prefab";
        public const string BuildControllerPrefabPath = "Assets/Game/Prefabs/StructureBuildController.prefab";
        public const string GreyboxStructurePrefabPath = "Assets/Game/Prefabs/GreyboxStructure.prefab";
        public const string GoldDepositPrefabPath = "Assets/Game/Prefabs/GoldDeposit.prefab";
        public const string AirshipPrefabPath = "Assets/Game/Prefabs/Airship.prefab";
        public const string DockablePadPrefabPath = "Assets/Game/Prefabs/DockableAnchorPad.prefab";
        public const string BallisticProjectilePrefabPath = "Assets/Game/Prefabs/BallisticProjectile.prefab";
        public const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";
        public const string CardsAssetDirectory = "Assets/Game/Data/Cards";
        public const string CardCatalogAssetPath = "Assets/Game/Data/CardCatalog.asset";
        public const string GreyboxArtDirectory = "Assets/Game/Art/Greybox";
        public const string GreyboxSquarePath = "Assets/Game/Art/Greybox/GreyboxSquare.png";

        public static Sprite EnsureGreyboxSquareSprite()
        {
            Directory.CreateDirectory(GreyboxArtDirectory);

            if (!File.Exists(GreyboxSquarePath))
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.white;
                }
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(GreyboxSquarePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(GreyboxSquarePath, ImportAssetOptions.ForceUpdate);
            }

            if (AssetImporter.GetAtPath(GreyboxSquarePath) is TextureImporter importer)
            {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (Mathf.Abs(importer.spritePixelsPerUnit - 4f) > 0.01f)
                {
                    importer.spritePixelsPerUnit = 4f;
                    changed = true;
                }
                if (importer.filterMode != FilterMode.Point)
                {
                    importer.filterMode = FilterMode.Point;
                    changed = true;
                }
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(GreyboxSquarePath);
        }

        [MenuItem("Tools/Game/M1-1/Create M1-1 Network Prefabs")]
        public static void CreateM1NetworkPrefabs()
        {
            Directory.CreateDirectory(PrefabDirectory);
            Sprite greyboxSprite = EnsureGreyboxSquareSprite();

            SetFishNetIgnorePostProcess(true, out bool previousIgnorePostProcess);
            try
            {
                NetworkObject seatNob = EnsureNetworkPrefab<PlayerSeatController>(PlayerSeatPrefabPath, "PlayerSeat");
                NetworkObject modeNob = EnsureNetworkPrefab<GameModeController>(GameModeControllerPrefabPath, "GameModeController");
                NetworkObject draftNob = EnsureNetworkPrefab<DraftLoadoutController>(DraftLoadoutControllerPrefabPath, "DraftLoadoutController");
                NetworkObject economyNob = EnsureNetworkPrefab<EconomyController>(EconomyControllerPrefabPath, "EconomyController");
                NetworkObject territoryNob = EnsureNetworkPrefab<TerritoryController>(TerritoryControllerPrefabPath, "TerritoryController");
                NetworkObject projectileNob = EnsureBallisticProjectilePrefab(greyboxSprite);
                BallisticProjectile defaultProjectile = projectileNob != null ? projectileNob.GetComponent<BallisticProjectile>() : null;
                NetworkObject structureNob = EnsureGreyboxStructurePrefab(greyboxSprite, defaultProjectile);
                StructureBase defaultStructure = structureNob != null ? structureNob.GetComponent<StructureBase>() : null;
                NetworkObject buildNob = EnsureBuildControllerPrefab(defaultStructure);
                NetworkObject goldDepositNob = EnsureGoldDepositPrefab(greyboxSprite);
                NetworkObject airshipNob = EnsureAirshipPrefab(greyboxSprite);
                NetworkObject padNob = EnsureDockablePadPrefab(greyboxSprite);

                var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
                if (defaultPrefabs != null)
                {
                    AddIfPresent(defaultPrefabs, seatNob);
                    AddIfPresent(defaultPrefabs, modeNob);
                    AddIfPresent(defaultPrefabs, draftNob);
                    AddIfPresent(defaultPrefabs, economyNob);
                    AddIfPresent(defaultPrefabs, territoryNob);
                    AddIfPresent(defaultPrefabs, buildNob);
                    AddIfPresent(defaultPrefabs, structureNob);
                    AddIfPresent(defaultPrefabs, goldDepositNob);
                    AddIfPresent(defaultPrefabs, airshipNob);
                    AddIfPresent(defaultPrefabs, padNob);
                    AddIfPresent(defaultPrefabs, projectileNob);

                    EditorUtility.SetDirty(defaultPrefabs);
                }
                else
                {
                    Debug.LogWarning("[M1-1] Assets/DefaultPrefabObjects.asset bulunamadı; prefablar kaydedildi ancak DefaultPrefabObjects listesine manuel eklenmeli.");
                }

                AssetDatabase.SaveAssets();
                RefreshDefaultPrefabsMenu.RebuildDefaultPrefabs();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                SetFishNetIgnorePostProcess(previousIgnorePostProcess, out _);
            }

            AssetDatabase.Refresh();
            Debug.Log("[M1-1/M1-4] Ağ prefabları (Seat, Mode, Draft, Economy, Territory, Build, Structure, Airship, Pad) hazırlandı.");
        }

        [MenuItem("Tools/Game/M1-2/Create Greybox Card Catalog Assets")]
        public static CardCatalogSO CreateGreyboxCardCatalogAssets()
        {
            Directory.CreateDirectory("Assets/Game/Data");
            Directory.CreateDirectory(CardsAssetDirectory);

            NetworkObject structurePrefabNob = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxStructurePrefabPath)?.GetComponent<NetworkObject>();
            if (structurePrefabNob == null)
            {
                CreateM1NetworkPrefabs();
                structurePrefabNob = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxStructurePrefabPath)?.GetComponent<NetworkObject>();
            }

            StructureCardSO balloon = EnsureStructureCardAsset(
                "Card_01_TerritoryBalloon.asset", 1, "Territory Balloon", StructureCategory.Territory,
                isLockedInSlotZero: true, isDeckSelectable: false, unlockedByDefault: true, buildCostGold: 75, unlockCostSoftCurrency: 0);
            balloon.ConfigureGameplayStats(7.5f, 0f, 1f, false, 120f, new Vector2(1.6f, 1.6f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 2.0f);
            balloon.ConfigureWeaponStats(false, 0f, 1f, 0f, 10f);
            EditorUtility.SetDirty(balloon);

            StructureCardSO cannon = EnsureStructureCardAsset(
                "Card_10_Cannon.asset", 10, "Cannon", StructureCategory.Offense,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: true, buildCostGold: 100, unlockCostSoftCurrency: 0);
            cannon.ConfigureGameplayStats(0f, 0f, 1f, true, 140f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 3.0f);
            cannon.ConfigureWeaponStats(true, 45f, 10f, 24f, 19f, gravityScale: 1f, splashRadius: 1.5f, canTargetAirships: false, preferHighArc: false);
            EditorUtility.SetDirty(cannon);

            StructureCardSO shield = EnsureStructureCardAsset(
                "Card_11_ShieldGenerator.asset", 11, "Shield Generator", StructureCategory.Defense,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: true, buildCostGold: 120, unlockCostSoftCurrency: 0);
            shield.ConfigureGameplayStats(0f, 0f, 1f, true, 180f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 3.0f);
            shield.ConfigureWeaponStats(false, 0f, 10f, 0f, 10f);
            EditorUtility.SetDirty(shield);

            StructureCardSO flak = EnsureStructureCardAsset(
                "Card_12_FlakTower.asset", 12, "Flak Tower", StructureCategory.Offense,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: true, buildCostGold: 110, unlockCostSoftCurrency: 0);
            flak.ConfigureGameplayStats(0f, 0f, 1f, true, 130f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 2.5f);
            flak.ConfigureWeaponStats(true, 30f, 8f, 20f, 24f, gravityScale: 0.35f, splashRadius: 1.8f, canTargetAirships: false, preferHighArc: false);
            EditorUtility.SetDirty(flak);

            StructureCardSO spawner = EnsureStructureCardAsset(
                "Card_13_BombBotSpawner.asset", 13, "Bomb Bot Spawner", StructureCategory.Spawner,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: true, buildCostGold: 150, unlockCostSoftCurrency: 0);
            spawner.ConfigureGameplayStats(0f, 0f, 1f, true, 160f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 3.5f);
            spawner.ConfigureWeaponStats(false, 0f, 10f, 0f, 10f);
            EditorUtility.SetDirty(spawner);

            StructureCardSO goldMine = EnsureStructureCardAsset(
                "Card_20_GoldMine.asset", 20, "Gold Mine", StructureCategory.Economy,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: false, buildCostGold: 80, unlockCostSoftCurrency: 150);
            goldMine.ConfigureGameplayStats(0f, 8f, 1f, true, 110f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: true, buildDurationSeconds: 2.5f);
            goldMine.ConfigureWeaponStats(false, 0f, 10f, 0f, 10f);
            EditorUtility.SetDirty(goldMine);

            StructureCardSO mortar = EnsureStructureCardAsset(
                "Card_21_HeavyMortar.asset", 21, "Heavy Mortar", StructureCategory.Offense,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: false, buildCostGold: 175, unlockCostSoftCurrency: 200);
            mortar.ConfigureGameplayStats(0f, 0f, 1f, true, 150f, new Vector2(2.0f, 1.5f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 4.0f);
            mortar.ConfigureWeaponStats(true, 75f, 12f, 30f, 20f, gravityScale: 1.25f, splashRadius: 3.2f, canTargetAirships: false, preferHighArc: true);
            EditorUtility.SetDirty(mortar);

            StructureCardSO laser = EnsureStructureCardAsset(
                "Card_22_LaserTower.asset", 22, "Laser Tower", StructureCategory.Offense,
                isLockedInSlotZero: false, isDeckSelectable: true, unlockedByDefault: false, buildCostGold: 200, unlockCostSoftCurrency: 250);
            laser.ConfigureGameplayStats(0f, 0f, 1f, true, 140f, new Vector2(1.8f, 1.4f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 4.0f);
            laser.ConfigureWeaponStats(true, 55f, 10f, 24f, 36f, gravityScale: 0.05f, splashRadius: 0f, canTargetAirships: false, preferHighArc: false);
            EditorUtility.SetDirty(laser);

            StructureCardSO hq = EnsureStructureCardAsset(
                "Card_99_Headquarters.asset", 99, "Headquarters", StructureCategory.Core,
                isLockedInSlotZero: false, isDeckSelectable: false, unlockedByDefault: true, buildCostGold: 0, unlockCostSoftCurrency: 0);
            hq.ConfigureGameplayStats(8.5f, 0f, 0f, true, 500f, new Vector2(2.4f, 2.2f), structurePrefabNob, requiresGoldDeposit: false, buildDurationSeconds: 0f);
            hq.ConfigureWeaponStats(false, 0f, 1f, 0f, 10f);
            EditorUtility.SetDirty(hq);

            var allCards = new List<StructureCardSO>
            {
                balloon, cannon, shield, flak, spawner, goldMine, mortar, laser, hq
            };

            var catalog = AssetDatabase.LoadAssetAtPath<CardCatalogSO>(CardCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CardCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CardCatalogAssetPath);
            }

            catalog.ConfigureForTest(balloon, allCards);
            EditorUtility.SetDirty(catalog);

            GameObject buildPrefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(BuildControllerPrefabPath);
            if (buildPrefabGo != null && buildPrefabGo.TryGetComponent(out StructureBuildController buildCtrl))
            {
                var buildSo = new SerializedObject(buildCtrl);
                buildSo.FindProperty("_cardCatalog").objectReferenceValue = catalog;
                buildSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(buildPrefabGo);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[M1-2] Greybox StructureCardSO ve CardCatalog.asset oluşturuldu/güncellendi.");
            return catalog;
        }

        private static void AddIfPresent(DefaultPrefabObjects defaultPrefabs, NetworkObject nob)
        {
            if (nob != null)
            {
                defaultPrefabs.AddObject(nob, checkForDuplicates: true, initializeAdded: false);
            }
        }

        private static NetworkObject EnsureAirshipPrefab(Sprite greyboxSprite)
        {
            var root = new GameObject("Airship");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var nt = GetOrAddComponent<NetworkTransform>(root);
                var ntSo = new SerializedObject(nt);
                ntSo.FindProperty("_componentConfiguration").enumValueIndex = (int)NetworkTransform.ComponentConfigurationType.Disabled;
                ntSo.FindProperty("_clientAuthoritative").boolValue = false;
                ntSo.FindProperty("_sendToOwner").boolValue = true;
                ntSo.FindProperty("_synchronizeScale").boolValue = false;
                ntSo.FindProperty("_interpolation").intValue = 2;
                ntSo.ApplyModifiedPropertiesWithoutUndo();

                var docking = GetOrAddComponent<AirshipDockingController>(root);
                var movement = GetOrAddComponent<AirshipMovement>(root);
                var controller = GetOrAddComponent<AirshipController>(root);

                var bodyGo = new GameObject("BodyVisual");
                bodyGo.transform.SetParent(root.transform, false);
                bodyGo.transform.localScale = new Vector3(1.5f, 0.75f, 1f);
                var bodySr = bodyGo.AddComponent<SpriteRenderer>();
                bodySr.sprite = greyboxSprite;
                bodySr.sortingOrder = 5;

                var indicatorGo = new GameObject("DockIndicator");
                indicatorGo.transform.SetParent(root.transform, false);
                indicatorGo.transform.localScale = new Vector3(1.85f, 1.05f, 1f);
                var indicatorSr = indicatorGo.AddComponent<SpriteRenderer>();
                indicatorSr.sprite = greyboxSprite;
                indicatorSr.sortingOrder = 4;
                indicatorSr.enabled = false;

                var ctrlSo = new SerializedObject(controller);
                ctrlSo.FindProperty("_movement").objectReferenceValue = movement;
                ctrlSo.FindProperty("_docking").objectReferenceValue = docking;
                ctrlSo.FindProperty("_bodyRenderer").objectReferenceValue = bodySr;
                ctrlSo.FindProperty("_dockIndicatorRenderer").objectReferenceValue = indicatorSr;
                ctrlSo.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, AirshipPrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, AirshipPrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static NetworkObject EnsureDockablePadPrefab(Sprite greyboxSprite)
        {
            var root = new GameObject("DockableAnchorPad");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var pad = GetOrAddComponent<DockableAnchorNode>(root);

                var visualGo = new GameObject("PadVisual");
                visualGo.transform.SetParent(root.transform, false);
                visualGo.transform.localScale = new Vector3(1.8f, 0.5f, 1f);
                var sr = visualGo.AddComponent<SpriteRenderer>();
                sr.sprite = greyboxSprite;
                sr.sortingOrder = 2;

                var padSo = new SerializedObject(pad);
                padSo.FindProperty("_visualRenderer").objectReferenceValue = sr;
                padSo.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, DockablePadPrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, DockablePadPrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static NetworkObject EnsureBallisticProjectilePrefab(Sprite greyboxSprite)
        {
            var root = new GameObject("BallisticProjectile");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var nt = GetOrAddComponent<NetworkTransform>(root);
                var ntSo = new SerializedObject(nt);
                ntSo.FindProperty("_componentConfiguration").enumValueIndex = (int)NetworkTransform.ComponentConfigurationType.Disabled;
                ntSo.FindProperty("_clientAuthoritative").boolValue = false;
                ntSo.FindProperty("_sendToOwner").boolValue = true;
                ntSo.FindProperty("_synchronizeScale").boolValue = false;
                ntSo.FindProperty("_interpolation").intValue = 2;
                ntSo.ApplyModifiedPropertiesWithoutUndo();

                var projectile = GetOrAddComponent<BallisticProjectile>(root);

                var visualGo = new GameObject("ProjectileVisual");
                visualGo.transform.SetParent(root.transform, false);
                visualGo.transform.localScale = new Vector3(0.35f, 0.35f, 1f);
                var sr = visualGo.AddComponent<SpriteRenderer>();
                sr.sprite = greyboxSprite;
                sr.sortingOrder = 8;

                var trail = GetOrAddComponent<TrailRenderer>(root);
                trail.time = 0.22f;
                trail.startWidth = 0.16f;
                trail.endWidth = 0.02f;
                trail.sortingOrder = 7;
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null)
                {
                    trail.sharedMaterial = new Material(defaultShader);
                }

                var projSo = new SerializedObject(projectile);
                projSo.FindProperty("_bodyRenderer").objectReferenceValue = sr;
                projSo.FindProperty("_trailRenderer").objectReferenceValue = trail;
                projSo.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, BallisticProjectilePrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BallisticProjectilePrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static NetworkObject EnsureGreyboxStructurePrefab(Sprite greyboxSprite, BallisticProjectile defaultProjectilePrefab)
        {
            var root = new GameObject("GreyboxStructure");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var weapon = GetOrAddComponent<StructureWeaponController>(root);
                var structure = GetOrAddComponent<StructureBase>(root);

                var bodyGo = new GameObject("BodyVisual");
                bodyGo.transform.SetParent(root.transform, false);
                bodyGo.transform.localScale = new Vector3(1.8f, 1.4f, 1f);
                var bodySr = bodyGo.AddComponent<SpriteRenderer>();
                bodySr.sprite = greyboxSprite;
                bodySr.sortingOrder = 3;

                var weaponSo = new SerializedObject(weapon);
                weaponSo.FindProperty("_projectilePrefab").objectReferenceValue = defaultProjectilePrefab;
                weaponSo.ApplyModifiedPropertiesWithoutUndo();

                var so = new SerializedObject(structure);
                so.FindProperty("_bodyRenderer").objectReferenceValue = bodySr;
                so.FindProperty("_territoryRingRenderer").objectReferenceValue = null;
                so.FindProperty("_weaponController").objectReferenceValue = weapon;
                so.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, GreyboxStructurePrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, GreyboxStructurePrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static NetworkObject EnsureBuildControllerPrefab(StructureBase defaultStructurePrefab)
        {
            var root = new GameObject("StructureBuildController");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var ctrl = GetOrAddComponent<StructureBuildController>(root);
                var catalog = AssetDatabase.LoadAssetAtPath<CardCatalogSO>(CardCatalogAssetPath);

                var so = new SerializedObject(ctrl);
                so.FindProperty("_defaultStructurePrefab").objectReferenceValue = defaultStructurePrefab;
                so.FindProperty("_cardCatalog").objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, BuildControllerPrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BuildControllerPrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static NetworkObject EnsureGoldDepositPrefab(Sprite greyboxSprite)
        {
            var root = new GameObject("GoldDeposit");
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(root);
                var deposit = GetOrAddComponent<GoldDepositNode>(root);

                var visualGo = new GameObject("DepositVisual");
                visualGo.transform.SetParent(root.transform, false);
                visualGo.transform.localScale = new Vector3(1.5f, 0.55f, 1f);
                var visualSr = visualGo.AddComponent<SpriteRenderer>();
                visualSr.sprite = greyboxSprite;
                visualSr.color = new Color(0.95f, 0.76f, 0.16f, 0.95f);
                visualSr.sortingOrder = 1;

                var ringGo = new GameObject("DepositHighlightRing");
                ringGo.transform.SetParent(root.transform, false);
                ringGo.transform.localScale = new Vector3(2.2f, 0.85f, 1f);
                var ringSr = ringGo.AddComponent<SpriteRenderer>();
                ringSr.sprite = greyboxSprite;
                ringSr.color = new Color(1f, 0.85f, 0.2f, 0.22f);
                ringSr.sortingOrder = 0;

                var so = new SerializedObject(deposit);
                so.FindProperty("_visualRenderer").objectReferenceValue = visualSr;
                so.FindProperty("_ringRenderer").objectReferenceValue = ringSr;
                so.ApplyModifiedPropertiesWithoutUndo();

                PreserveExistingAssetPathHash(nob, GoldDepositPrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, GoldDepositPrefabPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static StructureCardSO EnsureStructureCardAsset(
            string fileName,
            ushort cardId,
            string displayName,
            StructureCategory category,
            bool isLockedInSlotZero,
            bool isDeckSelectable,
            bool unlockedByDefault,
            int buildCostGold,
            int unlockCostSoftCurrency)
        {
            string path = $"{CardsAssetDirectory}/{fileName}";
            var card = AssetDatabase.LoadAssetAtPath<StructureCardSO>(path);
            if (card == null)
            {
                card = ScriptableObject.CreateInstance<StructureCardSO>();
                card.ConfigureForTest(
                    cardId,
                    displayName,
                    category,
                    isLockedInSlotZero,
                    isDeckSelectable,
                    unlockedByDefault,
                    buildCostGold,
                    unlockCostSoftCurrency);
                AssetDatabase.CreateAsset(card, path);
            }
            else
            {
                card.ConfigureForTest(
                    cardId,
                    displayName,
                    category,
                    isLockedInSlotZero,
                    isDeckSelectable,
                    unlockedByDefault,
                    buildCostGold,
                    unlockCostSoftCurrency);
                EditorUtility.SetDirty(card);
            }

            return card;
        }

        private static NetworkObject EnsureNetworkPrefab<TBehaviour>(string assetPath, string objectName)
            where TBehaviour : NetworkBehaviour
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
            {
                var existingNob = existing.GetComponent<NetworkObject>();
                if (existingNob != null && existing.GetComponent<TBehaviour>() != null)
                {
                    ReserializeNetworkPrefab(existingNob);
                    return existingNob;
                }
            }

            var tempRoot = new GameObject(objectName);
            try
            {
                var nob = GetOrAddComponent<NetworkObject>(tempRoot);
                GetOrAddComponent<TBehaviour>(tempRoot);
                PreserveExistingAssetPathHash(nob, assetPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(tempRoot, assetPath);
                NetworkObject savedNob = saved != null ? saved.GetComponent<NetworkObject>() : nob;
                ReserializeNetworkPrefab(savedNob);
                return savedNob;
            }
            finally
            {
                Object.DestroyImmediate(tempRoot);
            }
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out T existing) ? existing : go.AddComponent<T>();
        }

        private static void PreserveExistingAssetPathHash(NetworkObject targetNob, string assetPath)
        {
            if (targetNob == null || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null && existing.TryGetComponent(out NetworkObject existingNob) && existingNob.AssetPathHash != 0)
            {
                targetNob.SetAssetPathHash(existingNob.AssetPathHash);
            }
        }

        private static void SetFishNetIgnorePostProcess(bool value, out bool previousValue)
        {
            previousValue = false;
            System.Type generatorType = typeof(NetworkObject).Assembly.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator");
            FieldInfo ignoreField = generatorType?.GetField("IgnorePostProcess", BindingFlags.Public | BindingFlags.Static);
            if (ignoreField == null || ignoreField.FieldType != typeof(bool))
            {
                return;
            }

            previousValue = (bool)ignoreField.GetValue(null);
            ignoreField.SetValue(null, value);
        }

        private static void ReserializeNetworkPrefab(NetworkObject nob)
        {
            if (nob == null)
            {
                return;
            }

            MethodInfo removeDuplicatesMethod = typeof(NetworkObject).GetMethod(
                "RemoveDuplicateNetworkObjects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            removeDuplicatesMethod?.Invoke(nob, null);

            MethodInfo reserializeMethod = typeof(NetworkObject).GetMethod(
                "ReserializeEditorSetValues",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool), typeof(bool) },
                null);
            reserializeMethod?.Invoke(nob, new object[] { true, false });

            EditorUtility.SetDirty(nob);
            EditorUtility.SetDirty(nob.gameObject);
        }
    }
}
