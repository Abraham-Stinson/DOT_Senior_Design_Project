using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateGame.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class ButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        public ButtonTheme theme;
        public Image overlay;
        public RectTransform focusOutline;
        public bool Confirmed { get; private set; }
        public bool Hovered { get; private set; }
        public bool Focused { get; private set; }
        private Button button;
        private bool held, wasEnabled;
        private float submittedUntil;

        void Awake()
        {
            button = GetComponent<Button>();
            if (theme.overrideFont)
                foreach (var text in GetComponentsInChildren<TMP_Text>(true)) text.font = theme.overrideFont;
            overlay.color = Color.white;
            overlay.sprite = theme.overlaySprite;
            foreach (var line in focusOutline.GetComponentsInChildren<Image>(true)) line.color = theme.focusOutline;
        }

        void OnEnable() => Paint(true);
        void OnDisable() { Hovered = Focused = held = false; submittedUntil = 0; }
        void LateUpdate()
        {
            bool finishSubmit = submittedUntil > 0 && Time.unscaledTime >= submittedUntil;
            if (finishSubmit) submittedUntil = 0;
            if (finishSubmit || wasEnabled != button.IsInteractable()) Paint();
        }
        public void SetConfirmed(bool value)
        {
            if (Confirmed == value) return;
            Confirmed = value;
            if (isActiveAndEnabled) Paint();
        }
        void Paint(bool immediate = false)
        {
            wasEnabled = button.IsInteractable();
            focusOutline.gameObject.SetActive(wasEnabled && Focused);
            Color color = !wasEnabled ? (Confirmed ? theme.confirmed : theme.disabled) : held || submittedUntil > 0 ? theme.pressed :
                Hovered ? theme.hover : Confirmed ? theme.confirmed : theme.normal;
            overlay.CrossFadeColor(color, immediate ? 0 : theme.transitionSeconds, true, true);
        }
        public void OnSelect(BaseEventData e) { Focused = true; Paint(); }
        public void OnDeselect(BaseEventData e) { Focused = false; Paint(); }
        public void OnPointerEnter(PointerEventData e) { Hovered = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { Hovered = false; Paint(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { held = true; Paint(); } }
        public void OnPointerUp(PointerEventData e) { held = false; Paint(); }
        public void OnSubmit(BaseEventData e)
        {
            if (!isActiveAndEnabled || !button.IsInteractable()) return;
            submittedUntil = Time.unscaledTime + Mathf.Max(theme.transitionSeconds, .08f);
            Paint();
        }
    }
}