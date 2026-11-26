using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateGame.UI
{
    public enum Command
    {
        Start, Campaign, Skirmish, Multiplayer, Settings, Credits, Quit, CancelQuit, ConfirmQuit, Back,
        Node, Prepare, Local, AI, ContinueMap, JoinSecond, Difficulty, Fight, Peek, Slot, Choice,
        Create, Join, RetrySteam, Copy, Invite, HostMap, Ready, Edit, Advance, Interact, Pick, Buy,
        CloseShop, Pause, Resume, LeaveMatch, Win, Lose, ContinueResult, Replay, Tab, ResetSettings, ApplySettings, Fullscreen, LeaveLobby, OnlinePreview, MainMenu
    }
    [RequireComponent(typeof(Button))]
    public sealed class MenuCommand : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Command command;
        public int argument;
        private ScreenRouter router;
        void Awake() { router = GetComponentInParent<ScreenRouter>(); GetComponent<Button>().onClick.AddListener(Run); }
        private void Run() { if (isActiveAndEnabled && router && GetComponent<Button>().IsInteractable()) router.Execute(command, argument); }
        public void OnSelect(BaseEventData data) => router?.Inspect(command, argument);
        public void OnPointerExit(PointerEventData data)
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected && selected.activeInHierarchy && selected.TryGetComponent<MenuCommand>(out var focused))
                router?.Inspect(focused.command, focused.argument);
        }
        public void OnPointerEnter(PointerEventData data) => router?.Inspect(command, argument);
    }
}