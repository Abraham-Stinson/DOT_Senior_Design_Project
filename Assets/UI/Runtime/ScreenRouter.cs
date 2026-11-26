using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PirateGame.Networking;

namespace PirateGame.UI
{
    public sealed class ScreenRouter : MonoBehaviour
    {
        public ScreenView[] screens;
        public PreviewCatalog catalog;
        public ButtonTheme theme;
        public OnlineSessionCoordinator network;
        public bool launchLiveArenaForLocalPvP = true;
        public Page Current { get; private set; }
        public ScreenView View => screens[(int)Current];
        public PreviewMode Mode { get; private set; }
        public bool QuitOpen { get; private set; }
        public bool SecondJoined { get; private set; }
        public int SelectedLevel { get; private set; } = 1;
        private readonly CampaignPreviewProgress progress = new();
        public int UnlockedLevel => progress.AccessibleLevel;
        private BattlePreviewPresenter battlePreview;
        private bool DevelopmentPreview => Application.isEditor || Debug.isDebugBuild;
        public bool MapVisible => peeking || held;
        public bool ReadyPreview { get; private set; }
        private bool showReward, peeking, held, titlePressed, focusedField, previouslyFocused, stagedFullscreen;
        private int difficulty, dialogue, shopSlot, tab, switchedFrame;
        private Page settingsBack, pauseBack;
        private ulong observedLobby;
        private float refreshAt;
        private Vector2 markerStart, speechStart, captionStart;
        private IDisposable anyButton;
        private LoadoutPresenter loadouts;
        private LobbyPresenter online;

