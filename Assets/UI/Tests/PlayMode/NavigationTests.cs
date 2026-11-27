using PirateGame.Gameplay;
using System;
using System.Collections;
using NUnit.Framework;
using PirateGame.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PirateGame.UI.Tests
{
    public sealed class NavigationTests
    {
        ScreenRouter ui;
        Keyboard keyboard;
        float volume;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorRouting;

        [UnitySetUp] public IEnumerator Open()
        {
            originalBackground = InputSystem.settings.backgroundBehavior;
            originalEditorRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            ui = Object.FindFirstObjectByType<ScreenRouter>();
            volume = AudioListener.volume;
            keyboard = InputSystem.AddDevice<Keyboard>();
            Assert.That(ui.Current, Is.EqualTo(Page.Title));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        [UnityTearDown] public IEnumerator Close()
        {
            AudioListener.volume = volume;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = originalBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorRouting;
            if (ui) { ui.network.ShutdownSession(); ui.network.Steam.Shutdown(); }
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }

        IEnumerator KeyDown(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
        }
        IEnumerator Release()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
        void Click(string key)
        {
            var item=ui.View.Item(key);
            Assert.IsTrue(item && item.activeInHierarchy,key);
            var button=item.GetComponent<Button>();
            Assert.IsTrue(button.IsInteractable(),key);
            button.onClick.Invoke();
        }
        void Action(Command command, int arg=0) => ui.Execute(command,arg);
        void PageIs(Page p) => Assert.That(ui.Current,Is.EqualTo(p));
        Button ButtonAt(RectTransform target)
        {
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsNotEmpty(hits, $"{target.name} at {point} on {Screen.width}x{Screen.height}");
            return hits[0].gameObject.GetComponentInParent<Button>();
        }
        void LabelIs(string key,string value) => Assert.That(ui.View.Item(key).GetComponent<TMP_Text>().text,Is.EqualTo(value));

        [UnityTest] public IEnumerator TitleKeyboardQuitModalAndSettingsReturn()
        {
            yield return KeyDown(Key.A); PageIs(Page.MainMenu); yield return Release();
            Click("quit"); Assert.IsTrue(ui.QuitOpen);
            Assert.IsFalse(ui.View.Item("campaign").GetComponent<Button>().IsInteractable());
            yield return KeyDown(Key.Escape); Assert.IsFalse(ui.QuitOpen); yield return Release();
            Click("settings"); PageIs(Page.Settings);
            ui.View.Item("master").GetComponent<Slider>().value=.37f; Click("apply");
            Assert.AreEqual(.37f,AudioListener.volume,.001f);
            Click("tab1"); Assert.IsTrue(ui.View.Item("display").activeInHierarchy);
            Click("tab2"); Assert.IsFalse(ui.View.Item("apply").GetComponent<Button>().IsInteractable());
            Click("back"); Click("credits"); PageIs(Page.Credits); Click("back"); PageIs(Page.MainMenu);
        }

        [UnityTest] public IEnumerator CampaignPreviewUnlockIsFirstWinOnlyAndShopIsContextual()
        {
            Click("start");Click("campaign");Assert.AreEqual(1,ui.UnlockedLevel);
            Assert.IsFalse(ui.View.Item("node1").GetComponent<Button>().IsInteractable());
            Click("fight");PageIs(Page.Mission);Click("fight");PageIs(Page.Dialogue);
            yield return null;
            var next = ui.View.Item("advance").GetComponent<Button>();
            var background = ui.View.Item("dialogueClick").GetComponent<Button>();
            Assert.AreSame(next, ButtonAt((RectTransform)next.transform));
            Assert.AreSame(background, ButtonAt((RectTransform)background.transform));
            Click("dialogueClick"); LabelIs("speaker","PRINCE");
            Assert.AreSame(next.gameObject, EventSystem.current.currentSelectedGameObject);
            Click("advance");
            yield return Field(BattleMode.Campaign);
            Assert.AreEqual(1,ui.launch.Context.CampaignLevelId);
            Assert.AreEqual(1,ui.UnlockedLevel);
            yield return LeaveField();
        }

        [UnityTest] public IEnumerator LocalPlayersHaveIndependentSlotsAndMapHoldRestoresPanels()
        {
            Click("start");Click("skirmish");Click("local");PageIs(Page.Map);
            Click("continue");PageIs(Page.Local);Assert.IsFalse(ui.SecondJoined);
            Click("joinSecond");Assert.IsTrue(ui.SecondJoined);
            Assert.IsFalse(ui.View.Item("p0slot0").GetComponent<Button>().IsInteractable());LabelIs("p0slot0name","Balloon");
            Click("p0slot1");LabelIs("p0slot1name","");LabelIs("p1slot1name","Mine");
            Assert.IsFalse(ui.View.Item("p0choice2").GetComponent<Button>().IsInteractable());ui.Inspect(Command.Choice,3);LabelIs("p0slot1name","");LabelIs("p0rule","Win campaign level 2");
            Click("p0choice0");Assert.IsFalse(ui.View.Item("p0choice0").GetComponent<Button>().IsInteractable());LabelIs("p0slot1name","Mine");LabelIs("p0slot3name","");
            yield return KeyDown(Key.Z);Assert.IsFalse(ui.View.Item("panels").activeInHierarchy);
            yield return Release();yield return null;Assert.IsTrue(ui.View.Item("panels").activeInHierarchy);
            Click("peek");Assert.IsTrue(ui.MapVisible);Click("peek");Assert.IsFalse(ui.MapVisible);
            Click("p0slot1"); Click("localFight");
            yield return Field(BattleMode.LocalPvP);
            var field = Object.FindFirstObjectByType<BattleSessionBootstrap>();
            Vector3 blue = field.BluePosition, red = field.RedPosition;
            yield return KeyDown(Key.D);
            yield return new WaitForSecondsRealtime(.12f);
            yield return Release(); yield return new WaitForFixedUpdate();
            Assert.Greater(field.BluePosition.x, blue.x);
            Assert.That(field.RedPosition.x,Is.EqualTo(red.x).Within(.01f));
            blue = field.BluePosition;
            yield return KeyDown(Key.LeftArrow);
            yield return new WaitForSecondsRealtime(.12f);
            yield return Release(); yield return new WaitForFixedUpdate();
            Assert.Less(field.RedPosition.x,red.x);
            Assert.That(field.BluePosition.x,Is.EqualTo(blue.x).Within(.01f));
            Assert.AreEqual(-1,BattleCards.Card(ui.launch.Context.BlueLoadout,1));
            Assert.AreEqual(1,BattleCards.Card(ui.launch.Context.RedLoadout,1));
            yield return LeaveField();
            Click("skirmish");Action(Command.AI);Click("continue");
            Action(Command.Difficulty);Click("fight");
            yield return Field(BattleMode.BattleVsAI);
            Assert.AreEqual(1,ui.launch.Context.AiDifficulty);
            yield return LeaveField();
        }

        IEnumerator Field(BattleMode mode)
        {
            float limit = Time.realtimeSinceStartup + 15;
            while (!SceneManager.GetSceneByName("Level_01").isLoaded && Time.realtimeSinceStartup < limit) yield return null;
            yield return null;
            Assert.AreEqual(mode,Object.FindFirstObjectByType<BattleSessionBootstrap>().CurrentMode);
            Assert.AreEqual(mode,ui.launch.Context.Mode);
            Assert.AreEqual(SceneManager.GetActiveScene().name,ui.launch.Context.MapId);
            Assert.IsTrue(ui.launch.IsActive && ui.launch.IsAuthority);
            Assert.AreEqual("Level_01",SceneManager.GetActiveScene().name);
            Assert.AreEqual(1,Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1,Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            Assert.AreSame(ui.network.Session.Manager,Object.FindFirstObjectByType<FishNet.Managing.NetworkManager>());
            Assert.AreEqual(1,Object.FindObjectsByType<FishNet.Managing.NetworkManager>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1,Object.FindObjectsByType<SteamRuntime>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1,Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(mode==BattleMode.LocalPvP ? 2 : 1,System.Array.FindAll(Object.FindObjectsByType<ZeppelinLocalInput>(FindObjectsSortMode.None),i=>i.enabled).Length);
            Assert.IsFalse(ui.View.gameObject.activeInHierarchy);
        }
        IEnumerator LeaveField()
        {
            ui.launch.Leave();
            float limit = Time.realtimeSinceStartup + 15;
            while(ui.launch.Busy && Time.realtimeSinceStartup < limit) yield return null;
            PageIs(Page.MainMenu);
            Assert.IsFalse(SceneManager.GetSceneByName("Level_01").isLoaded);
            Assert.IsNull(ui.launch.Context);
            Assert.IsTrue(ui.launch.menuCamera.isActiveAndEnabled);
            Assert.IsTrue(ui.View.gameObject.activeInHierarchy);
        }

        [UnityTest] public IEnumerator SingleHostFishNetSceneMembershipAndOnlineBootstrap()
        {
            if (!ui.network.Steam.IsReady) Assert.Ignore(ui.network.Steam.LastError);
            Click("start"); Click("multiplayer"); Click("create");
            var manager = ui.network.Session.Manager;
            float until = Time.realtimeSinceStartup + 35;
            while (ui.room.View.Lobby == 0 && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(ui.network.Session.IsAuthenticated);
            var lobby = ui.network.Lobby.LobbyId;
            var host = manager.ServerManager.Clients[manager.ClientManager.Connection.ClientId];
            // Isolate scene membership with one actual connection. The absent seat never joins or spawns.
            var fixture = new BattleLaunchContext(BattleMode.OnlinePvP,0,0,1,1,lobby,"scene-membership-test",host.ClientId,host.ClientId+1);
            ui.launch.AcceptOnline(fixture);
            manager.SceneManager.LoadConnectionScenes(host,new FishNet.Managing.Scened.SceneLoadData(BattleLaunchContext.DefaultMap));
            until = Time.realtimeSinceStartup + 30;
            while (!host.Scenes.Contains(SceneManager.GetSceneByName(BattleLaunchContext.DefaultMap)) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(host.Scenes.Contains(SceneManager.GetSceneByName(BattleLaunchContext.DefaultMap)));
            Assert.AreEqual(lobby,ui.network.Lobby.LobbyId);
            Assert.IsTrue(ui.network.Session.IsAuthenticated);
            Assert.AreSame(manager,ui.network.Session.Manager);
            Assert.IsTrue(Object.FindFirstObjectByType<BattleSessionBootstrap>().IsOnline);
            Assert.IsEmpty(Object.FindObjectsByType<ZeppelinLocalInput>(FindObjectsSortMode.None));
            Assert.IsEmpty(Object.FindObjectsByType<ZeppelinNetworkInput>(FindObjectsSortMode.None));
            Assert.IsFalse(ui.launch.IsActive);
            ui.launch.Leave();
            until = Time.realtimeSinceStartup + 30;
            while (ui.launch.Busy && Time.realtimeSinceStartup < until) yield return null;
            PageIs(Page.Multiplayer);
            Assert.IsNull(ui.launch.Context);
            Assert.IsFalse(SceneManager.GetSceneByName(BattleLaunchContext.DefaultMap).isLoaded);
        }

        [UnityTest] public IEnumerator GamepadTitleAndSubmitUseTheProjectUiActions()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.North));
                yield return null; yield return null; PageIs(Page.MainMenu);
                InputSystem.QueueStateEvent(pad,new GamepadState());
                yield return null;
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));
                yield return null; yield return null; PageIs(Page.Campaign);
            }
            finally { InputSystem.RemoveDevice(pad); }
        }

        [UnityTest] public IEnumerator LobbyInputConsumesEscapeBeforePageNavigation()
        {
            Click("start"); Click("multiplayer");
            var field=ui.View.Item("id").GetComponent<TMP_InputField>();
            field.Select(); field.ActivateInputField();
            yield return null; yield return null;
            Assert.IsTrue(field.isFocused);
            yield return KeyDown(Key.Escape); PageIs(Page.Multiplayer);
            yield return Release();
            Click("back"); PageIs(Page.MainMenu);
        }

        [UnityTest] public IEnumerator MouseHoverFocusPressAndHiddenScreensAreIndependent()
        {
            var start = ui.View.Item("start").GetComponent<Button>();
            Assert.AreSame(start, ButtonAt((RectTransform)start.transform));
            Click("start");
            var campaign=ui.View.Item("campaign");
            var skirmish=ui.View.Item("skirmish");
            var focused=campaign.GetComponent<ButtonFeedback>();
            var hovered=skirmish.GetComponent<ButtonFeedback>();
            var mouse=InputSystem.AddDevice<Mouse>();
            try
            {
                var rect=(RectTransform)skirmish.transform;
                var position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                InputSystem.QueueStateEvent(mouse,new MouseState{position=position});
                yield return null; yield return null;
                Assert.IsTrue(hovered.Hovered);
                Assert.IsFalse(hovered.Focused);
                Assert.IsTrue(focused.Focused);
                PageIs(Page.MainMenu);
                var state=new MouseState{position=position}.WithButton(MouseButton.Left);
                InputSystem.QueueStateEvent(mouse,state);
                yield return null; yield return null;
                PageIs(Page.MainMenu);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=position});
                yield return null; yield return null;
                PageIs(Page.Skirmish);
                campaign.GetComponent<Button>().onClick.Invoke();
                PageIs(Page.Skirmish);
                Assert.AreEqual(ui.View.Item("local"),EventSystem.current.currentSelectedGameObject);
                Assert.AreEqual(1,System.Array.FindAll(ui.screens,p=>p.gameObject.activeInHierarchy).Length);
                yield return KeyDown(Key.DownArrow); yield return Release();
                Assert.AreNotEqual(ui.View.Item("local"),EventSystem.current.currentSelectedGameObject);
                yield return KeyDown(Key.Space); PageIs(Page.Map);
                Assert.AreEqual(PreviewMode.AI,ui.Mode);
            }
            finally { InputSystem.RemoveDevice(mouse); }
        }

        [UnityTest] public IEnumerator StandaloneHudRendersExternalDataAndOnlyEmitsIntent()
        {
            var hud=Object.Instantiate(ui.screens[(int)Page.Hud].battleHud,ui.transform);
            var state=new BattleHudState
            {
                gold=79, goldPerMinute=2.5f, shopOpen=true, selectedSlot=2, purchaseEnabled=true,
                friendlyShip=new ShipVitality{current=3,maximum=10},
                opposingShip=new ShipVitality{current=8,maximum=8},
                interaction=new HudInteraction{primary="Talk",primaryEnabled=true},
                objectives=new[]{new HudObjective{label="Example objective",completed=true}}
            };
            state.cards[2]=new HudCard{occupied=true,name="Test card",price="TBD"};
            int requests=0;
            hud.Requested+=(intent,index)=>{Assert.That(intent,Is.EqualTo(HudIntent.Buy));requests++;};
            try
            {
                hud.Present(state);
                Assert.AreEqual("79",hud.gold.text);
                Assert.AreEqual("+2.5/min",hud.production.text);
                Assert.AreEqual("3 / 10",hud.friendly.amount.text);
                Assert.AreEqual(hud.friendly.fullWidth*.3f,hud.friendly.fill.rectTransform.rect.width,.05f);
                Assert.AreEqual("Test card",hud.slots[2].title.text);
                Assert.IsTrue(hud.slots[2].selection.activeSelf);
                Assert.IsFalse(hud.slots[0].button.interactable);
                hud.buy.onClick.Invoke();
                Assert.AreEqual(1,requests); Assert.AreEqual(79,state.gold);
                Assert.AreEqual("Example objective",hud.objectives[0].label.text);
                state.shopOpen=false;hud.Present(state);
                Assert.IsFalse(hud.shopGroup.activeSelf);Assert.AreEqual("Talk",hud.primaryLabel.text);
                state.outcome=MatchOutcome.Defeat;hud.Present(state);
                Assert.IsTrue(hud.result.gameObject.activeSelf);Assert.AreEqual("DEFEAT",hud.result.text);
                Assert.IsFalse(hud.interactionGroup.activeSelf);
                Assert.IsEmpty(hud.GetComponentsInChildren<MenuCommand>(true));
            }
            finally { Object.Destroy(hud.gameObject); }
            yield return null;
        }

        [UnityTest] public IEnumerator SteamUnavailableKeepsOfflineMenusUsable()
        {
            ui.network.Steam.Shutdown();
            Click("start");Click("multiplayer");LabelIs("steamState","STEAM OFFLINE");
            Assert.IsFalse(ui.View.Item("create").GetComponent<Button>().IsInteractable());
            Assert.IsTrue(ui.View.Item("retry").activeInHierarchy);
            Click("back");Click("campaign");Click("fight");PageIs(Page.Mission);
            Click("fight");Click("advance");Click("advance");
            yield return Field(BattleMode.Campaign);
            Assert.IsFalse(ui.network.Steam.IsReady);
            yield return LeaveField();
        }

        [UnityTest] public IEnumerator LostMembershipClosesOnlinePreviewAndPreservesOfflineNavigation()
        {
            if (!ui.network.Steam.IsReady) Assert.Ignore(ui.network.Steam.LastError);
            Click("start"); Click("multiplayer"); Click("create");
            float until=Time.realtimeSinceStartup+30;
            while(ui.Current!=Page.Lobby && Time.realtimeSinceStartup<until) yield return null;
            PageIs(Page.Lobby); Action(Command.Settings);
            PageIs(Page.Settings);
            ui.network.LeaveLobby();
            yield return null; yield return null;
            PageIs(Page.Multiplayer);
            Assert.That(ui.View.Item("error").GetComponent<TMP_Text>().text,Does.Contain("ended"));
            Click("back"); Click("campaign"); PageIs(Page.Campaign);
        }

        [UnityTest] public IEnumerator RealSteamButtonsValidateIdCreateCopyAndLeave()
        {
            if (!ui.network.Steam.IsReady) Assert.Ignore(ui.network.Steam.LastError);
            Click("start");Click("multiplayer");
            var id=ui.View.Item("id").GetComponent<TMP_InputField>();
            id.text="1";Click("join");LabelIs("error","Enter a valid Steam lobby ID.");
            Click("create");
            float until=Time.realtimeSinceStartup+35;
            while(ui.Current!=Page.Lobby && Time.realtimeSinceStartup<until) yield return null;
            PageIs(Page.Lobby);Assert.IsTrue(ui.network.Lobby.IsOwner);
            Assert.IsTrue(ui.View.Item("invite").GetComponent<Button>().IsInteractable());
            Assert.IsFalse(ui.View.Item("startMatch").GetComponent<Button>().IsInteractable());
            Click("invite");
            if (ui.network.Lobby.LastError.Contains("Overlay"))
                LabelIs("lobbyNotice","Enable Steam Overlay and launch through Steam.");
            string oldClipboard=GUIUtility.systemCopyBuffer;
            try { Click("copy");Assert.AreEqual(ui.network.Lobby.LobbyId.ToString(),GUIUtility.systemCopyBuffer); }
            finally { GUIUtility.systemCopyBuffer=oldClipboard; }
            yield return new WaitUntil(()=>ui.room.View.Lobby!=0);
            Click("ready"); yield return new WaitUntil(()=>ui.room.LocalReady);
            Assert.IsFalse(ui.room.CanStart);
            ui.room.Submit(0,true);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(BattleCards.Starters,ui.room.View.BlueCards);
            Assert.IsTrue(ui.room.LocalReady);
            ui.room.Submit(1,true);
            yield return new WaitUntil(()=>ui.room.View.BlueCards == 1);
            Assert.IsFalse(ui.room.LocalReady);
            ui.room.Submit(BattleCards.Starters,false);
            yield return new WaitUntil(()=>ui.room.View.BlueCards == BattleCards.Starters);
            Click("ready"); yield return new WaitUntil(()=>ui.room.LocalReady);
            ui.room.StartMatch(); ui.room.StartMatch(); Assert.IsFalse(ui.launch.Busy);
            ui.Back(); yield return new WaitUntil(()=>!ui.room.LocalReady);
            Click("hostMap"); PageIs(Page.Map); Click("continue"); PageIs(Page.Lobby);
            Click("back");
            PageIs(Page.Multiplayer);Assert.IsFalse(ui.network.Lobby.InLobby);
        }
    }
}
