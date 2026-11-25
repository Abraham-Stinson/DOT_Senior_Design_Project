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
using UnityEngine.UI;

namespace Game.Editor
{
    public static class GameSceneSetupEditor
    {
        public const string ScenesDirectory = "Assets/Game/Scenes";
        public const string MainMenuScenePath = "Assets/Game/Scenes/MainMenu.unity";
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

            CreateMainMenuScene();
            CreateArenaScene(greyboxSprite);
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[M1-1] MainMenu ve CannonBrawlArena sahneleri ile M1-1 prefab kurulumu tamamlandı.");
        }

        [MenuItem("Tools/Game/Open MainMenu Scene")]
        public static void OpenMainMenuScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                SetupAllScenesAndPrefabs();
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

        private static void CreateMainMenuScene()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(menuScene);

            try
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                var cam = cameraGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.067f, 0.094f, 0.153f);

                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<InputSystemUIInputModule>();

                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var canvasGo = new GameObject("MainMenuCanvas");
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGo.AddComponent<GraphicRaycaster>();

                var bgGo = CreateUiObject("Background", canvasGo.transform);
                StretchFull(bgGo.GetComponent<RectTransform>());
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.color = new Color(0.067f, 0.094f, 0.153f, 1f);

                var panelGo = CreateUiObject("MenuPanel", canvasGo.transform);
                var panelRect = panelGo.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(580f, 560f);
                var panelImg = panelGo.AddComponent<Image>();
                panelImg.color = new Color(0.11f, 0.15f, 0.24f, 0.96f);

                var layout = panelGo.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(36, 36, 36, 36);
                layout.spacing = 16f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                CreateLabel("TitleText", panelGo.transform, font, "CANNON BRAWL — ARTILLERY STRATEGY", 24, FontStyle.Bold, Color.white, 44f);
                CreateLabel("SubtitleText", panelGo.transform, font, "M1.1 Greybox · Öncelik 1: Local PvP  |  Öncelik 2: Steam Online", 14, FontStyle.Normal, new Color(0.75f, 0.82f, 0.92f), 28f);

                Button localBtn = CreateButton("LocalPvPButton", panelGo.transform, font, "1. Local PvP (Aynı PC)", 64f, new Color(0.16f, 0.52f, 0.34f));
                Button onlineBtn = CreateButton("OnlinePvPButton", panelGo.transform, font, "2. Online PvP (Steam / FishNet)", 64f, new Color(0.17f, 0.39f, 0.68f));
                Button storyBtn = CreateButton("StoryWipButton", panelGo.transform, font, "3. Hikaye Modu (WIP — Yakında)", 54f, new Color(0.24f, 0.26f, 0.31f));
                storyBtn.interactable = false;
                Button quitBtn = CreateButton("QuitButton", panelGo.transform, font, "Çıkış", 48f, new Color(0.45f, 0.2f, 0.22f));

                Text statusLabel = CreateLabel("StatusLabel", panelGo.transform, font, string.Empty, 14, FontStyle.Italic, new Color(0.9f, 0.85f, 0.55f), 44f);

                var menuController = canvasGo.AddComponent<MainMenuController>();
                SetRef(menuController, "_localPvPButton", localBtn);
                SetRef(menuController, "_onlinePvPButton", onlineBtn);
                SetRef(menuController, "_storyWipButton", storyBtn);
                SetRef(menuController, "_quitButton", quitBtn);
                SetRef(menuController, "_statusLabel", statusLabel);

                EditorSceneManager.SaveScene(menuScene, MainMenuScenePath);
            }
            finally
            {
                if (previous.IsValid())
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(menuScene, true);
                }
            }
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
                var netRoot = new GameObject("ArenaNetworkRoot");
                netRoot.transform.SetParent(matchSystems.transform, false);
                netRoot.SetActive(false);

                var steamTransport = netRoot.AddComponent<FishySteamworks.FishySteamworks>();
                SetBool(steamTransport, "_peerToPeer", true);
                SetInt(steamTransport, "_maximumClients", SessionRules.PlayerLimit - 1);
                SetInt(steamTransport, "_port", 0);

                var tugboat = netRoot.AddComponent<Tugboat>();
                SetStr(tugboat, "_ipv4BindAddress", "127.0.0.1");
                SetStr(tugboat, "_ipv6BindAddress", "::1");
                SetBool(tugboat, "_enableIpv6", false);
                SetInt(tugboat, "_port", 7771);
                SetInt(tugboat, "_maximumClients", 1);
                SetStr(tugboat, "_clientAddress", "127.0.0.1");

                var multipass = netRoot.AddComponent<Multipass>();
                multipass.GlobalServerActions = false;
                var mpSo = new SerializedObject(multipass);
                var transportsProp = mpSo.FindProperty("_transports");
                transportsProp.arraySize = 2;
                transportsProp.GetArrayElementAtIndex(0).objectReferenceValue = steamTransport;
                transportsProp.GetArrayElementAtIndex(1).objectReferenceValue = tugboat;
                mpSo.ApplyModifiedPropertiesWithoutUndo();

                var transportManager = netRoot.AddComponent<TransportManager>();
                transportManager.Transport = multipass;

                var serverManager = netRoot.AddComponent<ServerManager>();
                netRoot.AddComponent<ClientManager>();
                var timeManager = netRoot.AddComponent<TimeManager>();
                SetInt(timeManager, "_tickRate", 30);

                var auth = netRoot.AddComponent<LobbyConnectionAuthenticator>();
                SetRef(serverManager, "_authenticator", auth);
                SetBool(serverManager, "_startOnHeadless", false);

                var networkManager = netRoot.AddComponent<NetworkManager>();
                SetBool(networkManager, "_dontDestroyOnLoad", false);
                SetBool(networkManager, "_runInBackground", true);
                var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(M1SetupEditor.DefaultPrefabObjectsPath);
                SetRef(networkManager, "_spawnablePrefabs", defaultPrefabs);

                var session = netRoot.AddComponent<FishNetSession>();
                SetRef(session, "manager", networkManager);
                SetRef(session, "authenticator", auth);

                var inputRouter = netRoot.AddComponent<PlayerInputRouter>();

                var bootstrap = netRoot.AddComponent<ArenaSessionBootstrap>();
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

                var draftUi = netRoot.AddComponent<DraftLoadoutUI>();
                SetRef(draftUi, "_bootstrap", bootstrap);

                var hud = netRoot.AddComponent<ArenaBootstrapHud>();
                SetRef(hud, "_bootstrap", bootstrap);

                var worldDockedUi = netRoot.AddComponent<DockedStructureWorldUI>();
                SetRef(worldDockedUi, "_bootstrap", bootstrap);

                netRoot.SetActive(true);
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

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateLabel(string name, Transform parent, Font font, string content, int fontSize, FontStyle style, Color color, float height)
        {
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, height);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, Font font, string labelText, float height, Color backgroundColor)
        {
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, height);

            var img = go.AddComponent<Image>();
            img.color = backgroundColor;

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);
            button.colors = colors;

            var labelGo = CreateUiObject("Label", go.transform);
            StretchFull(labelGo.GetComponent<RectTransform>());
            var text = labelGo.AddComponent<Text>();
            text.font = font;
            text.text = labelText;
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;

            return button;
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