        void Awake()
        {
            online = new LobbyPresenter(network);
            loadouts = new LoadoutPresenter(catalog);
            battlePreview = new BattlePreviewPresenter(catalog, loadouts);
            foreach (var screen in screens)
                if (screen.battleHud) screen.battleHud.Requested += HandleHudIntent;
            markerStart = Rect(Page.Campaign,"marker").anchoredPosition;
            speechStart = Rect(Page.Dialogue,"speech").anchoredPosition;
            captionStart = Rect(Page.Dialogue,"caption").anchoredPosition;
            Go(Page.Title);
        }
        void OnDestroy()
        {
            foreach (var screen in screens)
                if (screen && screen.battleHud) screen.battleHud.Requested -= HandleHudIntent;
        }
        void HandleHudIntent(HudIntent intent, int slot)
        {
            switch (intent)
            {
                case HudIntent.Interact: Execute(Command.Interact); break;
                case HudIntent.SelectCard: Execute(Command.Pick, slot); Focus("buy"); break;
                case HudIntent.Buy: Execute(Command.Buy); break;
                case HudIntent.LeaveShop: Execute(Command.CloseShop); break;
                case HudIntent.Pause: Execute(Command.Pause); break;
            }
        }
        void OnEnable() => anyButton = InputSystem.onAnyButtonPress.Call(_ => { if (Current == Page.Title) titlePressed = true; });
        void OnDisable() { anyButton?.Dispose(); titlePressed = false; }
        static bool Prep(Page p) => p == Page.Local || p == Page.AI || p == Page.Mission || p == Page.Lobby;
        RectTransform Rect(Page page, string key) => screens[(int)page].Item(key).GetComponent<RectTransform>();
        void Focus(string key)
        {
            var item = View.Item(key);
            if (item && item.activeInHierarchy && item.TryGetComponent<Selectable>(out var selectable) && selectable.IsInteractable())
            {
                selectable.Select();
                return;
            }
            foreach (var candidate in View.GetComponentsInChildren<Selectable>())
                if (candidate.IsInteractable()) { candidate.Select(); return; }
        }
        void Go(Page page)
        {
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            Current = page;
            switchedFrame = Time.frameCount;
            held = peeking = false;
            foreach (var screen in screens) screen.gameObject.SetActive(screen.page == page);
            if (page == Page.Lobby) SyncLocalOnlinePresetToLaunchConfig();
            Refresh();
            var first = page switch
            {
                Page.MainMenu => "campaign", Page.Campaign or Page.Mission or Page.AI => "fight",
                Page.Skirmish => "local", Page.Map or Page.Victory or Page.Defeat => "continue", Page.Lobby => ReadyPreview ? "edit" : "ready",
                Page.Local => SecondJoined ? "localFight" : "joinSecond", Page.Multiplayer => network.Steam.IsReady ? "create" : "retry",
                Page.Dialogue => "advance", Page.Hud => "interact", Page.Shop => "shopslot0", Page.Pause => "resume",
                Page.Settings => "tab0", Page.Credits => "back", _ => "start"
            };
            Focus(first);
        }
        void Start()
        {
            if (Current == Page.Title && EventSystem.current && !EventSystem.current.currentSelectedGameObject) Focus("start");
        }
        void Update()
        {
            var field = screens[(int)Page.Multiplayer].Item("id").GetComponent<TMP_InputField>();
            // TMP can consume Escape before our Update. Keep the focus from the previous frame as well.
            bool editing = Current == Page.Multiplayer && field.isFocused;
            focusedField = editing || previouslyFocused;
            previouslyFocused = editing;
            ulong id = online.LobbyId;
            if (id != observedLobby && Current != Page.Title)
            {
                bool lost = observedLobby != 0 && id == 0;
                observedLobby = id;
                if (id != 0) { QuitOpen = false; Mode = PreviewMode.Online; ReadyPreview = false; Go(Page.Lobby); }
                else if (lost && Mode == PreviewMode.Online)
                { ReadyPreview = false; MatchLaunchConfig.Reset(); online.Lost(); Go(Page.Multiplayer); }
            }
            if ((Current == Page.Lobby || Current == Page.Multiplayer) && Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .2f;
                online.Render(View);
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (!selected || !selected.activeInHierarchy ||
                    selected.TryGetComponent<Selectable>(out var control) && !control.IsInteractable())
                    Focus(Current == Page.Lobby ? "copy" : network.Steam.IsReady ? "create" : "retry");
            }
        }
        void LateUpdate()
        {
            if (Current == Page.Title && titlePressed) { titlePressed = false; Execute(Command.Start); return; }
            if (switchedFrame == Time.frameCount || focusedField) return;
            var k = Keyboard.current; var pad = Gamepad.current;
            if (k?.escapeKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true) { Back(); return; }
            if (Prep(Current))
            {
                bool down = k?.zKey.isPressed == true;
                if (held != down) { held = down; SetPanelsVisible(); Focus("peek"); }
            }
            if (Current == Page.Campaign && k != null)
            {
                if (k.leftArrowKey.wasPressedThisFrame) SelectLevel(SelectedLevel-1);
                if (k.rightArrowKey.wasPressedThisFrame) SelectLevel(SelectedLevel+1);
            }
            if (Current == Page.Shop && k?.xKey.wasPressedThisFrame == true) { Go(Page.Hud); return; }
            if (k?.spaceKey.wasPressedThisFrame != true) return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected && selected.activeInHierarchy && selected.transform.IsChildOf(View.transform))
                ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }
        public void Inspect(Command command, int arg)
        {
            if (!Prep(Current) || ReadyPreview && Current == Page.Lobby) return;
            int player = arg / 10;
            if (command != Command.Slot && command != Command.Choice) return;
            loadouts.Inspect(player, command == Command.Slot ? loadouts.Equipped(player,arg%10) : arg%10);
            loadouts.Render(View,player,player == 1 ? 0 : progress.LastVictory,true);
        }

