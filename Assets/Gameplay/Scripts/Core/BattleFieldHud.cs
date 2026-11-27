using PirateGame.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace PirateGame.Gameplay
{
    public sealed class BattleFieldHud : MonoBehaviour
    {
        public BattleHud hud;
        public Button resume, leave;
        public GameObject pausePanel;
        private BattleLaunchService session;
        private BattleSessionBootstrap field;
        private void Start()
        {
            session = FindFirstObjectByType<BattleLaunchService>();
            if (!session || !session.Busy) { gameObject.SetActive(false); return; }
            field = FindFirstObjectByType<BattleSessionBootstrap>();
            var ctx = session.Context;
            var hand = ctx.Mode == BattleMode.OnlinePvP && !session.network.Session.IsServer ? ctx.RedLoadout : ctx.BlueLoadout;
            var state = new BattleHudState();
            string[] names = {"Territory Balloon","Mining Camp","Cannon Tower"};
            for (int i = 0; i < 5; i++)
            {
                int card = BattleCards.Card(hand,i);
                if (card >= 0) state.cards[i] = new HudCard { occupied = true, name = names[card], fallbackGlyph = card+1 };
            }
            hud.Present(state);
            hud.gold.text = hud.production.text = hud.friendly.amount.text = hud.enemy.amount.text = "—";
            hud.Requested += Request;
            resume.onClick.AddListener(() => Pause(false));
            leave.onClick.AddListener(session.Leave);
            pausePanel.SetActive(false);
            hud.pause.Select();
        }
        void Request(HudIntent action, int slot) { if (action == HudIntent.Pause) Pause(true); }
        void Update()
        {
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) Pause(!pausePanel.activeSelf);
        }
        void Pause(bool open)
        {
            if (!session.Busy) return;
            pausePanel.SetActive(open);
            hud.gameObject.SetActive(!open);
            if (session.Context.Mode != BattleMode.OnlinePvP) field.SetOfflinePaused(open);
            else foreach (var actor in FindObjectsByType<ZeppelinNetworkInput>(FindObjectsSortMode.None))
                if (actor.IsOwner) actor.SuppressLocalInput = open;
            (open ? resume : hud.pause).Select();
        }
        void OnDestroy() { if (hud) hud.Requested -= Request; }
    }
}