using UnityEngine;

namespace PirateGame.UI
{
    public sealed class LoadoutPresenter
    {
        private readonly PreviewCatalog catalog;
        private readonly PreviewLoadout[] players = { new(), new() };
        private readonly int[] detail = { 2, 2 }, preferred = { -1, -1 };
        public LoadoutPresenter(PreviewCatalog catalog) { this.catalog = catalog; }
        public int Equipped(int player, int slot) => players[player][slot];
        public void Inspect(int player, int card) { detail[player] = card; }
        public void Slot(int player, int slot)
        {
            if (slot == 0) return;
            Inspect(player, Equipped(player, slot));
            players[player].Remove(slot);
            preferred[player] = slot;
        }
        public bool Choice(int player, int card, int level, out int slot)
        {
            slot = -1;
            if (!players[player].Add(card, catalog, level, preferred[player])) return false;
            slot = players[player].SlotOf(card);
            preferred[player] = slot;
            Inspect(player, card);
            return true;
        }
        public void Render(ScreenView view, int player, int level, bool editable)
        {
            string p = "p" + player;
            for (int i = 0; i < PreviewLoadout.Capacity; i++)
            {
                RenderSlot(view, p, i, Equipped(player, i), preferred[player] == i);
                view.Enable(p + "slot" + i, editable && i > 0);
            }
            for (int i = 1; i < catalog.cards.Length; i++)
            {
                var card = catalog.cards[i];
                bool locked = level < card.requiredVictory;
                bool equipped = players[player].Contains(i);
                view.Label(p + "condition" + (i-1), locked ? "WIN L" + card.requiredVictory : equipped ? "EQUIPPED" : "AVAILABLE");
                if (i == 3) view.Show(p + "shieldAvailable", !locked);
                if (i >= 3) view.Show(p + "choiceIcon" + (i-1), locked);
                view.Enable(p + "choice" + (i-1), editable && !locked && !equipped);
            }
            if (detail[player] < 0)
            {
                view.Label(p + "card", "EMPTY SLOT");
                view.Label(p + "description", "Choose an available card or leave this slot empty.");
                view.Label(p + "rule", "");
                view.Label(p + "stats", "");
                return;
            }
            var shown = catalog.cards[detail[player]];
            view.Label(p + "card", shown.title.ToUpperInvariant());
            view.Label(p + "description", shown.description);
            view.Label(p + "rule", level < shown.requiredVictory ? $"Win campaign level {shown.requiredVictory}" :
                detail[player] == 0 ? "Fixed in slot 1" : "Select a slot to remove • choose a card to equip");
            view.Label(p + "stats", shown.Stats);
        }
        public void RenderSlot(ScreenView view, string p, int slot, int card, bool highlight)
        {
            string key = p + "slot" + slot;
            bool occupied = card >= 0;
            view.Show(key + "empty", !occupied);
            view.Show(key + "tile", occupied);
            view.Show(key + "highlight", highlight);
            for (int i = 0; i < 4; i++) view.Show(key + "icon" + i, occupied && Mathf.Min(card, 3) == i);
            view.Label(key + "name", occupied ? catalog.cards[card].shortName : "");
            view.Label(key + "price", occupied ? catalog.cards[card].Price + (card == 0 && p != "shop" ? " / FIXED" : "") : "");
        }
    }
}