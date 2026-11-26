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
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UiButtonFeedback = PirateGame.UI.ButtonFeedback;
using UiButtonTheme = PirateGame.UI.ButtonTheme;
using UiReferenceFrame = PirateGame.UI.ReferenceFrame;
using UiScreenView = PirateGame.UI.ScreenView;
using UiWireframeShape = PirateGame.UI.WireframeShape;

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
            if (string.IsNullOrEmpty(previous.path) && Application.isBatchMode)
            {
                previous = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            }

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
                SetBool(hud, "_renderLegacyImGui", false);

                var worldDockedUi = matchControllerGo.AddComponent<DockedStructureWorldUI>();
                SetRef(worldDockedUi, "_bootstrap", bootstrap);

                CreateArenaBattleUi(matchControllerGo, bootstrap);

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

        private static void CreateArenaBattleUi(GameObject matchControllerGo, ArenaSessionBootstrap bootstrap)
        {
            const string pauseScreenPrefabPath = "Assets/UI/Screens/14_Pause.prefab";
            const string victoryScreenPrefabPath = "Assets/UI/Screens/15_Victory.prefab";
            const string defeatScreenPrefabPath = "Assets/UI/Screens/16_Defeat.prefab";
            const string tmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            const string themePath = "Assets/UI/Components/WireframeButtonTheme.asset";

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpFontPath);
            UiButtonTheme theme = AssetDatabase.LoadAssetAtPath<UiButtonTheme>(themePath);

            var canvasGo = new GameObject("ArenaBattleUiCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var frameGo = new GameObject("ReferenceFrame", typeof(RectTransform));
            frameGo.transform.SetParent(canvasGo.transform, false);
            var frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = Vector2.zero;
            frameRect.sizeDelta = new Vector2(1920f, 1080f);
            frameGo.AddComponent<UiReferenceFrame>();

            ArenaBattleHudView hudView = CreateDualSeatBattleHud(frameRect, font, theme);
            UiScreenView pauseView = InstantiateUiPrefabComponent<UiScreenView>(pauseScreenPrefabPath, frameGo.transform, active: false);
            UiScreenView victoryView = InstantiateUiPrefabComponent<UiScreenView>(victoryScreenPrefabPath, frameGo.transform, active: false);
            UiScreenView defeatView = InstantiateUiPrefabComponent<UiScreenView>(defeatScreenPrefabPath, frameGo.transform, active: false);

            var controller = matchControllerGo.AddComponent<ArenaBattleUiController>();
            SetRef(controller, "_bootstrap", bootstrap);
            SetRef(controller, "_hudView", hudView);
            SetRef(controller, "_pauseView", pauseView);
            SetRef(controller, "_victoryView", victoryView);
            SetRef(controller, "_defeatView", defeatView);
        }

        private static ArenaBattleHudView CreateDualSeatBattleHud(RectTransform parent, TMP_FontAsset font, UiButtonTheme theme)
        {
            Color surfaceWhite = new(0.98039216f, 0.98039216f, 0.98039216f, 1f);
            Color outlineGrey = new(0.46666667f, 0.46666667f, 0.46666667f, 1f);
            Color darkInk = new(0.14509805f, 0.14509805f, 0.14509805f, 1f);
            Color subInk = new(0.38f, 0.40f, 0.44f, 1f);
            Color blueTeam = new(0.18f, 0.48f, 0.84f, 1f);
            Color redTeam = new(0.82f, 0.25f, 0.25f, 1f);
            Color barBacking = new(0.11f, 0.13f, 0.17f, 0.92f);

            var hudRoot = CreateUiRect("ArenaBattleHudView", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var hudView = hudRoot.gameObject.AddComponent<ArenaBattleHudView>();

            // Top-Left Blue Gold Panel
            CreateTopGoldPanel(
                hudRoot, "LeftGoldPanel", isLeft: true, blueTeam, surfaceWhite, outlineGrey, darkInk, subInk, font,
                out GameObject leftGoldRoot, out TMP_Text leftGoldLabel, out TMP_Text leftIncomeLabel);

            // Top-Center Pause Button
            Button pauseButton = CreateTulparButton(
                "PauseButton", hudRoot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(176f, 54f),
                "PAUSE", 20, surfaceWhite, outlineGrey, darkInk, font, theme, out _);

            // Top-Right Red Gold Panel
            CreateTopGoldPanel(
                hudRoot, "RightGoldPanel", isLeft: false, redTeam, surfaceWhite, outlineGrey, darkInk, subInk, font,
                out GameObject rightGoldRoot, out TMP_Text rightGoldLabel, out TMP_Text rightIncomeLabel);

            // Blue & Red HQ Health Bars cleanly anchored below each gold panel
            const float hqBarWidth = 272f;
            CreateHqHealthBar(
                hudRoot, "BlueHqBar", isLeft: true, hqBarWidth, blueTeam, barBacking, outlineGrey, font,
                out GameObject blueHqRoot, out Image blueHqFill, out TMP_Text blueHqLabel);
            CreateHqHealthBar(
                hudRoot, "RedHqBar", isLeft: false, hqBarWidth, redTeam, barBacking, outlineGrey, font,
                out GameObject redHqRoot, out Image redHqFill, out TMP_Text redHqLabel);

            // Dedicated Bottom-Left (Seat 0) & Bottom-Right (Seat 1) Shop Panels
            ArenaSeatShopPanel leftShop = CreateSeatShopPanel(
                hudRoot, "LeftShopPanel", seatId: 0, isLeft: true, blueTeam, surfaceWhite, outlineGrey, darkInk, font, theme);
            ArenaSeatShopPanel rightShop = CreateSeatShopPanel(
                hudRoot, "RightShopPanel", seatId: 1, isLeft: false, redTeam, surfaceWhite, outlineGrey, darkInk, font, theme);

            // High-Contrast Bottom-Left & Bottom-Right Carry / Feedback Strips
            ArenaCarryStripView leftCarry = CreateCarryStrip(
                hudRoot, "LeftCarryStrip", isLeft: true, blueTeam, darkInk, outlineGrey, font, theme);
            ArenaCarryStripView rightCarry = CreateCarryStrip(
                hudRoot, "RightCarryStrip", isLeft: false, redTeam, darkInk, outlineGrey, font, theme);

            hudView.Configure(
                leftGoldRoot, leftGoldLabel, leftIncomeLabel,
                pauseButton,
                rightGoldRoot, rightGoldLabel, rightIncomeLabel,
                blueHqRoot, blueHqFill, hqBarWidth, blueHqLabel,
                redHqRoot, redHqFill, hqBarWidth, redHqLabel,
                leftShop, rightShop,
                leftCarry, rightCarry);

            return hudView;
        }

        private static void CreateTopGoldPanel(
            RectTransform parent,
            string name,
            bool isLeft,
            Color teamColor,
            Color surfaceColor,
            Color outlineColor,
            Color inkColor,
            Color subInkColor,
            TMP_FontAsset font,
            out GameObject panelRoot,
            out TMP_Text goldLabel,
            out TMP_Text incomeLabel)
        {
            Vector2 anchor = new(isLeft ? 0f : 1f, 1f);
            RectTransform root = CreateUiRect(name, parent, anchor, anchor, anchor, new Vector2(isLeft ? 36f : -36f, -24f), new Vector2(272f, 96f));
            AddSurfaceWithOutline(root.gameObject, surfaceColor, outlineColor, 2f);
            panelRoot = root.gameObject;

            Vector2 stripAnchor = new(isLeft ? 0f : 1f, 0f);
            Vector2 stripMax = new(isLeft ? 0f : 1f, 1f);
            RectTransform strip = CreateUiRect("TeamAccent", root, stripAnchor, stripMax, new Vector2(isLeft ? 0f : 1f, 0.5f), Vector2.zero, new Vector2(8f, 0f));
            var stripImg = strip.gameObject.AddComponent<Image>();
            stripImg.color = teamColor;
            stripImg.raycastTarget = false;

            // Coin icon cleanly separated from text
            Vector2 coinAnchor = new(isLeft ? 0f : 1f, 0.5f);
            RectTransform coinRect = CreateUiRect("CoinIcon", root, coinAnchor, coinAnchor, coinAnchor, new Vector2(isLeft ? 22f : -22f, 0f), new Vector2(40f, 40f));
            AddWireframeIcon(coinRect.gameObject, 0, new Color(0.78f, 0.78f, 0.78f, 1f), outlineColor, 2.5f);

            float textLeftPad = isLeft ? 74f : 16f;
            float textRightPad = isLeft ? 14f : 74f;
            TextAlignmentOptions align = isLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;

            RectTransform goldRect = CreateUiRect("GoldValue", root, new Vector2(0f, 0.44f), new Vector2(1f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            goldRect.offsetMin = new Vector2(textLeftPad, 0f);
            goldRect.offsetMax = new Vector2(-textRightPad, 0f);
            goldLabel = CreateTmpText(goldRect.gameObject, "250", 30, FontStyles.Bold, inkColor, align, font, autoSize: true, minSize: 18f);

            RectTransform incRect = CreateUiRect("IncomeValue", root, new Vector2(0f, 0.08f), new Vector2(1f, 0.42f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            incRect.offsetMin = new Vector2(textLeftPad, 0f);
            incRect.offsetMax = new Vector2(-textRightPad, 0f);
            incomeLabel = CreateTmpText(incRect.gameObject, "+600/m", 18, FontStyles.Bold, subInkColor, align, font, autoSize: true, minSize: 12f);
        }

        private static void CreateHqHealthBar(
            RectTransform parent,
            string name,
            bool isLeft,
            float width,
            Color fillColor,
            Color backingColor,
            Color outlineColor,
            TMP_FontAsset font,
            out GameObject rootGo,
            out Image fillImage,
            out TMP_Text label)
        {
            Vector2 anchor = new(isLeft ? 0f : 1f, 1f);
            RectTransform root = CreateUiRect(name, parent, anchor, anchor, anchor, new Vector2(isLeft ? 36f : -36f, -128f), new Vector2(width, 34f));
            AddSurfaceWithOutline(root.gameObject, backingColor, outlineColor, 2f);
            rootGo = root.gameObject;

            Vector2 fillAnchorMin = new(isLeft ? 0f : 1f, 0f);
            Vector2 fillAnchorMax = new(isLeft ? 0f : 1f, 1f);
            Vector2 fillPivot = new(isLeft ? 0f : 1f, 0.5f);
            RectTransform fillRect = CreateUiRect("Fill", root, fillAnchorMin, fillAnchorMax, fillPivot, Vector2.zero, new Vector2(width, 0f));
            fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            RectTransform labelRect = CreateUiRect("Label", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            label = CreateTmpText(labelRect.gameObject, isLeft ? "BLUE HQ  500 / 500" : "RED HQ  500 / 500", 15, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font, autoSize: true, minSize: 11f);
        }

        private static ArenaSeatShopPanel CreateSeatShopPanel(
            RectTransform parent,
            string name,
            byte seatId,
            bool isLeft,
            Color teamColor,
            Color surfaceColor,
            Color outlineColor,
            Color inkColor,
            TMP_FontAsset font,
            UiButtonTheme theme)
        {
            const float panelW = 620f;
            const float panelH = 248f;
            Vector2 anchor = new(isLeft ? 0f : 1f, 0f);
            RectTransform root = CreateUiRect(name, parent, anchor, anchor, anchor, new Vector2(isLeft ? 28f : -28f, 24f), new Vector2(panelW, panelH));
            AddSurfaceWithOutline(root.gameObject, surfaceColor, outlineColor, 2f);

            var panel = root.gameObject.AddComponent<ArenaSeatShopPanel>();

            // Header bar (36px)
            RectTransform headerRect = CreateUiRect("HeaderBar", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 36f));
            var headerBg = headerRect.gameObject.AddComponent<Image>();
            headerBg.color = teamColor;
            headerBg.raycastTarget = false;

            RectTransform titleRect = CreateUiRect("HeaderTitle", headerRect, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            titleRect.offsetMin = new Vector2(14f, 0f);
            titleRect.offsetMax = new Vector2(-170f, 0f);
            TMP_Text headerTitle = CreateTmpText(titleRect.gameObject, isLeft ? "BLUE BASE • BUILDINGS" : "RED BASE • BUILDINGS", 15, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, font, autoSize: true, minSize: 12f);

            RectTransform hintPill = CreateUiRect("SelectHintPill", headerRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(150f, 24f));
            var hintPillImg = hintPill.gameObject.AddComponent<Image>();
            hintPillImg.color = new Color(0.10f, 0.12f, 0.16f, 0.85f);
            hintPillImg.raycastTarget = false;
            RectTransform hintTextRect = CreateUiRect("Label", hintPill, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            TMP_Text selectHintLabel = CreateTmpText(hintTextRect.gameObject, isLeft ? "A/D Select" : "←/→ Select", 13, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font, autoSize: true, minSize: 10f);

            // 5 Card Slots (112 x 142) with zero clipping or price overlap
            var slots = new ArenaShopSlotView[5];
            for (int i = 0; i < slots.Length; i++)
            {
                float xPos = 14f + i * 120f;
                RectTransform slotRect = CreateUiRect($"Slot_{i}", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(xPos, -46f), new Vector2(112f, 142f));
                Image tileBg = slotRect.gameObject.AddComponent<Image>();
                tileBg.color = surfaceColor;
                Outline slotOutline = slotRect.gameObject.AddComponent<Outline>();
                slotOutline.effectColor = outlineColor;
                slotOutline.effectDistance = new Vector2(2f, -2f);

                Button btn = slotRect.gameObject.AddComponent<Button>();
                DisableNavigation(btn);
                AttachButtonFeedback(slotRect.gameObject, btn, theme);

                RectTransform emptyRect = CreateUiRect("Empty", slotRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                TMP_Text emptyTxt = CreateTmpText(emptyRect.gameObject, "—", 18, FontStyles.Normal, outlineColor, TextAlignmentOptions.Center, font, autoSize: false, minSize: 18f);
                emptyTxt.raycastTarget = false;

                RectTransform contentRect = CreateUiRect("Content", slotRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

                // Slot index badge top-left
                RectTransform badgeRect = CreateUiRect("Badge", contentRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, -6f), new Vector2(22f, 20f));
                var badgeBg = badgeRect.gameObject.AddComponent<Image>();
                badgeBg.color = inkColor;
                badgeBg.raycastTarget = false;
                RectTransform badgeTextRect = CreateUiRect("Label", badgeRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                TMP_Text slotBadge = CreateTmpText(badgeTextRect.gameObject, (i + 1).ToString(), 12, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font, autoSize: false, minSize: 12f);

                // Wireframe icon glyphs (0: circle, 1: triangle, 2: cross, 3: diamond)
                var glyphs = new UiWireframeShape[4];
                for (int g = 0; g < glyphs.Length; g++)
                {
                    RectTransform glyphRect = CreateUiRect($"Glyph_{g}", contentRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(36f, 36f));
                    glyphs[g] = AddWireframeIcon(glyphRect.gameObject, g, new Color(0.62f, 0.64f, 0.68f, 1f), outlineColor, 2f);
                    glyphRect.gameObject.SetActive(g == 0);
                }

                // Wrapped 2-line title container above price bar
                RectTransform cardTitleRect = CreateUiRect("CardTitle", contentRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(-12f, 46f));
                TMP_Text titleLabel = CreateTmpText(cardTitleRect.gameObject, "Card", 13, FontStyles.Bold, inkColor, TextAlignmentOptions.Center, font, autoSize: true, minSize: 9.5f);
                titleLabel.textWrappingMode = TextWrappingModes.Normal;

                // Dedicated price pill at bottom of card
                RectTransform priceRect = CreateUiRect("PriceRow", contentRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(-10f, 24f));
                var priceBg = priceRect.gameObject.AddComponent<Image>();
                priceBg.color = new Color(0.92f, 0.93f, 0.95f, 1f);
                priceBg.raycastTarget = false;
                RectTransform priceTextRect = CreateUiRect("Label", priceRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                TMP_Text priceLabel = CreateTmpText(priceTextRect.gameObject, "100g", 13, FontStyles.Bold, inkColor, TextAlignmentOptions.Center, font, autoSize: true, minSize: 10f);

                slots[i] = new ArenaShopSlotView
                {
                    button = btn,
                    tileBackground = tileBg,
                    selectionOutline = slotOutline,
                    slotBadge = slotBadge,
                    titleLabel = titleLabel,
                    priceLabel = priceLabel,
                    emptyState = emptyRect.gameObject,
                    contentRoot = contentRect.gameObject,
                    glyphs = glyphs
                };
            }

            // High-Contrast Buy & Leave buttons INSIDE the shop backing
            Button buyButton = CreateTulparButton(
                "BuyButton", root,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -196f), new Vector2(300f, 40f),
                isLeft ? "Space  BUY" : "RCtrl  BUY", 15, inkColor, inkColor, Color.white, font, theme, out TMP_Text buyLabel);

            Button leaveButton = CreateTulparButton(
                "LeaveButton", root,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(326f, -196f), new Vector2(280f, 40f),
                isLeft ? "E  LEAVE" : "Enter  LEAVE", 15, surfaceColor, outlineColor, inkColor, font, theme, out TMP_Text leaveLabel);

            panel.Configure(seatId, headerTitle, selectHintLabel, slots, buyButton, buyLabel, leaveButton, leaveLabel);
            root.gameObject.SetActive(false);
            return panel;
        }

        private static ArenaCarryStripView CreateCarryStrip(
            RectTransform parent,
            string name,
            bool isLeft,
            Color teamColor,
            Color darkInk,
            Color outlineColor,
            TMP_FontAsset font,
            UiButtonTheme theme)
        {
            Vector2 anchor = new(isLeft ? 0f : 1f, 0f);
            RectTransform root = CreateUiRect(name, parent, anchor, anchor, anchor, new Vector2(isLeft ? 28f : -28f, 24f), new Vector2(468f, 92f));
            AddSurfaceWithOutline(root.gameObject, new Color(0.11f, 0.13f, 0.17f, 0.94f), teamColor, 2f);

            RectTransform noticeRect = CreateUiRect("NoticeRow", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-20f, 26f));
            TMP_Text noticeLabel = CreateTmpText(noticeRect.gameObject, string.Empty, 14, FontStyles.Bold, new Color(1f, 0.88f, 0.45f, 1f), TextAlignmentOptions.Center, font, autoSize: true, minSize: 11f);

            Button deployBtn = CreateTulparButton(
                "DeployButton", root,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 10f), new Vector2(234f, 42f),
                "Space  DEPLOY", 14, teamColor, Color.white, Color.white, font, theme, out TMP_Text deployLabel);

            Button cancelBtn = CreateTulparButton(
                "CancelButton", root,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f, 10f), new Vector2(202f, 42f),
                "Q  CANCEL", 14, darkInk, outlineColor, Color.white, font, theme, out TMP_Text cancelLabel);

            root.gameObject.SetActive(false);
            return new ArenaCarryStripView
            {
                root = root.gameObject,
                deployButton = deployBtn,
                deployLabel = deployLabel,
                cancelButton = cancelBtn,
                cancelLabel = cancelLabel,
                noticeGroup = noticeRect.gameObject,
                noticeLabel = noticeLabel
            };
        }

        private static RectTransform CreateUiRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static void AddSurfaceWithOutline(GameObject go, Color fillColor, Color outlineColor, float outlineDist)
        {
            var img = go.AddComponent<Image>();
            img.color = fillColor;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(outlineDist, -outlineDist);
        }

        private static TMP_Text CreateTmpText(
            GameObject go,
            string text,
            float fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment,
            TMP_FontAsset font,
            bool autoSize,
            float minSize)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.enableAutoSizing = autoSize;
            if (autoSize)
            {
                tmp.fontSizeMin = minSize;
                tmp.fontSizeMax = fontSize;
            }
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateTulparButton(
            string name,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 pos,
            Vector2 size,
            string caption,
            float fontSize,
            Color bgColor,
            Color outlineColor,
            Color textColor,
            TMP_FontAsset font,
            UiButtonTheme theme,
            out TMP_Text label)
        {
            RectTransform rect = CreateUiRect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            AddSurfaceWithOutline(rect.gameObject, bgColor, outlineColor, 2f);
            var btn = rect.gameObject.AddComponent<Button>();
            DisableNavigation(btn);
            AttachButtonFeedback(rect.gameObject, btn, theme);

            RectTransform labelRect = CreateUiRect("Label", rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            labelRect.offsetMin = new Vector2(8f, 2f);
            labelRect.offsetMax = new Vector2(-8f, -2f);
            label = CreateTmpText(labelRect.gameObject, caption, fontSize, FontStyles.Bold, textColor, TextAlignmentOptions.Center, font, autoSize: true, minSize: 10f);
            return btn;
        }

        private static void AttachButtonFeedback(GameObject buttonGo, Button button, UiButtonTheme theme)
        {
            if (theme == null)
            {
                return;
            }

            var rt = buttonGo.GetComponent<RectTransform>();
            RectTransform overlayRect = CreateUiRect("Overlay", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var overlayImg = overlayRect.gameObject.AddComponent<Image>();
            overlayImg.color = Color.clear;
            overlayImg.raycastTarget = false;

            RectTransform focusRect = CreateUiRect("FocusOutline", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            focusRect.gameObject.SetActive(false);

            var feedback = buttonGo.AddComponent<UiButtonFeedback>();
            feedback.theme = theme;
            feedback.overlay = overlayImg;
            feedback.focusOutline = focusRect;
        }

        private static void DisableNavigation(Button button)
        {
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
        }

        private static UiWireframeShape AddWireframeIcon(GameObject go, int glyphIndex, Color fillColor, Color edgeColor, float edgeWidth)
        {
            var shape = go.AddComponent<UiWireframeShape>();
            shape.closed = true;
            shape.color = fillColor;
            shape.edge = edgeColor;
            shape.edgeWidth = edgeWidth;
            shape.raycastTarget = false;

            switch (glyphIndex)
            {
                case 0: // Circle (Balloon / Coin)
                {
                    const int segments = 16;
                    var pts = new Vector2[segments];
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = (i / (float)segments) * Mathf.PI * 2f;
                        pts[i] = new Vector2(0.5f + Mathf.Cos(angle) * 0.45f, 0.5f + Mathf.Sin(angle) * 0.45f);
                    }
                    shape.points = pts;
                    break;
                }
                case 1: // Triangle (Gold Mine / Structure)
                    shape.points = new[] { new Vector2(0.08f, 0.1f), new Vector2(0.5f, 0.92f), new Vector2(0.92f, 0.1f) };
                    break;
                case 2: // Cannon / Pentagon
                    shape.points = new[]
                    {
                        new Vector2(0.15f, 0.15f), new Vector2(0.15f, 0.65f), new Vector2(0.88f, 0.82f),
                        new Vector2(0.92f, 0.58f), new Vector2(0.55f, 0.15f)
                    };
                    break;
                default: // Diamond (Shield / Tower)
                    shape.points = new[] { new Vector2(0.5f, 0.94f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.06f), new Vector2(0.08f, 0.5f) };
                    break;
            }

            return shape;
        }

        private static T InstantiateUiPrefabComponent<T>(string assetPath, Transform parent, bool active) where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[M1-1] Missing UI prefab at {assetPath}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                return null;
            }

            instance.SetActive(active);
            return instance.GetComponent<T>();
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
