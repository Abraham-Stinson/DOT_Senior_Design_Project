using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.UI.Tests
{
    public sealed class ScreenAssetTests
    {
        static string[] Screens() => AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/UI/Screens"})
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();

        [TestCaseSource(nameof(Screens))]
        public void ScreenIsEditableAndIndependentOfReferenceImages(string path)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var view=asset.GetComponent<ScreenView>();
            Assert.NotNull(view);
            Assert.IsNotEmpty(asset.GetComponentsInChildren<TextMeshProUGUI>(true));
            Assert.AreEqual(view.references.Length,view.references.Select(p=>p.key).Distinct().Count());
            Assert.IsTrue(view.references.All(pair=>pair.target && pair.target.transform.IsChildOf(view.transform)));
            Assert.IsTrue(view.GetComponentsInChildren<MenuCommand>(true).All(c=>c.GetComponent<Button>()!=null));
            Assert.IsTrue(view.GetComponentsInChildren<WireframeShape>(true).All(g=>g.points.Length>=2 && g.GetComponent<CanvasRenderer>()!=null));
            Assert.IsFalse(AssetDatabase.GetDependencies(path).Any(p=>p.Contains("M1_UI_Reference_v2")));
        }

        [Test] public void AllPagesExistAndTitleSceneIsFirst()
        {
            var pages=Screens().Select(path=>AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ScreenView>().page).OrderBy(x=>x).ToArray();
            CollectionAssert.AreEqual(Enum.GetValues(typeof(Page)),pages);
            Assert.AreEqual("Assets/Scenes/MainMenu.unity",EditorBuildSettings.scenes.First(s=>s.enabled).path);
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s=>s.enabled && s.path=="Assets/Scenes/NetworkTest.unity"));
        }

        [Test] public void BattleHudHasNoPreviewWorldOrNavigationDependency()
        {
            const string path="Assets/UI/Components/BattleHud.prefab";
            var h=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BattleHud>();
            Assert.AreEqual(5,h.slots.Length); Assert.AreEqual(3,h.objectives.Length);
            Assert.IsEmpty(h.GetComponentsInChildren<MenuCommand>(true));
            Assert.IsEmpty(h.GetComponentsInChildren<SpriteRenderer>(true));
            Assert.IsFalse(AssetDatabase.GetDependencies(path).Any(p=>p.StartsWith("Assets/Networking/") ||
                p.StartsWith("Assets/Game/Networking/") || p.StartsWith("Assets/Game/Scripts/Networking/") ||
                p.Contains("PreviewCatalog") || p.Contains("UI/Screens/")));
            foreach(var button in h.GetComponentsInChildren<Button>(true)) Assert.NotNull(button.GetComponent<ButtonFeedback>());
        }

        [Test] public void UiAssembliesHaveNoDirectFishNetRuntimeDependency()
        {
            string[] uiAsmdefs =
            {
                "Assets/UI/Runtime/PirateGame.UI.asmdef",
                "Assets/UI/Presentation/PirateGame.UI.Presentation.asmdef",
                "Assets/Game/Scripts/UI/Game.UI.asmdef"
            };
            foreach (string asmdefPath in uiAsmdefs)
            {
                Assert.IsTrue(File.Exists(asmdefPath), $"Missing UI asmdef: {asmdefPath}");
                string json = File.ReadAllText(asmdefPath);
                Assert.IsFalse(json.Contains("FishNet.Runtime"), $"{asmdefPath} must not reference FishNet.Runtime.");
            }
        }

        [Test] public void CampaignRequiresWinningTheRewardLevelAndResetsBetweenPreviews()
        {
            var progress=new CampaignPreviewProgress();
            Assert.IsFalse(progress.RecordPreviewWin(2));Assert.AreEqual(0,progress.LastVictory);
            Assert.IsTrue(progress.RecordPreviewWin(1));Assert.AreEqual(2,progress.AccessibleLevel);
            var hand=new PreviewLoadout();
            var catalog=AssetDatabase.LoadAssetAtPath<PreviewCatalog>("Assets/UI/PreviewCatalog.asset");
            Assert.IsFalse(hand.Add(3,catalog,progress.LastVictory));
            Assert.IsTrue(progress.RecordPreviewWin(2));Assert.IsTrue(hand.Add(3,catalog,progress.LastVictory));
            Assert.IsFalse(progress.RecordPreviewWin(2));Assert.IsFalse(progress.RecordPreviewWin(3));
            Assert.AreEqual(0,new CampaignPreviewProgress().LastVictory);
        }

        [Test] public void SlotsRejectDuplicatesAndLocksAndRespectCapacity()
        {
            var data=AssetDatabase.LoadAssetAtPath<PreviewCatalog>("Assets/UI/PreviewCatalog.asset");
            var hand=new PreviewLoadout();
            Assert.IsFalse(hand.Remove(0));
            Assert.IsFalse(hand.Add(1,data,1));
            Assert.IsFalse(hand.Add(3,data,1));
            Assert.IsTrue(hand.Add(3,data,2));
            Assert.IsTrue(hand.Add(4,data,4));
            Assert.IsFalse(hand.Add(5,data,8));
            Assert.IsTrue(hand.Remove(1));
            Assert.IsTrue(hand.Add(5,data,8,1));
            Assert.AreEqual(5,hand[1]);Assert.AreEqual(0,hand[0]);
        }
    }
}