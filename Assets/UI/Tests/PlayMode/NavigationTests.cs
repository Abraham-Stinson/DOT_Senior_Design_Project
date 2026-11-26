using System;
using System.Collections;
using Game.Core;
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
            MatchLaunchConfig.Reset();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded)
            {
                Scene teardownScene = SceneManager.CreateScene("NavigationTestsTeardown_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(teardownScene);
                yield return SceneManager.UnloadSceneAsync(active);
            }
            yield return null;
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
            Click("advance");PageIs(Page.Hud);
            Assert.IsFalse(ui.screens[(int)Page.Shop].gameObject.activeInHierarchy);
            Click("interact");PageIs(Page.Shop);Click("buy");
            LabelIs("shopNotice","Purchase preview only • no gold spent");LabelIs("gold",ui.catalog.previewGold.ToString());
            ui.Back();PageIs(Page.Hud);ui.Back();PageIs(Page.Pause);
            Action(Command.Settings);ui.Back();Click("resume");PageIs(Page.Hud);
            Action(Command.Win);Assert.IsFalse(ui.View.Item("reward").activeInHierarchy);
            Click("continue");Assert.AreEqual(2,ui.UnlockedLevel);
            Click("node1");Assert.AreEqual(2,ui.SelectedLevel);Assert.IsFalse(ui.View.Item("node2").GetComponent<Button>().IsInteractable());
            Click("fight");LabelIs("stagePreview","SHIELD PIRATE");
            Assert.IsFalse(ui.View.Item("p0choice2").GetComponent<Button>().IsInteractable());
            Click("fight");Click("advance");Click("advance");
            Action(Command.Win);Assert.IsTrue(ui.View.Item("reward").activeInHierarchy);
            Action(Command.Replay);Click("advance");Click("advance");Action(Command.Win);
            Assert.IsFalse(ui.View.Item("reward").activeInHierarchy);
            Action(Command.Replay);Click("advance");Click("advance");Action(Command.Lose);
            Click("retry");PageIs(Page.Dialogue);yield return null;
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
            ui.launchLiveArenaForLocalPvP = false;
            Click("localFight");PageIs(Page.Hud);Assert.IsFalse(ui.View.Item("objectives").activeInHierarchy);
            Action(Command.Win);Assert.IsFalse(ui.View.Item("stars").activeInHierarchy);
            Click("continue");PageIs(Page.Skirmish);Action(Command.AI);Click("continue");
            Action(Command.Difficulty);LabelIs("difficulty","AI — NORMAL");Click("fight");PageIs(Page.Hud);
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
            Click("back");Click("campaign");Click("fight");PageIs(Page.Mission);yield return null;
        }

        [UnityTest] public IEnumerator LostMembershipClosesOnlinePreviewAndPreservesOfflineNavigation()
        {
            if (!ui.network.Steam.IsReady) Assert.Ignore(ui.network.Steam.LastError);
            Click("start"); Click("multiplayer"); Click("create");
            float until=Time.realtimeSinceStartup+30;
            while(ui.Current!=Page.Lobby && Time.realtimeSinceStartup<until) yield return null;
            PageIs(Page.Lobby); Click("onlinePreview"); ui.Back(); Action(Command.Settings);
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
            Click("ready");Assert.IsTrue(ui.ReadyPreview);ui.Back();Assert.IsFalse(ui.ReadyPreview);
            Click("hostMap");PageIs(Page.Map);Assert.IsTrue(ui.View.Item("hostPreview").activeInHierarchy);
            Click("continue"); Click("onlinePreview"); PageIs(Page.Hud);
            ui.Back(); PageIs(Page.Pause); LabelIs("leaveCaption","RETURN TO LOBBY");
            Click("resume"); Action(Command.Lose); LabelIs("destination","RETURN TO LOBBY");
            Assert.IsFalse(ui.View.Item("replay").activeInHierarchy);
            Click("continue"); PageIs(Page.Lobby); Click("back");
            PageIs(Page.Multiplayer);Assert.IsFalse(ui.network.Lobby.InLobby);
        }

        [UnityTest] public IEnumerator LocalPvPFightLaunchesCannonBrawlArena()
        {
            Assert.IsTrue(ui.launchLiveArenaForLocalPvP);
            Click("start"); Click("skirmish"); Click("local"); PageIs(Page.Map);
            Click("continue"); PageIs(Page.Local);
            Click("joinSecond"); Assert.IsTrue(ui.SecondJoined);
            Click("localFight");

            float until = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != GameSceneNames.CannonBrawlArena && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSceneNames.CannonBrawlArena));
            Assert.That(MatchLaunchConfig.HasExplicitSelection, Is.True);
            Assert.That(MatchLaunchConfig.SelectedMode, Is.EqualTo(MatchMode.LocalPvP));
            Assert.That(MatchLaunchConfig.HasAnyPresetLoadout, Is.True);
            yield return null;
        }
    }
}
