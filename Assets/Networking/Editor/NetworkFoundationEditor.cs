using System;
using System.IO;
using System.Linq;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateGame.Networking.Editor
{
    [InitializeOnLoad]
    public static class NetworkFoundationEditor
    {
        public const string ScenePath = "Assets/Scenes/NetworkTest.unity";
        public const string SettingsPath = "Assets/Networking/Configuration/SteamNetworkSettings.asset";
        public const string PrefabsPath = "Assets/Networking/Configuration/NetworkTestPrefabs.asset";
        public const string FoundationPrefabPath = "Assets/Networking/Prefabs/NetworkFoundation.prefab";

        static NetworkFoundationEditor()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode) SyncDevelopmentAppId();
            };
        }

        [MenuItem("Tools/Networking/M0-A/Ensure NetworkTest Assets")]
        public static void EnsureTestAssets()
        {
            Directory.CreateDirectory("Assets/Networking/Configuration");
            Directory.CreateDirectory("Assets/Networking/Prefabs");
            AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SteamNetworkSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            var prefabs = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(PrefabsPath);
            if (prefabs == null)
            {
                prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                AssetDatabase.CreateAsset(prefabs, PrefabsPath);
            }
            AssetDatabase.SaveAssets();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Scene previous = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(previous.path))
                {
                    if (!Application.isBatchMode)
                        throw new BuildFailedException("NetworkTest oluşturulmadan önce açık Untitled sahnesini kaydedin. Mevcut sahneniz korunmuştur.");
                    // A batch process owns its initial empty scene; use the existing saved
                    // sample as context so additive creation never discards a user's scene.
                    previous = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                }
                Scene technical = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(technical);
                try
                {
                    var root = new GameObject("Network Foundation");
                    root.SetActive(false);
                    var transport = root.AddComponent<FishySteamworks.FishySteamworks>();
                    var transportManager = root.AddComponent<TransportManager>();
                    transportManager.Transport = transport;
                    var server = root.AddComponent<ServerManager>();
                    root.AddComponent<ClientManager>();
                    var time = root.AddComponent<TimeManager>();
                    var auth = root.AddComponent<LobbyConnectionAuthenticator>();
                    var manager = root.AddComponent<NetworkManager>();
                    var steam = root.AddComponent<SteamRuntime>();
                    var session = root.AddComponent<FishNetSession>();
                    var coordinator = root.AddComponent<OnlineSessionCoordinator>();
                    var probe = root.AddComponent<NetworkProbe>();
                    var panel = root.AddComponent<NetworkTestPanel>();
                    SetBool(transport, "_peerToPeer", true);
                    // FishySteamworks counts only remote Steam sockets; the host is a local socket.
                    SetInt(transport, "_maximumClients", SessionRules.PlayerLimit - 1);
                    SetInt(transport, "_port", 0);
                    SetBool(manager, "_dontDestroyOnLoad", false);
                    SetBool(manager, "_runInBackground", true);
                    SetRef(manager, "_spawnablePrefabs", AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(PrefabsPath));
                    SetRef(server, "_authenticator", auth);
                    SetBool(server, "_startOnHeadless", false);
                    SetInt(time, "_tickRate", 30);
                    SetRef(steam, "settings", AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(SettingsPath));
                    SetRef(session, "manager", manager);
                    SetRef(session, "authenticator", auth);
                    SetRef(coordinator, "steam", steam);
                    SetRef(coordinator, "session", session);
                    SetRef(probe, "session", session);
                    SetRef(panel, "coordinator", coordinator);
                    SetRef(panel, "probe", probe);
                    root.SetActive(true);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoundationPrefabPath);
                    if (prefab == null) prefab = PrefabUtility.SaveAsPrefabAsset(root, FoundationPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    PrefabUtility.InstantiatePrefab(prefab, technical);
                    var camera = new GameObject("Technical Camera").AddComponent<Camera>();
                    camera.orthographic = true;
                    camera.transform.position = new Vector3(0, 0, -10);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);
                    EditorSceneManager.SaveScene(technical, ScenePath);
                }
                finally
                {
                    if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(technical, true);
                }
            }
            // Preserve all existing entries and their enabled flags.
            if (!EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            AssetDatabase.SaveAssets();
            SyncDevelopmentAppId();
            Validate();
        }

        [MenuItem("Tools/Networking/M0-A/Validate NetworkTest")]
        public static void Validate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(SettingsPath);
            if (settings == null || !settings.IsValid) throw new BuildFailedException("Steam Network Settings invalid.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new BuildFailedException("NetworkTest scene missing.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoundationPrefabPath);
            if (prefab == null) throw new BuildFailedException("Network Foundation prefab missing.");
            var manager = prefab.GetComponent<NetworkManager>();
            var transport = prefab.GetComponent<FishySteamworks.FishySteamworks>();
            var transportManager = prefab.GetComponent<TransportManager>();
            Require(manager != null && transport != null && transportManager.Transport == transport, "FishySteamworks must be the only configured transport.");
            Require(new SerializedObject(transport).FindProperty("_peerToPeer").boolValue, "P2P must be enabled.");
            Require(new SerializedObject(transport).FindProperty("_maximumClients").intValue == 1, "One remote client plus one local host required.");
            Require(new SerializedObject(manager).FindProperty("_spawnablePrefabs").objectReferenceValue != null, "Explicit prefab collection required.");
            Require(new SerializedObject(prefab.GetComponent<SteamRuntime>()).FindProperty("settings").objectReferenceValue != null, "Central Steam settings reference required.");
            Require(new SerializedObject(prefab.GetComponent<ServerManager>()).FindProperty("_authenticator").objectReferenceValue != null, "Lobby identity authenticator required.");
            Debug.Log("[M0-A] NetworkTest configuration validated: Steam P2P, 1 remote + local host, 30Hz, lobby authenticator, explicit prefab collection.");
        }

        private static void Require(bool condition, string error) { if (!condition) throw new BuildFailedException(error); }
        private static void SetRef(UnityEngine.Object target, string name, UnityEngine.Object value)
        { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetBool(UnityEngine.Object target, string name, bool value)
        { var so = new SerializedObject(target); so.FindProperty(name).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetInt(UnityEngine.Object target, string name, int value)
        { var so = new SerializedObject(target); so.FindProperty(name).intValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

        [MenuItem("Tools/Networking/M0-A/Sync Development Steam App ID")]
        public static void SyncDevelopmentAppId()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(SettingsPath);
            if (settings != null && settings.IsValid)
                File.WriteAllText("steam_appid.txt", settings.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        [MenuItem("Tools/Networking/M0-A/Build Windows NetworkTest")]
        public static void BuildWindows()
        {
            EnsureTestAssets();
            string output = Path.GetFullPath("Builds/WindowsNetworkTest/Untitled Pirate Game.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Explicit scene list, so the sample scene never becomes the test build's entry scene.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/M0A-WindowsBuild.txt", $"{report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nBytes: {report.summary.totalSize}\nOutput: {output}\n");
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Windows NetworkTest build failed.");
            Debug.Log("[M0-A] Windows build succeeded: " + output);
        }
    }

    public sealed class SteamAppIdBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(NetworkFoundationEditor.SettingsPath);
            if (settings == null || !settings.IsValid) throw new BuildFailedException("Steam settings missing or invalid.");
            if (settings.AppId == 480 && (report.summary.options & BuildOptions.Development) == 0)
                throw new BuildFailedException("App ID 480 yalnızca Development Build için kullanılabilir. Yayın öncesi gerçek App ID girin.");
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0) return;
            var settings = AssetDatabase.LoadAssetAtPath<SteamNetworkSettings>(NetworkFoundationEditor.SettingsPath);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "steam_appid.txt"),
                settings.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }
    }
}