        public void Execute(Command command, int arg = 0)
        {
            if (QuitOpen && command != Command.CancelQuit && command != Command.ConfirmQuit && command != Command.Back) return;
            switch (command)
            {
                case Command.Start: if (Current == Page.Title) Go(Page.MainMenu); break;
                case Command.Campaign: Mode = PreviewMode.Campaign; Go(Page.Campaign); break;
                case Command.Skirmish: Go(Page.Skirmish); break;
                case Command.Multiplayer: Go(online.LobbyId == 0 ? Page.Multiplayer : Page.Lobby); break;
                case Command.Credits: Go(Page.Credits); break;
                case Command.Quit: QuitOpen = true; Refresh(); Focus("cancel"); break;
                case Command.CancelQuit: QuitOpen = false; Refresh(); Focus("quit"); break;
                case Command.ConfirmQuit:
                    if (!QuitOpen) return;
                    network.ShutdownSession(); Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    break;
                case Command.Back: Back(); break;
                case Command.Node: if (Current == Page.Campaign) SelectLevel(arg+1); break;
                case Command.Prepare: Go(Page.Mission); break;
                case Command.Local: Mode = PreviewMode.Local; SecondJoined = false; Go(Page.Map); break;
                case Command.AI: Mode = PreviewMode.AI; Go(Page.Map); break;
                case Command.ContinueMap: Go(Mode == PreviewMode.Online ? Page.Lobby : Mode == PreviewMode.AI ? Page.AI : Page.Local); break;
                case Command.JoinSecond: SecondJoined = true; Refresh(); Focus("localFight"); break;
                case Command.Difficulty: difficulty = (difficulty+1)%3; Refresh(); break;
                case Command.Fight:
                    if (Mode == PreviewMode.Online)
                    {
                        SyncLocalOnlinePresetToLaunchConfig();
                        if (!online.TryStartOnlineMatch())
                        {
                            Refresh();
                        }
                        break;
                    }
                    if (Mode == PreviewMode.Local)
                    {
                        if (!SecondJoined) break;
                        if (launchLiveArenaForLocalPvP && Application.CanStreamedLevelBeLoaded(GameSceneNames.CannonBrawlArena))
                        {
                            MatchLaunchConfig.Reset();
                            MatchLaunchConfig.SelectMode(MatchMode.LocalPvP);
                            MatchLaunchConfig.SetPresetLoadout(
                                0,
                                MatchLoadoutHandoffResolver.MapPreviewLoadoutToGameplayIds(loadouts.CopyEquippedPreviewIndices(0)));
                            MatchLaunchConfig.SetPresetLoadout(
                                1,
                                MatchLoadoutHandoffResolver.MapPreviewLoadoutToGameplayIds(loadouts.CopyEquippedPreviewIndices(1)));
                            SceneManager.LoadScene(GameSceneNames.CannonBrawlArena, LoadSceneMode.Single);
                            break;
                        }
                    }
                    BeginPreview();
                    break;
                case Command.Peek: peeking = !peeking; SetPanelsVisible(); Focus("peek"); break;
                case Command.Slot:
                    loadouts.Slot(arg/10,arg%10);
                    if (Current == Page.Lobby) SyncLocalOnlinePresetToLaunchConfig();
                    Refresh();
                    break;
                case Command.Choice:
                    int player = arg / 10;
                    if (loadouts.Choice(player, arg % 10, player == 1 ? 0 : progress.LastVictory, out int slot))
                    {
                        if (Current == Page.Lobby) SyncLocalOnlinePresetToLaunchConfig();
                        Refresh();
                        Focus("p" + player + "slot" + slot);
                    }
                    break;
                case Command.Create: online.Create(); Refresh(); break;
                case Command.Join: online.Join(View.Item("id").GetComponent<TMP_InputField>().text); Refresh(); break;
                case Command.LeaveLobby: ExitLobby(); break;
                case Command.Copy: online.Copy(); Refresh(); break;
                case Command.Invite: online.Invite(); Refresh(); break;
                case Command.RetrySteam: online.Retry(); Refresh(); break;
                case Command.HostMap: if (online.Host) Go(Page.Map); break;
                case Command.Ready: if (online.LobbyId != 0) { ReadyPreview=true; SyncLocalOnlinePresetToLaunchConfig(); Refresh(); Focus("edit"); } break;
                case Command.Edit: ReadyPreview=false; Refresh(); Focus("ready"); break;
                case Command.Advance:
                    if (++dialogue >= catalog.dialogue.Length) Go(Page.Hud);
                    else { Refresh(); Focus("advance"); }
                    break;
                case Command.Interact: shopSlot=0; Go(Page.Shop); break;
                case Command.Pick: shopSlot=arg; Refresh(); break;
                case Command.Buy: View.Label("shopNotice",loadouts.Equipped(0,shopSlot)<0 ? "Empty slot" : "Purchase preview only • no gold spent"); break;
                case Command.CloseShop: Go(Page.Hud); break;
                case Command.Pause: pauseBack=Current; Go(Page.Pause); break;
                case Command.Resume: Go(pauseBack); break;
                case Command.LeaveMatch: case Command.ContinueResult: ReturnToMode(); break;
                case Command.OnlinePreview:
                    if (Current == Page.Lobby && online.LobbyId != 0 && DevelopmentPreview) BeginPreview();
                    break;
                case Command.MainMenu:
                    if (online.LobbyId != 0) { observedLobby = 0; online.Leave(); }
                    MatchLaunchConfig.Reset();
                    ReadyPreview = false;
                    Go(Page.MainMenu); break;
                case Command.Win:
                    if (Current != Page.Hud || !DevelopmentPreview) break;
                    showReward = Mode == PreviewMode.Campaign && progress.RecordPreviewWin(SelectedLevel) && SelectedLevel == 2;
                    Go(Page.Victory); break;
                case Command.Lose: if (Current == Page.Hud && DevelopmentPreview) Go(Page.Defeat); break;
                case Command.Replay: if (Mode != PreviewMode.Online) BeginPreview(); break;
                case Command.Settings:
                    settingsBack=Current; tab=0; stagedFullscreen=Screen.fullScreen;
                    screens[(int)Page.Settings].Item("master").GetComponent<Slider>().SetValueWithoutNotify(AudioListener.volume);
                    Go(Page.Settings); break;
                case Command.Tab: tab=arg; Refresh(); Focus("tab" + arg); break;
                case Command.Fullscreen: stagedFullscreen=!stagedFullscreen; Refresh(); break;
                case Command.ResetSettings:
                    if (tab == 0) View.Item("master").GetComponent<Slider>().value=1;
                    if (tab == 1) stagedFullscreen=false;
                    Refresh(); View.Label("settingsNotice", "Default value selected"); break;
                case Command.ApplySettings:
                    if (tab==0) AudioListener.volume=View.Item("master").GetComponent<Slider>().value;
                    if (tab==1 && !Application.isEditor) Screen.fullScreen=stagedFullscreen;
                    View.Label("settingsNotice", tab == 0 ? "Master volume applied" : "Display mode applied");
                    break;
            }
        }
        void ExitLobby()
        {
            observedLobby = 0;
            ReadyPreview = false;
            MatchLaunchConfig.Reset();
            online.Leave();
            Go(Page.Multiplayer);
        }
        void SyncLocalOnlinePresetToLaunchConfig()
        {
            if (loadouts == null)
            {
                return;
            }

            ushort[] mapped = MatchLoadoutHandoffResolver.MapPreviewLoadoutToGameplayIds(
                loadouts.CopyEquippedPreviewIndices(0));
            MatchLaunchConfig.SelectMode(MatchMode.OnlinePvP);
            MatchLaunchConfig.SetLocalOnlinePresetLoadout(mapped);
        }
        void SetPanelsVisible()
        {
            View.Show("panels", !MapVisible);
            if (Current == Page.Lobby) View.Show("onlinePreview", DevelopmentPreview && !MapVisible);
        }
        void SelectLevel(int value)
        {
            if (value < 1 || value > UnlockedLevel) { Focus("fight"); return; }
            SelectedLevel=value; Refresh(); Focus("fight");
        }
        void BeginPreview() { dialogue=0; Go(Mode==PreviewMode.Campaign ? Page.Dialogue : Page.Hud); }
        void ReturnToMode()
        {
            if (Mode == PreviewMode.Online) Go(online.LobbyId != 0 ? Page.Lobby : Page.Multiplayer);
            else Go(Mode == PreviewMode.Campaign ? Page.Campaign : Page.Skirmish);
        }
        public void Back()
        {
            if (QuitOpen) { Execute(Command.CancelQuit); return; }
            if (MapVisible) { peeking=held=false; SetPanelsVisible(); Focus("peek"); return; }
            switch (Current)
            {
                case Page.MainMenu: Go(Page.Title); break;
                case Page.Campaign: case Page.Skirmish: case Page.Credits: Go(Page.MainMenu); break;
                case Page.Multiplayer: online.Leave(); Go(Page.MainMenu); break;
                case Page.Map: Go(Mode == PreviewMode.Online ? Page.Lobby : Page.Skirmish); break;
                case Page.Local: case Page.AI: Go(Page.Map); break;
                case Page.Mission: Go(Page.Campaign); break;
                case Page.Lobby:
                    if (ReadyPreview) { Execute(Command.Edit); break; }
                    ExitLobby(); break;
                case Page.Dialogue: Go(Page.Mission); break;
                case Page.Hud: Execute(Command.Pause); break;
                case Page.Shop: Go(Page.Hud); break;
                case Page.Pause: Go(pauseBack); break;
                case Page.Settings: Go(settingsBack); break;
                case Page.Victory: case Page.Defeat: ReturnToMode(); break;
            }
        }

