using TMPro;
using UnityEngine;

namespace PirateGame.UI
{
    [CreateAssetMenu(menuName = "UI/Button theme")]
    public sealed class ButtonTheme : ScriptableObject
    {
        public Color normal = Color.clear;
        public Color hover = new(.12f, .12f, .12f, .12f);
        public Color pressed = new(.1f, .1f, .1f, .30f);
        public Color disabled = new(.68f, .68f, .68f, .42f);
        public Color confirmed = new(.24f, .24f, .24f, .18f);
        public Color focusOutline = new(.16f, .16f, .16f, .9f);
        public Color activeFace = new(.32f, .32f, .32f);
        public Color activeText = Color.white;
        public Color inactiveFace = new(.96f, .96f, .96f);
        public Color inactiveText = new(.15f, .15f, .15f);
        [Min(0)] public float transitionSeconds = .07f;
        public Sprite overlaySprite;
        [Tooltip("Leave empty to retain the font authored on each label.")]
        public TMP_FontAsset overrideFont;
    }
}