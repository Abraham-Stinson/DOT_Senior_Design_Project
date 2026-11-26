using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UiWireframeShape = PirateGame.UI.WireframeShape;

namespace Game.UI
{
    [Serializable]
    public sealed class ArenaShopSlotView
    {
        public Button button;
        public Image tileBackground;
        public Outline selectionOutline;
        public TMP_Text slotBadge;
        public TMP_Text titleLabel;
        public TMP_Text priceLabel;
        public GameObject emptyState;
        public GameObject contentRoot;
        public UiWireframeShape[] glyphs;
    }

    /// <summary>
    /// Isolated per-seat base shop panel bound permanently to one immutable seatId (0 = Left/Blue, 1 = Right/Red).
    /// </summary>
    public sealed class ArenaSeatShopPanel : MonoBehaviour
    {
        private static readonly Color s_selectedTileColor = new(0.92f, 0.96f, 1f, 1f);
        private static readonly Color s_normalTileColor = new(0.98039216f, 0.98039216f, 0.98039216f, 1f);
        private static readonly Color s_unaffordableTileColor = new(0.93f, 0.91f, 0.91f, 1f);
        private static readonly Color s_selectedOutlineColor = new(0.14509805f, 0.14509805f, 0.14509805f, 1f);
        private static readonly Color s_normalOutlineColor = new(0.46666667f, 0.46666667f, 0.46666667f, 1f);
        private static readonly Color s_affordablePriceColor = new(0.14509805f, 0.14509805f, 0.14509805f, 1f);
        private static readonly Color s_unaffordablePriceColor = new(0.76f, 0.22f, 0.22f, 1f);

        [SerializeField] private byte _seatId;
        [SerializeField] private TMP_Text _headerTitle;
        [SerializeField] private TMP_Text _selectHintLabel;
        [SerializeField] private ArenaShopSlotView[] _slots = new ArenaShopSlotView[5];
        [SerializeField] private Button _buyButton;
        [SerializeField] private TMP_Text _buyLabel;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _leaveLabel;

        private bool _initialized;

        public byte SeatId => _seatId;
        public Button BuyButton => _buyButton;
        public Button LeaveButton => _leaveButton;
        public ArenaShopSlotView[] Slots => _slots;

        public event Action<byte, int> SlotClicked;
        public event Action<byte> BuyClicked;
        public event Action<byte> LeaveClicked;

        public void Configure(
            byte seatId,
            TMP_Text headerTitle,
            TMP_Text selectHintLabel,
            ArenaShopSlotView[] slots,
            Button buyButton,
            TMP_Text buyLabel,
            Button leaveButton,
            TMP_Text leaveLabel)
        {
            _seatId = seatId;
            _headerTitle = headerTitle;
            _selectHintLabel = selectHintLabel;
            _slots = slots;
            _buyButton = buyButton;
            _buyLabel = buyLabel;
            _leaveButton = leaveButton;
            _leaveLabel = leaveLabel;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    ArenaShopSlotView slotView = _slots[i];
                    if (slotView?.button == null)
                    {
                        continue;
                    }

                    DisableKeyboardNavigation(slotView.button);
                    int capturedSlot = i;
                    slotView.button.onClick.AddListener(() => SlotClicked?.Invoke(_seatId, capturedSlot));
                }
            }

            if (_buyButton != null)
            {
                DisableKeyboardNavigation(_buyButton);
                _buyButton.onClick.AddListener(() => BuyClicked?.Invoke(_seatId));
            }

            if (_leaveButton != null)
            {
                DisableKeyboardNavigation(_leaveButton);
                _leaveButton.onClick.AddListener(() => LeaveClicked?.Invoke(_seatId));
            }
        }

        public void Present(ArenaSeatHudState state)
        {
            EnsureInitialized();
            if (state == null)
            {
                return;
            }

            if (gameObject.activeSelf != state.ShowShopPanel)
            {
                gameObject.SetActive(state.ShowShopPanel);
            }

            if (!state.ShowShopPanel)
            {
                return;
            }

            if (_headerTitle != null) _headerTitle.text = state.ShopTitle;
            if (_selectHintLabel != null) _selectHintLabel.text = state.SelectHint;
            if (_buyLabel != null) _buyLabel.text = state.BuyHint;
            if (_leaveLabel != null) _leaveLabel.text = state.LeaveHint;
            if (_buyButton != null) _buyButton.interactable = state.CanBuySelected;

            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                ArenaShopSlotView view = _slots[i];
                if (view == null)
                {
                    continue;
                }

                ArenaShopCardViewModel card = i < state.Cards.Length ? state.Cards[i] : default;

                if (view.button != null)
                {
                    view.button.interactable = card.Occupied;
                }

                if (view.emptyState != null)
                {
                    view.emptyState.SetActive(!card.Occupied);
                }

                if (view.contentRoot != null)
                {
                    view.contentRoot.SetActive(card.Occupied);
                }

                if (view.slotBadge != null)
                {
                    view.slotBadge.text = (i + 1).ToString();
                }

                if (view.titleLabel != null)
                {
                    view.titleLabel.text = card.Occupied ? card.Title : string.Empty;
                }

                if (view.priceLabel != null)
                {
                    view.priceLabel.text = card.Occupied ? card.PriceText : string.Empty;
                    view.priceLabel.color = card.Affordable ? s_affordablePriceColor : s_unaffordablePriceColor;
                }

                if (view.tileBackground != null)
                {
                    view.tileBackground.color = !card.Occupied
                        ? s_normalTileColor
                        : (card.Selected ? s_selectedTileColor : (card.Affordable ? s_normalTileColor : s_unaffordableTileColor));
                }

                if (view.selectionOutline != null)
                {
                    view.selectionOutline.effectColor = card.Selected ? s_selectedOutlineColor : s_normalOutlineColor;
                    view.selectionOutline.effectDistance = card.Selected ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
                }

                if (view.glyphs != null)
                {
                    for (int g = 0; g < view.glyphs.Length; g++)
                    {
                        if (view.glyphs[g] != null)
                        {
                            view.glyphs[g].gameObject.SetActive(card.Occupied && card.GlyphIndex == g);
                        }
                    }
                }
            }
        }

        private static void DisableKeyboardNavigation(Button button)
        {
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
        }
    }
}