        void Refresh()
        {
            var v=View;
            if (Prep(Current))
            {
                v.Show("panels",!MapVisible);
                loadouts.Render(v,0,progress.LastVictory,Current != Page.Lobby || !ReadyPreview);
                if (Current == Page.Local)
                {
                    v.Show("right",!SecondJoined); v.Show("p1panel",SecondJoined); v.Show("localFight",SecondJoined);
                    if (SecondJoined) loadouts.Render(v,1,0,true);
                }
            }
            switch (Current)
            {
                case Page.MainMenu:
                    var menu = v.Item("menu").GetComponent<CanvasGroup>();
                    menu.interactable = menu.blocksRaycasts = !QuitOpen;
                    v.Show("quitModal",QuitOpen); break;
                case Page.Campaign:
                    for (int i=0;i<10;i++) v.Enable("node"+i,i<UnlockedLevel);
                    Rect(Current,"marker").anchoredPosition=markerStart+Rect(Current,"node"+(SelectedLevel-1)).anchoredPosition-Rect(Current,"node0").anchoredPosition;
                    int stars = progress.Stars;
                    v.Label("stars",$"{stars} / {catalog.plannedStars}");
                    Rect(Current,"progress").SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,Mathf.Max(4,912f*stars/catalog.plannedStars));
                    PaintRating(v.Item("rating"), progress.StarsFor(SelectedLevel));
                    for (int i = 0; i < 10; i++)
                    {
                        var node = v.Item("node" + i);
                        bool selected = SelectedLevel == i + 1;
                        node.GetComponentInChildren<WireframeShape>().color = selected ? theme.activeFace : theme.inactiveFace;
                        node.GetComponentInChildren<TMP_Text>().color = selected ? theme.activeText : theme.inactiveText;
                    }
                    goto case Page.Mission;
                case Page.Mission:
                    var stage = catalog.stages[SelectedLevel - 1];
                    v.Label("level", "LEVEL " + SelectedLevel);
                    v.Label("levelName", stage.title);
                    v.Label("stagePreview", stage.previewCaption);
                    for (int i = 0; i < stage.objectives.Length; i++) v.Label("goal" + i, stage.objectives[i]);
                    break;
                case Page.AI: v.Label("difficulty","AI — "+new[]{"EASY","NORMAL","HARD"}[difficulty]); break;
                case Page.Lobby:
                    v.Show("onlinePreview", DevelopmentPreview);
                    v.Item("confirmed").GetComponent<ButtonFeedback>().SetConfirmed(ReadyPreview);
                    v.Show("readyModal",ReadyPreview); v.Show("ready",!ReadyPreview);
                    
                    goto case Page.Multiplayer;
                case Page.Multiplayer: online.Render(v); break;
                case Page.Map: v.Show("hostPreview",Mode == PreviewMode.Online); break;
                case Page.Dialogue:
                    v.Label("line",catalog.dialogue[dialogue]);
                    Rect(Current,"speech").anchoredPosition=speechStart+new Vector2(dialogue%2==0?0:-307,0)*2;
                    v.Label("speaker",dialogue%2==0?"PIRATE CAPTAIN":"PRINCE");
                    var caption=Rect(Current,"caption");
                    caption.anchoredPosition = captionStart + (dialogue%2==0 ? Vector2.zero : Vector2.left*556);
                    v.Show("captainName",dialogue%2!=0);
                    break;
                case Page.Shop: case Page.Hud:
                    v.Show("previewResults", DevelopmentPreview);
                    battlePreview.Display(v.battleHud, Current == Page.Shop, shopSlot, Mode == PreviewMode.Campaign ? SelectedLevel : 0);
                    v.Label("shopNotice","SHIP INTERACTION PREVIEW");
                    break;
                case Page.Pause:
                    v.Label("pauseNote", Mode == PreviewMode.Online ? "Online session continues • local menu" : "Offline preview paused");
                    v.Label("leaveCaption", Mode == PreviewMode.Online ? "RETURN TO LOBBY" : "LEAVE MATCH");
                    break;
                case Page.Victory: case Page.Defeat:
                    v.Label("resultLevel",Mode == PreviewMode.Campaign ? $"LEVEL {SelectedLevel} — {catalog.stages[SelectedLevel-1].title}" : Mode == PreviewMode.Online ? "ONLINE UI PREVIEW" : "SKIRMISH PREVIEW");
                    v.Show("stars",Mode==PreviewMode.Campaign); v.Show("reward",showReward && Mode==PreviewMode.Campaign);
                    v.Show("replay", Mode != PreviewMode.Online);
                    v.Label("destination", Mode == PreviewMode.Online ? "RETURN TO LOBBY" : Current == Page.Victory ? "CONTINUE" : Mode == PreviewMode.Campaign ? "BACK TO MAP" : "BACK TO SKIRMISH");
                    break;
                case Page.Settings:
                    v.Show("audio",tab==0); v.Show("display",tab==1); v.Show("controls",tab==2);
                    v.Enable("reset",tab!=2); v.Enable("apply",tab==0 || tab==1 && !Application.isEditor);
                    v.Label("settingsNotice", tab == 1 && Application.isEditor ? "Apply display changes in the Windows player" : "");
                    for (int i=0;i<3;i++)
                    {
                        v.Item("tabFace"+i).GetComponent<Image>().color=i==tab ? theme.activeFace : theme.inactiveFace;
                        v.Item("tabText"+i).GetComponent<TMP_Text>().color=i==tab ? theme.activeText : theme.inactiveText;
                    }
                    v.Label("mode",stagedFullscreen?"Fullscreen":"Windowed"); break;
            }
        }
        static void PaintRating(GameObject row,int count)
        {
            int i=0;
            foreach (var star in row.GetComponentsInChildren<WireframeShape>()) star.color=i++<count ? Color.gray : Color.clear;
        }
    }
}
