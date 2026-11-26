using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.UI
{
    [Serializable]
    public sealed class HudCardView
    {
        public Button button;
        public TMP_Text title, price;
        public Image icon;
        public GameObject[] fallbackIcons;
        public GameObject empty, tile, selection;
    }
    [Serializable]
    public sealed class ShipBarView
    {
        public TMP_Text amount;
        public Image fill;
        public float fullWidth;
        public void Display(ShipVitality health)
        {
            amount.text = $"{health.current} / {health.maximum}";
            fill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fullWidth * health.Fraction);
        }
    }
    [Serializable]
    public sealed class ObjectiveView
    {
        public GameObject row;
        public TMP_Text label;
        public WireframeShape star;
    }
    public sealed class BattleHud : MonoBehaviour
    {
        public TMP_Text gold, production, result;
        public ShipBarView friendly, enemy;
        public GameObject interactionGroup, shopGroup, goalsGroup;
        public Button primary, secondary, leaveInteraction, buy, leaveShop, pause;
        public TMP_Text primaryLabel, secondaryLabel, leaveLabel;
        public HudCardView[] slots;
        public ObjectiveView[] objectives;
        public event Action<HudIntent, int> Requested;

        void Awake()
        {
            Bind(primary, HudIntent.Interact);
            Bind(secondary, HudIntent.SecondaryInteraction);
            Bind(leaveInteraction, HudIntent.LeaveInteraction);
            Bind(buy, HudIntent.Buy);
            Bind(leaveShop, HudIntent.LeaveShop);
            Bind(pause, HudIntent.Pause);
            for (int i = 0; i < slots.Length; i++) Bind(slots[i].button, HudIntent.SelectCard, i);
        }
        void Bind(Button control, HudIntent intent, int index = -1)
        {
            control.onClick.AddListener(() =>
            {
                if (isActiveAndEnabled && control.isActiveAndEnabled && control.IsInteractable()) Requested?.Invoke(intent, index);
            });
        }
        public void Present(BattleHudState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            gold.text = string.IsNullOrEmpty(state.goldTextOverride)
                ? state.gold.ToString(CultureInfo.InvariantCulture)
                : state.goldTextOverride;
            production.text = string.IsNullOrEmpty(state.productionTextOverride)
                ? state.goldPerMinute.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "/min"
                : state.productionTextOverride;
            friendly.Display(state.friendlyShip);
            enemy.Display(state.opposingShip);
            bool playing = state.outcome == MatchOutcome.InProgress;
            result.gameObject.SetActive(!playing);
            if (!playing) result.text = state.outcome == MatchOutcome.Victory ? "VICTORY" : "DEFEAT";
            bool showShop = playing && state.shopOpen;
            shopGroup.SetActive(showShop);
            pause.interactable = playing;
            var commands = state.interaction;
            interactionGroup.SetActive(playing && !showShop && (!string.IsNullOrEmpty(commands.primary) ||
                !string.IsNullOrEmpty(commands.secondary) || !string.IsNullOrEmpty(commands.leave)));
            CommandLabel(primary, primaryLabel, commands.primary, commands.primaryEnabled);
            CommandLabel(secondary, secondaryLabel, commands.secondary, commands.secondaryEnabled);
            CommandLabel(leaveInteraction, leaveLabel, commands.leave, commands.leaveEnabled);
            buy.interactable = playing && state.purchaseEnabled;
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                HudCard card = state.cards != null && i < state.cards.Length ? state.cards[i] : default;
                view.button.interactable = playing && card.occupied;
                view.title.text = card.occupied ? card.name : "";
                view.price.text = card.occupied ? card.price : "";
                view.empty.SetActive(!card.occupied);
                view.tile.SetActive(card.occupied);
                view.selection.SetActive(card.occupied && state.selectedSlot == i);
                view.icon.sprite = card.sprite;
                view.icon.gameObject.SetActive(card.occupied && card.sprite);
                for (int g = 0; g < view.fallbackIcons.Length; g++)
                    view.fallbackIcons[g].SetActive(card.occupied && !card.sprite && card.fallbackGlyph == g + 1);
            }
            int count = state.objectives?.Length ?? 0;
            goalsGroup.SetActive(count > 0);
            for (int i = 0; i < objectives.Length; i++)
            {
                var view = objectives[i];
                view.row.SetActive(i < count);
                if (i >= count) continue;
                view.label.text = state.objectives[i].label;
                view.star.color = state.objectives[i].completed ? view.star.edge : Color.clear;
            }
        }
        static void CommandLabel(Button button, TMP_Text label, string caption, bool enabled)
        {
            label.text = caption ?? "";
            button.gameObject.SetActive(!string.IsNullOrEmpty(caption));
            button.interactable = enabled;
        }
    }
}