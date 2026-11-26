using UnityEngine;

namespace PirateGame.UI
{
    // Only the navigation preview reads these fixtures. A gameplay presenter can call BattleHud.Present directly.
    public sealed class BattlePreviewPresenter
    {
        private readonly PreviewCatalog catalog;
        private readonly LoadoutPresenter loadout;
        public BattlePreviewPresenter(PreviewCatalog catalog, LoadoutPresenter loadout)
        {
            this.catalog = catalog;
            this.loadout = loadout;
        }
        public void Display(BattleHud hud, bool shop, int selectedSlot, int campaignLevel)
        {
            var health = new ShipVitality { current = catalog.previewHealth, maximum = catalog.previewHealth };
            var data = new BattleHudState
            {
                gold = catalog.previewGold,
                goldPerMinute = catalog.previewIncome,
                friendlyShip = health,
                opposingShip = health,
                selectedSlot = selectedSlot,
                shopOpen = shop,
                purchaseEnabled = loadout.Equipped(0, selectedSlot) >= 0,
                interaction = new HudInteraction { primary = "Open ship shop", primaryEnabled = true, secondary = "Upgrade unavailable" }
            };
            for (int i = 0; i < data.cards.Length; i++)
            {
                int id = loadout.Equipped(0, i);
                if (id < 0) continue;
                var card = catalog.cards[id];
                data.cards[i] = new HudCard { occupied = true, name = card.shortName, price = card.Price, fallbackGlyph = Mathf.Min(id, 3) + 1 };
            }
            if (campaignLevel > 0)
            {
                var labels = catalog.stages[campaignLevel - 1].objectiveLabels;
                data.objectives = new HudObjective[labels.Length];
                for (int i = 0; i < labels.Length; i++) data.objectives[i].label = labels[i];
            }
            hud.Present(data);
        }
    }
}