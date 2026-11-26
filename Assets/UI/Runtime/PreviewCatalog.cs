using System;
using UnityEngine;

namespace PirateGame.UI
{
    [Serializable]
    public class PreviewCard
    {
        public string title, shortName, description;
        public int requiredVictory;
        [Tooltip("Empty statistics are undecided. These display values never change gameplay.")]
        public string price, health, cooldown;
        public string Price => string.IsNullOrEmpty(price) ? "—" : price;
        public string Stats => $"Price {Price}    HP {Display(health)}    Cooldown {Display(cooldown)}";
        static string Display(string value) => string.IsNullOrEmpty(value) ? "—" : value;
    }

    [Serializable]
    public sealed class PreviewStage
    {
        public string title, previewCaption;
        public string[] objectives, objectiveLabels;
    }

    public sealed class PreviewCatalog : ScriptableObject
    {
        public PreviewCard[] cards;
        public int plannedStars = 30;
        public int previewGold, previewIncome, previewHealth;
        public PreviewStage[] stages;
        public string[] dialogue;
    }

    // Per player, per play session. Index zero is always the Territory Balloon.
    public sealed class PreviewLoadout
    {
        public const int Capacity = 5;
        private readonly int[] slots = { 0, 1, 2, -1, -1 };
        public int this[int slot] => slots[slot];
        public int SlotOf(int card) => Array.IndexOf(slots, card);
        public bool Contains(int card) => Array.IndexOf(slots, card) >= 0;
        public bool Remove(int slot)
        {
            if (slot < 1 || slot >= Capacity || slots[slot] < 0) return false;
            slots[slot] = -1;
            return true;
        }
        public bool Add(int card, PreviewCatalog catalog, int level, int preferred = -1)
        {
            if (card <= 0 || card >= catalog.cards.Length || level < catalog.cards[card].requiredVictory || Array.IndexOf(slots, card) >= 0) return false;
            int slot = preferred > 0 && preferred < Capacity && slots[preferred] < 0 ? preferred : Array.IndexOf(slots, -1);
            if (slot < 0) return false;
            slots[slot] = card;
            return true;
        }
    }
}