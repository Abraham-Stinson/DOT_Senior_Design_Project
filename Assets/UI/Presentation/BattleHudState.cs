using System;
using UnityEngine;

namespace PirateGame.UI
{
    public enum HudIntent { Interact, SelectCard, Buy, LeaveShop, Pause, SecondaryInteraction, LeaveInteraction }
    public enum MatchOutcome { InProgress, Victory, Defeat }
    [Serializable] public struct ShipVitality
    {
        public int current, maximum;
        public float Fraction => maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0;
    }
    [Serializable] public struct HudCard
    {
        public bool occupied;
        public string name, price;
        public Sprite sprite;
        public int fallbackGlyph; // 0 hides the optional Inspector-authored placeholder.
    }
    [Serializable] public struct HudObjective { public string label; public bool completed; }
    [Serializable] public struct HudInteraction
    {
        public string primary, secondary, leave;
        public bool primaryEnabled, secondaryEnabled, leaveEnabled;
    }
    [Serializable]
    public sealed class BattleHudState
    {
        public int gold;
        public float goldPerMinute;
        public string goldTextOverride;
        public string productionTextOverride;
        public ShipVitality friendlyShip, opposingShip;
        public HudCard[] cards = new HudCard[5];
        public int selectedSlot;
        public bool shopOpen, purchaseEnabled;
        public HudInteraction interaction;
        public HudObjective[] objectives = Array.Empty<HudObjective>();
        public MatchOutcome outcome;
    }
}