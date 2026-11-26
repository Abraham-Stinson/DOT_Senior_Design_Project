using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Input;
using Game.UI;
using PirateGame.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    public static class GameSceneSetupEditor
    {
        public const string ScenesDirectory = "Assets/Game/Scenes";
        public const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string ArenaScenePath = "Assets/Game/Scenes/CannonBrawlArena.unity";
        public const string NetworkTestScenePath = "Assets/Scenes/NetworkTest.unity";
        private const string GreyboxArtDirectory = "Assets/Game/Art/Greybox";
        private const string GreyboxSquarePath = "Assets/Game/Art/Greybox/GreyboxSquare.png";

        [MenuItem("Tools/Game/Setup MainMenu & CannonBrawlArena (M1-1)")]
        public static void SetupAllScenesAndPrefabs()
        {
            Directory.CreateDirectory(ScenesDirectory);
            Directory.CreateDirectory(GreyboxArtDirectory);

            Sprite greyboxSprite = M1SetupEditor.EnsureGreyboxSquareSprite();
            M1SetupEditor.CreateM1NetworkPrefabs();
            M1SetupEditor.CreateGreyboxCardCatalogAssets();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                Debug.LogWarning($"[M1-1] Primary MainMenu scene not found at {MainMenuScenePath}. Keeping Build Settings order.");
            }
            CreateArenaScene(greyboxSprite);
            EnsureNetworkFoundationGameplayPrefabs();
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[M1-1] CannonBrawlArena sahnesi, M1-1 prefab kurulumu ve Build Settings guncellendi (Assets/Scenes/MainMenu.unity korundu).");
        }

        [MenuItem("Tools/Game/Build Windows (MainMenu + Online + Arena)")]
        public static void BuildWindowsOnlinePvP()
        {
            SetupAllScenesAndPrefabs();
            var buildScenes = new List<string>
            {
                MainMenuScenePath,
                ArenaScenePath
            };
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NetworkTestScenePath) != null)
            {
                buildScenes.Add(NetworkTestScenePath);
            }

            string output = Path.GetFullPath("Builds/WindowsOnlinePvP/Untitled Pirate Game.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = buildScenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new System.Exception("Windows Online PvP build failed.");
            }
            Debug.Log("[Online PvP] Windows build succeeded: " + output);
        }

        private static void EnsureNetworkFoundationGameplayPrefabs()
        {
            const string foundationPrefabPath = "Assets/Game/Networking/Prefabs/NetworkFoundation.prefab";
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(foundationPrefabPath);
            PrefabObjects gameplayPrefabs = AssetDatabase.LoadAssetAtPath<PrefabObjects>(M1SetupEditor.DefaultPrefabObjectsPath);
            if (prefabAsset == null || gameplayPrefabs == null)
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(foundationPrefabPath);
            try
            {
                if (root.TryGetComponent(out NetworkManager nm))
                {
                    var so = new SerializedObject(nm);
                    SerializedProperty prop = so.FindProperty("_spawnablePrefabs");
                    if (prop != null && prop.objectReferenceValue != gameplayPrefabs)
                    {
                        prop.objectReferenceValue = gameplayPrefabs;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, foundationPrefabPath);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Game/Open MainMenu Scene")]
        public static void OpenMainMenuScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                Debug.LogError($"[M1-1] MainMenu scene not found at {MainMenuScenePath}.");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            }
        }

        [MenuItem("Tools/Game/Open CannonBrawlArena Scene")]
        public static void OpenArenaScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ArenaScenePath) == null)
            {
                SetupAllScenesAndPrefabs();
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);
            }
        }

        private static Sprite EnsureGreyboxSquareSprite()
        {
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

        private static void CreateArenaScene(Sprite greyboxSprite)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene arenaScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(arenaScene);

            try
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                var cam = cameraGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 9f;
                cam.transform.position = new Vector3(0f, 3f, -10f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.078f, 0.106f, 0.161f);

                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<InputSystemUIInputModule>();

                var envRoot = new GameObject("ArenaEnvironment");
                CreateGroundBlock("Ground_Base", envRoot.transform, greyboxSprite, new Vector3(0f, -4f, 0f), new Vector3(36f, 4f, 1f), new Color(0.21f, 0.24f, 0.29f));
                CreateGroundBlock("Ground_TeamA_Platform", envRoot.transform, greyboxSprite, new Vector3(-11f, -1.25f, 0f), new Vector3(10f, 1.5f, 1f), new Color(0.25f, 0.31f, 0.39f));
                CreateGroundBlock("Ground_TeamB_Platform", envRoot.transform, greyboxSprite, new Vector3(11f, -1.25f, 0f), new Vector3(10f, 1.5f, 1f), new Color(0.25f, 0.31f, 0.39f));
                CreateGroundBlock("Ground_Center_Hill", envRoot.transform, greyboxSprite, new Vector3(0f, -1.75f, 0f), new Vector3(6f, 1.0f, 1f), new Color(0.28f, 0.33f, 0.4f));

                var pointsRoot = new GameObject("ArenaPoints");
                var layoutConfig = pointsRoot.AddComponent<ArenaLayoutConfig>();
                Transform teamAHq = CreateMarker("TeamA_HqSpawn", pointsRoot.transform, greyboxSprite, new Vector3(-12.5f, -0.5f, 0f), new Vector3(1.6f, 1.8f, 1f), new Color(0.25f, 0.6f, 1f, 0.45f));
                Transform teamAAirship = CreateMarker("TeamA_AirshipSpawn", pointsRoot.transform, greyboxSprite, new Vector3(-11f, 4.5f, 0f), new Vector3(1.4f, 0.8f, 1f), new Color(0.55f, 0.85f, 1f, 0.45f));
                Transform teamATerritory = CreateMarker("TeamA_TerritoryOrigin", pointsRoot.transform, null, new Vector3(-12.5f, -0.5f, 0f), Vector3.one, Color.clear);

                Transform teamBHq = CreateMarker("TeamB_HqSpawn", pointsRoot.transform, greyboxSprite, new Vector3(12.5f, -0.5f, 0f), new Vector3(1.6f, 1.8f, 1f), new Color(1f, 0.35f, 0.35f, 0.45f));
                Transform teamBAirship = CreateMarker("TeamB_AirshipSpawn", pointsRoot.transform, greyboxSprite, new Vector3(11f, 4.5f, 0f), new Vector3(1.4f, 0.8f, 1f), new Color(1f, 0.65f, 0.55f, 0.45f));
                Transform teamBTerritory = CreateMarker("TeamB_TerritoryOrigin", pointsRoot.transform, null, new Vector3(12.5f, -0.5f, 0f), Vector3.one, Color.clear);

                Transform centerNode = CreateMarker("Center_NeutralNode", pointsRoot.transform, greyboxSprite, new Vector3(0f, -1.25f, 0f), new Vector3(1.1f, 1.1f, 1f), new Color(1f, 0.85f, 0.25f, 0.45f));

                Transform goldTeamABase = CreateMarker("GoldDeposit_TeamA_Base", pointsRoot.transform, null, new Vector3(-9.5f, -0.5f, 0f), Vector3.one, Color.clear);
                Transform goldTeamAForward = CreateMarker("GoldDeposit_TeamA_Forward", pointsRoot.transform, null, new Vector3(-2.0f, -1.25f, 0f), Vector3.one, Color.clear);
                Transform goldTeamBForward = CreateMarker("GoldDeposit_TeamB_Forward", pointsRoot.transform, null, new Vector3(2.0f, -1.25f, 0f), Vector3.one, Color.clear);
                Transform goldTeamBBase = CreateMarker("GoldDeposit_TeamB_Base", pointsRoot.transform, null, new Vector3(9.5f, -0.5f, 0f), Vector3.one, Color.clear);

                layoutConfig.ConfigureForSetup(
                    new Rect(-18f, -2f, 36f, 16f),
                    teamAHq, teamAAirship, teamATerritory,
                    teamBHq, teamBAirship, teamBTerritory,
                    centerNode);
                layoutConfig.SetGoldDepositSpawns(new[] { goldTeamABase, goldTeamAForward, goldTeamBForward, goldTeamBBase });

                var matchSystems = new GameObject("MatchSystems");
                var matchControllerGo = new GameObject("ArenaMatchController");
                matchControllerGo.transform.SetParent(matchSystems.transform, false);
                matchControllerGo.SetActive(false);

                var localRig = new GameObject("ArenaLocalNetworkRig");
                localRig.transform.SetParent(matchSystems.transform, false);
                localRig.SetActive(false);

                var steamTransport = localRig.AddComponent<FishySteamworks.FishySteamworks>();
                SetBool(steamTransport, "_peerToPeer", true);
                SetInt(steamTransport, "_maximumClients", SessionRules.PlayerLimit - 1);
                SetInt(steamTransport, "_port", 0);

                var tugboat = localRig.AddComponent<Tugboat>();
                SetStr(tugboat, "_ipv4BindAddress", "127.0.0.1");
                SetStr(tugboat, "_ipv6BindAddress", "::1");
                SetBool(tugboat, "_enableIpv6", false);
                SetInt(tugboat, "_port", 7771);
                SetInt(tugboat, "_maximumClients", 1);
                SetStr(tugboat, "_clientAddress", "127.0.0.1");

                var multipass = localRig.AddComponent<Multipass>();
                multipass.GlobalServerActions = false;
                var mpSo = new SerializedObject(multipass);
                var transportsProp = mpSo.FindProperty("_transports");
                transportsProp.arraySize = 2;
                transportsProp.GetArrayElementAtIndex(0).objectReferenceValue = steamTransport;
                transportsProp.GetArrayElementAtIndex(1).objectReferenceValue = tugboat;
                mpSo.ApplyModifiedPropertiesWithoutUndo();

                var transportManager = localRig.AddComponent<TransportManager>();
                transportManager.Transport = multipass;

                var serverManager = localRig.AddComponent<ServerManager>();
                localRig.AddComponent<ClientManager>();
                var timeManager = localRig.AddComponent<TimeManager>();
                SetInt(timeManager, "_tickRate", 30);

                var auth = localRig.AddComponent<LobbyConnectionAuthenticator>();
                SetRef(serverManager, "_authenticator", auth);
                SetBool(serverManager, "_startOnHeadless", false);

                var networkManager = localRig.AddComponent<NetworkManager>();
                SetBool(networkManager, "_dontDestroyOnLoad", false);
                SetBool(networkManager, "_runInBackground", true);
                var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(M1SetupEditor.DefaultPrefabObjectsPath);
                SetRef(networkManager, "_spawnablePrefabs", defaultPrefabs);

                var session = localRig.AddComponent<FishNetSession>();
                SetRef(session, "manager", networkManager);
                SetRef(session, "authenticator", auth);

                var inputRouter = matchControllerGo.AddComponent<PlayerInputRouter>();

                var bootstrap = matchControllerGo.AddComponent<ArenaSessionBootstrap>();
                var seatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.PlayerSeatPrefabPath)?.GetComponent<PlayerSeatController>();
                var modePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.GameModeControllerPrefabPath)?.GetComponent<GameModeController>();
                var draftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.DraftLoadoutControllerPrefabPath)?.GetComponent<DraftLoadoutController>();
                var economyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.EconomyControllerPrefabPath)?.GetComponent<EconomyController>();
                var territoryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.TerritoryControllerPrefabPath)?.GetComponent<TerritoryController>();
                var buildPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.BuildControllerPrefabPath)?.GetComponent<StructureBuildController>();
                var structurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.GreyboxStructurePrefabPath)?.GetComponent<StructureBase>();
                var goldDepositPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.GoldDepositPrefabPath)?.GetComponent<GoldDepositNode>();
                var airshipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.AirshipPrefabPath)?.GetComponent<AirshipController>();
                var padPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.DockablePadPrefabPath)?.GetComponent<DockableAnchorNode>();
                var projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(M1SetupEditor.BallisticProjectilePrefabPath)?.GetComponent<BallisticProjectile>();
                var cardCatalog = AssetDatabase.LoadAssetAtPath<CardCatalogSO>(M1SetupEditor.CardCatalogAssetPath);
                SetRef(bootstrap, "_localNetworkRig", localRig);
                SetRef(bootstrap, "_networkSessionComponent", session);
                SetRef(bootstrap, "_networkManager", networkManager);
                SetRef(bootstrap, "_inputRouter", inputRouter);
                SetRef(bootstrap, "_seatPrefab", seatPrefab);
                SetRef(bootstrap, "_gameModePrefab", modePrefab);
                SetRef(bootstrap, "_draftControllerPrefab", draftPrefab);
                SetRef(bootstrap, "_economyControllerPrefab", economyPrefab);
                SetRef(bootstrap, "_territoryControllerPrefab", territoryPrefab);
                SetRef(bootstrap, "_buildControllerPrefab", buildPrefab);
                SetRef(bootstrap, "_greyboxStructurePrefab", structurePrefab);
                SetRef(bootstrap, "_goldDepositPrefab", goldDepositPrefab);
                SetRef(bootstrap, "_airshipPrefab", airshipPrefab);
                SetRef(bootstrap, "_dockablePadPrefab", padPrefab);
                SetRef(bootstrap, "_projectilePrefab", projectilePrefab);
                SetRef(bootstrap, "_cardCatalog", cardCatalog);
                SetRef(bootstrap, "_arenaLayout", layoutConfig);

                var draftUi = matchControllerGo.AddComponent<DraftLoadoutUI>();
                SetRef(draftUi, "_bootstrap", bootstrap);

                var hud = matchControllerGo.AddComponent<ArenaBootstrapHud>();
                SetRef(hud, "_bootstrap", bootstrap);

                var worldDockedUi = matchControllerGo.AddComponent<DockedStructureWorldUI>();
                SetRef(worldDockedUi, "_bootstrap", bootstrap);

                // ArenaLocalNetworkRig intentionally stays inactive by default so Online PvP never initializes a duplicate NetworkManager;
                // ArenaSessionBootstrap.Awake activates it automatically for Local PvP.
                matchControllerGo.SetActive(true);
                EditorSceneManager.SaveScene(arenaScene, ArenaScenePath);
            }
            finally
            {
                if (previous.IsValid())
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(arenaScene, true);
                }
            }
        }

        private static void UpdateBuildSettings()
        {
            var desired = new List<EditorBuildSettingsScene>
            {
                new(MainMenuScenePath, true),
                new(ArenaScenePath, true)
            };

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NetworkTestScenePath) != null)
            {
                desired.Add(new EditorBuildSettingsScene(NetworkTestScenePath, true));
            }

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (string.IsNullOrEmpty(existing.path))
                {
                    continue;
                }

                if (desired.Any(s => s.path == existing.path))
                {
                    continue;
                }

                desired.Add(existing);
            }

            EditorBuildSettings.scenes = desired.ToArray();
        }

        private static void CreateGroundBlock(string name, Transform parent, Sprite sprite, Vector3 pos, Vector3 scale, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;

            go.AddComponent<BoxCollider2D>();
        }

        private static Transform CreateMarker(string name, Transform parent, Sprite sprite, Vector3 pos, Vector3 scale, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;

            if (sprite != null && color.a > 0f)
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = color;
                sr.sortingOrder = 2;
            }

            return go.transform;
        }

        private static void SetRef(Object target, string name, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(name).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string name, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(name).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string name, int value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(name).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetStr(Object target, string name, string value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(name).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
