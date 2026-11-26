using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PirateGame.UI.Editor
{
    public static class NavigationBuild
    {
        [MenuItem("Tools/Navigation/Build Windows M1.1 Preview")]
        public static void BuildWindows()
        {
            const string file = "Builds/WindowsM11/Untitled Pirate Game.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/MainMenu.unity" }, target = BuildTarget.StandaloneWindows64,
                locationPathName = file, options = BuildOptions.Development
            });
            var s = report.summary;
            Debug.Log($"[M1.1] Windows x64 {s.result}, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize} bytes.");
            if (s.result != BuildResult.Succeeded) throw new BuildFailedException("M1 Windows build failed.");
        }
    }
}