using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [Serializable]
    public sealed class ArenaCarryStripView
    {
        public GameObject root;
        public Button deployButton;
        public TMP_Text deployLabel;
        public Button cancelButton;
        public TMP_Text cancelLabel;
        public GameObject noticeGroup;
        public TMP_Text noticeLabel;
    }

    /// <summary>
    /// Unified Tulpar-styled live Battle HUD for CannonBrawlArena:
    /// Top-Left Blue Gold + Blue HQ bar, Top-Center Pause, Top-Right Red Gold + Red HQ bar,
    /// and isolated per-seat Left/Right base shops and carry/feedback strips.
    /// </summary>
    public sealed class ArenaBattleHudView : MonoBehaviour
    {
        [Header("Top Bar")]
        [SerializeField] private GameObject _leftGoldRoot;
        [SerializeField] private TMP_Text _leftGoldLabel;
        [SerializeField] private TMP_Text _leftIncomeLabel;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private GameObject _rightGoldRoot;
        [SerializeField] private TMP_Text _rightGoldLabel;
        [SerializeField] private TMP_Text _rightIncomeLabel;

        [Header("HQ Health Bars")]
        [SerializeField] private GameObject _blueHqBarRoot;
        [SerializeField] private Image _blueHqFill;
        [SerializeField] private float _blueHqFullWidth = 272f;
        [SerializeField] private TMP_Text _blueHqLabel;
        [SerializeField] private GameObject _redHqBarRoot;
        [SerializeField] private Image _redHqFill;
        [SerializeField] private float _redHqFullWidth = 272f;
        [SerializeField] private TMP_Text _redHqLabel;

        [Header("Per-Seat Shops & Carry Prompts")]
        [SerializeField] private ArenaSeatShopPanel _leftShopPanel;
        [SerializeField] private ArenaSeatShopPanel _rightShopPanel;
        [SerializeField] private ArenaCarryStripView _leftCarryStrip;
        [SerializeField] private ArenaCarryStripView _rightCarryStrip;

        private bool _initialized;

        public GameObject LeftGoldRoot => _leftGoldRoot;
        public TMP_Text LeftGoldLabel => _leftGoldLabel;
        public TMP_Text LeftIncomeLabel => _leftIncomeLabel;
        public Button PauseButton => _pauseButton;
        public GameObject RightGoldRoot => _rightGoldRoot;
        public TMP_Text RightGoldLabel => _rightGoldLabel;
        public TMP_Text RightIncomeLabel => _rightIncomeLabel;
        public GameObject BlueHqBarRoot => _blueHqBarRoot;
        public TMP_Text BlueHqLabel => _blueHqLabel;
        public GameObject RedHqBarRoot => _redHqBarRoot;
        public TMP_Text RedHqLabel => _redHqLabel;
        public ArenaSeatShopPanel LeftShopPanel => _leftShopPanel;
        public ArenaSeatShopPanel RightShopPanel => _rightShopPanel;
        public ArenaCarryStripView LeftCarryStrip => _leftCarryStrip;
        public ArenaCarryStripView RightCarryStrip => _rightCarryStrip;

        public event Action PauseClicked;
        public event Action<byte, int> SlotClicked;
        public event Action<byte> BuyClicked;
        public event Action<byte> LeaveShopClicked;
        public event Action<byte> DeployClicked;
        public event Action<byte> CancelCarryClicked;

        public void Configure(
            GameObject leftGoldRoot,
            TMP_Text leftGoldLabel,
            TMP_Text leftIncomeLabel,
            Button pauseButton,
            GameObject rightGoldRoot,
            TMP_Text rightGoldLabel,
            TMP_Text rightIncomeLabel,
            GameObject blueHqBarRoot,
            Image blueHqFill,
            float blueHqFullWidth,
            TMP_Text blueHqLabel,
            GameObject redHqBarRoot,
            Image redHqFill,
            float redHqFullWidth,
            TMP_Text redHqLabel,
            ArenaSeatShopPanel leftShopPanel,
            ArenaSeatShopPanel rightShopPanel,
            ArenaCarryStripView leftCarryStrip,
            ArenaCarryStripView rightCarryStrip)
        {
            _leftGoldRoot = leftGoldRoot;
            _leftGoldLabel = leftGoldLabel;
            _leftIncomeLabel = leftIncomeLabel;
            _pauseButton = pauseButton;
            _rightGoldRoot = rightGoldRoot;
            _rightGoldLabel = rightGoldLabel;
            _rightIncomeLabel = rightIncomeLabel;
            _blueHqBarRoot = blueHqBarRoot;
            _blueHqFill = blueHqFill;
            _blueHqFullWidth = blueHqFullWidth;
            _blueHqLabel = blueHqLabel;
            _redHqBarRoot = redHqBarRoot;
            _redHqFill = redHqFill;
            _redHqFullWidth = redHqFullWidth;
            _redHqLabel = redHqLabel;
            _leftShopPanel = leftShopPanel;
            _rightShopPanel = rightShopPanel;
            _leftCarryStrip = leftCarryStrip;
            _rightCarryStrip = rightCarryStrip;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (_leftShopPanel != null)
            {
                _leftShopPanel.SlotClicked -= ForwardSlotClicked;
                _leftShopPanel.BuyClicked -= ForwardBuyClicked;
                _leftShopPanel.LeaveClicked -= ForwardLeaveClicked;
            }

            if (_rightShopPanel != null)
            {
                _rightShopPanel.SlotClicked -= ForwardSlotClicked;
                _rightShopPanel.BuyClicked -= ForwardBuyClicked;
                _rightShopPanel.LeaveClicked -= ForwardLeaveClicked;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            if (_pauseButton != null)
            {
                DisableKeyboardNavigation(_pauseButton);
                _pauseButton.onClick.AddListener(() => PauseClicked?.Invoke());
            }

            BindShopPanel(_leftShopPanel);
            BindShopPanel(_rightShopPanel);
            BindCarryStrip(0, _leftCarryStrip);
            BindCarryStrip(1, _rightCarryStrip);
        }

        private void BindShopPanel(ArenaSeatShopPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            panel.SlotClicked += ForwardSlotClicked;
            panel.BuyClicked += ForwardBuyClicked;
            panel.LeaveClicked += ForwardLeaveClicked;
        }

        private void BindCarryStrip(byte seatId, ArenaCarryStripView strip)
        {
            if (strip == null)
            {
                return;
            }

            if (strip.deployButton != null)
            {
                DisableKeyboardNavigation(strip.deployButton);
                strip.deployButton.onClick.AddListener(() => DeployClicked?.Invoke(seatId));
            }

            if (strip.cancelButton != null)
            {
                DisableKeyboardNavigation(strip.cancelButton);
                strip.cancelButton.onClick.AddListener(() => CancelCarryClicked?.Invoke(seatId));
            }
        }

        private void ForwardSlotClicked(byte seatId, int slot) => SlotClicked?.Invoke(seatId, slot);
        private void ForwardBuyClicked(byte seatId) => BuyClicked?.Invoke(seatId);
        private void ForwardLeaveClicked(byte seatId) => LeaveShopClicked?.Invoke(seatId);

        public void Present(ArenaBattleHudState state)
        {
            EnsureInitialized();
            if (state == null)
            {
                return;
            }

            if (_pauseButton != null)
            {
                _pauseButton.interactable = state.PauseInteractable;
            }

            PresentTopGold(_leftGoldRoot, _leftGoldLabel, _leftIncomeLabel, state.LeftSeat);
            PresentTopGold(_rightGoldRoot, _rightGoldLabel, _rightIncomeLabel, state.RightSeat);

            PresentHqBar(_blueHqBarRoot, _blueHqFill, _blueHqFullWidth, _blueHqLabel, "BLUE HQ", state.BlueHqCurrent, state.BlueHqMax, state.BlueHqFraction);
            PresentHqBar(_redHqBarRoot, _redHqFill, _redHqFullWidth, _redHqLabel, "RED HQ", state.RedHqCurrent, state.RedHqMax, state.RedHqFraction);

            if (_leftShopPanel != null) _leftShopPanel.Present(state.LeftSeat);
            if (_rightShopPanel != null) _rightShopPanel.Present(state.RightSeat);

            PresentCarryStrip(_leftCarryStrip, state.LeftSeat);
            PresentCarryStrip(_rightCarryStrip, state.RightSeat);
        }

        private static void PresentTopGold(GameObject root, TMP_Text goldLabel, TMP_Text incomeLabel, ArenaSeatHudState seat)
        {
            if (root != null && root.activeSelf != seat.ShowGoldPanel)
            {
                root.SetActive(seat.ShowGoldPanel);
            }

            if (!seat.ShowGoldPanel)
            {
                return;
            }

            if (goldLabel != null && goldLabel.text != seat.GoldText)
            {
                goldLabel.text = seat.GoldText;
            }

            if (incomeLabel != null && incomeLabel.text != seat.IncomeText)
            {
                incomeLabel.text = seat.IncomeText;
            }
        }

        private static void PresentHqBar(
            GameObject root,
            Image fill,
            float fullWidth,
            TMP_Text label,
            string prefix,
            int current,
            int max,
            float fraction)
        {
            bool hasHq = max > 0;
            if (root != null && root.activeSelf != hasHq)
            {
                root.SetActive(hasHq);
            }

            if (!hasHq)
            {
                return;
            }

            if (fill != null)
            {
                fill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fullWidth * fraction);
            }

            if (label != null)
            {
                string next = $"{prefix}  {current} / {max}";
                if (label.text != next)
                {
                    label.text = next;
                }
            }
        }

        private static void PresentCarryStrip(ArenaCarryStripView strip, ArenaSeatHudState seat)
        {
            if (strip == null || strip.root == null)
            {
                return;
            }

            if (strip.root.activeSelf != seat.ShowCarryPanel)
            {
                strip.root.SetActive(seat.ShowCarryPanel);
            }

            if (!seat.ShowCarryPanel)
            {
                return;
            }

            if (strip.deployButton != null)
            {
                strip.deployButton.gameObject.SetActive(seat.IsCarrying);
            }
            if (strip.deployLabel != null && seat.IsCarrying)
            {
                strip.deployLabel.text = seat.DeployLabel;
            }

            if (strip.cancelButton != null)
            {
                strip.cancelButton.gameObject.SetActive(seat.IsCarrying);
            }
            if (strip.cancelLabel != null && seat.IsCarrying)
            {
                strip.cancelLabel.text = seat.CancelLabel;
            }

            bool hasNotice = !string.IsNullOrEmpty(seat.StatusNotice);
            if (strip.noticeGroup != null)
            {
                strip.noticeGroup.SetActive(hasNotice);
            }
            if (strip.noticeLabel != null && hasNotice)
            {
                strip.noticeLabel.text = seat.StatusNotice;
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
