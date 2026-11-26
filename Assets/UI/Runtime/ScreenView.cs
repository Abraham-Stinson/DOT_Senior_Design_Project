using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.UI
{
    public enum Page { Title, MainMenu, Campaign, Skirmish, Map, Local, AI, Mission, Multiplayer, Lobby, Dialogue, Hud, Shop, Pause, Victory, Defeat, Settings, Credits }
    public enum PreviewMode { Campaign, Local, AI, Online }
    [Serializable] public struct ViewReference { public string key; public GameObject target; }
    public sealed class ScreenView : MonoBehaviour
    {
        public Page page;
        public BattleHud battleHud;
        public ViewReference[] references;
        private Dictionary<string, GameObject> lookup;
        public GameObject Item(string key)
        {
            if (lookup == null)
            {
                lookup = new();
                foreach (var pair in references) lookup.Add(pair.key, pair.target);
            }
            return lookup.TryGetValue(key, out var item) ? item : null;
        }
        public void Show(string key, bool visible)
        {
            var item = Item(key);
            if (item != null && item.activeSelf != visible) item.SetActive(visible);
        }
        public void Label(string key, string value)
        {
            var item = Item(key);
            if (!item) return;
            var label = item.GetComponent<TMP_Text>();
            if (label.text != value) label.text = value;
        }
        public void Enable(string key, bool value)
        {
            var item = Item(key);
            if (item) item.GetComponent<Selectable>().interactable = value;
        }
    }
}