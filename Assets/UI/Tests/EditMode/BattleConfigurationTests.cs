using System;
using NUnit.Framework;
using PirateGame.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PirateGame.UI.Tests
{
    public class BattleConfigurationTests
    {
        [TestCase(0,false)] [TestCase(1,true)] [TestCase(5,false)] [TestCase(1025,false)] [TestCase(57,true)]
        public void ValidateFiveSlots(int packed,bool valid) => Assert.AreEqual(valid,BattleCards.Valid((ushort)packed));
        [Test]
        public void RequiresTwoDistinctAuthenticatedSeatsAndFrozenValidChoices()
        {
            var s = new PreparationState {Map=BattleLaunchContext.DefaultMap,Lobby=15, Blue=0,Red=-1,BlueReady=true,RedReady=true,
                BlueCards=BattleCards.Starters,RedCards=1};
            Assert.IsFalse(s.CanLaunch);
            s.Red=0;Assert.IsFalse(s.CanLaunch);
            s.Red=1;Assert.IsTrue(s.CanLaunch);
            s.RedReady=false;Assert.IsFalse(s.CanLaunch);
            s.RedReady=true;s.Match="started";Assert.IsFalse(s.CanLaunch);
            s.Match=null;s.RedCards=0;Assert.IsFalse(s.CanLaunch);
        }
        [Test]
        public void OnlineContextRequiresAuthorityAssignedIds()
        {
            Assert.Throws<ArgumentException>(()=>new BattleLaunchContext(BattleMode.OnlinePvP,0,0,1,1));
            var c=new BattleLaunchContext(BattleMode.OnlinePvP,0,0,1,57,15,"match",4,8);
            Assert.AreEqual("Level_01",c.MapId);
            Assert.Throws<ArgumentException>(()=>new BattleLaunchContext(BattleMode.LocalPvP,0,0,1,1,mapId:"Unknown"));
            Assert.AreEqual(8,c.RedPlayerId);Assert.AreEqual(4,c.BluePlayerId);
        }
        [Test]
        public void TechnicalSceneRetainsSpawnerAndDistinctRegisteredActors()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Gameplay/Scenes/NetworkZeppelinTest.unity");
            try
            {
                NetworkBattleSpawner spawner=null;
                foreach(var root in scene.GetRootGameObjects()) spawner ??= root.GetComponentInChildren<NetworkBattleSpawner>(true);
                Assert.IsNotNull(spawner);
                var data=new SerializedObject(spawner);
                var blue=data.FindProperty("blueZeppelinPrefab").objectReferenceValue;
                var red=data.FindProperty("redZeppelinPrefab").objectReferenceValue;
                Assert.NotNull(blue); Assert.NotNull(red);Assert.AreNotSame(blue,red);
            }
            finally {UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}