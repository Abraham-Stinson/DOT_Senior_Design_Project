using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Real in-game World-Space Canvas UI rendered directly above each docked structure (per seat),
    /// replacing OnGUI docked prompts with zero FishNet dependencies.
    /// </summary>
    public sealed class DockedStructureWorldUI : MonoBehaviour
    {
        [SerializeField] private ArenaSessionBootstrap _bootstrap;

        private const float WorldScale = 0.01f;
        private readonly Dictionary<byte, SeatPromptCanvas> _seatPrompts = new();
        private readonly List<ushort> _loadoutBuffer = new();
        private Font _font;
        private Sprite _whiteSprite;
        private Texture2D _whiteTexture;

        public void BindBootstrap(ArenaSessionBootstrap bootstrap)
        {
            if (bootstrap != null)
            {
                _bootstrap = bootstrap;
            }
        }

        private void Awake()
        {
            if (_bootstrap == null)
            {
                _bootstrap = GetComponent<ArenaSessionBootstrap>();
            }
        }

        private void OnDestroy()
        {
            foreach (SeatPromptCanvas prompt in _seatPrompts.Values)
            {
                if (prompt != null && prompt.Root != null)
                {
                    Destroy(prompt.Root);
                }
            }
            _seatPrompts.Clear();

            if (_whiteSprite != null)
            {
                Destroy(_whiteSprite);
                _whiteSprite = null;
            }
            if (_whiteTexture != null)
            {
                Destroy(_whiteTexture);
                _whiteTexture = null;
            }
        }

        private void LateUpdate()
        {
            if (_bootstrap == null || _bootstrap.SeatCount <= 0)
            {
                HideAllPrompts();
                return;
            }

            Camera cam = ResolveWorldCamera();
            CardCatalogSO catalog = _bootstrap.CardCatalog;
            bool canAct = _bootstrap.CurrentPhase == MatchPhase.Playing;
            int seatCount = _bootstrap.SeatCount;

            for (int i = 0; i < seatCount; i++)
            {
                if (!_bootstrap.TryGetSeatEconomyInfo(
                        i,
                        out byte seatId,
                        out TeamId team,
                        out _,
                        out int gold,
                        out _))
                {
                    continue;
                }

                bool isCarrying = _bootstrap.TryGetAirshipCarryInfo(seatId, out _, out _, out _, out _);
                if (isCarrying || !_bootstrap.TryGetDockedInteractionInfo(
                        seatId,
                        out DockedTargetKind targetKind,
                        out DockedInteractionSubMode subMode,
                        out int selectedDeckSlot,
                        out DockedStructureAction selectedAction,
                        out ushort targetCardId,
                        out int sellRefundGold,
                        out bool canUseTarget,
                        out bool supportsUseAction,
                        out float targetCooldownRemaining,
                        out Vector2 dockedWorldAnchor))
                {
                    if (_seatPrompts.TryGetValue(seatId, out SeatPromptCanvas existing) && existing.Root != null)
                    {
                        existing.Root.SetActive(false);
                    }
                    continue;
                }

                SeatPromptCanvas prompt = GetOrCreatePrompt(seatId);
                prompt.Root.SetActive(true);
                prompt.Canvas.worldCamera = cam;

                UpdatePromptContents(
                    prompt,
                    seatId,
                    team,
                    gold,
                    canAct,
                    targetKind,
                    subMode,
                    selectedDeckSlot,
                    selectedAction,
                    targetCardId,
                    sellRefundGold,
                    canUseTarget,
                    supportsUseAction,
                    targetCooldownRemaining,
                    catalog);

                PositionPromptAboveStructure(prompt, dockedWorldAnchor, cam);
            }
        }

        private void HideAllPrompts()
        {
            foreach (SeatPromptCanvas prompt in _seatPrompts.Values)
            {
                if (prompt != null && prompt.Root != null)
                {
                    prompt.Root.SetActive(false);
                }
            }
        }

        private Camera ResolveWorldCamera()
        {
            return Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }

        private void PositionPromptAboveStructure(SeatPromptCanvas prompt, Vector2 worldAnchor, Camera cam)
        {
            Vector2 panelSizePx = prompt.RootRect.sizeDelta;
            float halfW = panelSizePx.x * WorldScale * 0.5f;
            float halfH = panelSizePx.y * WorldScale * 0.5f;

            Vector3 targetPos = new(worldAnchor.x, worldAnchor.y + halfH + 0.85f, -1f);

            if (cam != null && cam.orthographic)
            {
                float orthoHalfH = cam.orthographicSize;
                float orthoHalfW = orthoHalfH * cam.aspect;
                Vector3 camPos = cam.transform.position;

                float minX = camPos.x - orthoHalfW + halfW + 0.25f;
                float maxX = camPos.x + orthoHalfW - halfW - 0.25f;
                float minY = camPos.y - orthoHalfH + halfH + 0.25f;
                float maxY = camPos.y + orthoHalfH - halfH - 0.25f;

                if (minX <= maxX) targetPos.x = Mathf.Clamp(targetPos.x, minX, maxX);
                if (minY <= maxY) targetPos.y = Mathf.Clamp(targetPos.y, minY, maxY);
            }

            prompt.Root.transform.position = targetPos;
        }

        private void UpdatePromptContents(
            SeatPromptCanvas prompt,
            byte seatId,
            TeamId team,
            int gold,
            bool canAct,
            DockedTargetKind targetKind,
            DockedInteractionSubMode subMode,
            int selectedDeckSlot,
            DockedStructureAction selectedAction,
            ushort targetCardId,
            int sellRefundGold,
            bool canUseTarget,
            bool supportsUseAction,
            float targetCooldownRemaining,
            CardCatalogSO catalog)
        {
            bool isLocalPvP = _bootstrap.ActiveMode == MatchMode.LocalPvP;
            string playerTag = isLocalPvP ? (seatId == 0 ? $"P1 · {team}" : $"P2 · {team}") : $"{team}";
            string navHint = isLocalPvP
                ? (seatId == 0 ? "A/D: Seç · Space: Onay · E: Ayrıl" : "←/→: Seç · RCtrl: Onay · Enter: Ayrıl")
                : "A/D: Seç · Space: Onay · E: Ayrıl";

            Color teamAccent = team == TeamId.TeamA
                ? new Color(0.16f, 0.50f, 0.86f, 0.96f)
                : (team == TeamId.TeamB ? new Color(0.84f, 0.28f, 0.28f, 0.96f) : new Color(0.32f, 0.36f, 0.44f, 0.96f));
            prompt.HeaderBar.color = teamAccent;
            prompt.BorderImage.color = Color.Lerp(teamAccent, Color.white, 0.35f);
            prompt.PointerStem.color = Color.Lerp(teamAccent, Color.white, 0.35f);

            if (subMode == DockedInteractionSubMode.BaseDeckSelect || targetKind == DockedTargetKind.Base)
            {
                prompt.RootRect.sizeDelta = new Vector2(440f, 152f);
                prompt.BaseView.SetActive(true);
                prompt.StructureView.SetActive(false);
                prompt.WeaponView.SetActive(false);

                string cardName = ResolveCardName(targetCardId, catalog);
                int cost = ResolveCardCost(targetCardId, catalog);
                bool canAfford = gold >= cost;

                prompt.HeaderTitle.text = $"{playerTag}  ·  BASE";
                prompt.BaseSelectedLabel.text = $"{selectedDeckSlot + 1}. {cardName}  ({cost}g)" + (canAfford ? "" : "  [YETERSİZ]");
                prompt.BaseSelectedLabel.color = canAfford ? new Color(0.75f, 0.98f, 0.80f) : new Color(1f, 0.50f, 0.48f);
                prompt.BaseBuyButtonText.text = canAfford ? $"Al & Taşı ({cost}g)" : $"Yetersiz ({cost}g)";
                prompt.BaseBuyButton.interactable = canAct && canAfford && targetCardId != 0;
                prompt.BasePrevButton.interactable = canAct;
                prompt.BaseNextButton.interactable = canAct;
                prompt.BaseHintText.text = navHint;

                if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                {
                    for (int s = 0; s < prompt.SlotPills.Length; s++)
                    {
                        bool hasSlot = s < _loadoutBuffer.Count;
                        prompt.SlotPills[s].Background.gameObject.SetActive(hasSlot);
                        if (!hasSlot)
                        {
                            continue;
                        }

                        bool isSelected = s == selectedDeckSlot;
                        string slotName = Abbreviate(ResolveCardName(_loadoutBuffer[s], catalog), 7);
                        prompt.SlotPills[s].Label.text = $"{s + 1}:{slotName}";
                        prompt.SlotPills[s].Label.color = isSelected ? Color.black : new Color(0.85f, 0.90f, 0.96f);
                        prompt.SlotPills[s].Background.color = isSelected
                            ? new Color(1f, 0.85f, 0.25f, 1f)
                            : new Color(0.14f, 0.19f, 0.28f, 0.95f);
                    }
                }
            }
            else if (subMode == DockedInteractionSubMode.WeaponManualControl)
            {
                prompt.RootRect.sizeDelta = new Vector2(360f, 94f);
                prompt.BaseView.SetActive(false);
                prompt.StructureView.SetActive(false);
                prompt.WeaponView.SetActive(true);

                string structureName = ResolveCardName(targetCardId, catalog);
                prompt.HeaderTitle.text = $"{playerTag}  ·  {structureName} (SİLAH KONTROLÜ)";
                prompt.WeaponHintText.text = isLocalPvP
                    ? (seatId == 0 ? "WASD: Nişan · Space: Ateş · Q: Menü · E: Ayrıl" : "Oklar: Nişan · RCtrl: Ateş · Backspace: Menü · Enter: Ayrıl")
                    : "Hareket/Fare: Nişan · Space/Tık: Ateş · Q: Menü · E: Ayrıl";
                prompt.WeaponMenuButton.interactable = canAct;
                prompt.WeaponUndockButton.interactable = canAct;
            }
            else
            {
                prompt.RootRect.sizeDelta = new Vector2(356f, 114f);
                prompt.BaseView.SetActive(false);
                prompt.StructureView.SetActive(true);
                prompt.WeaponView.SetActive(false);

                string structureName = ResolveCardName(targetCardId, catalog);
                prompt.HeaderTitle.text = $"{playerTag}  ·  {structureName}";

                // Gold Mine has SupportsUseAction == false: never show a bogus Use button.
                prompt.UsePillButton.gameObject.SetActive(supportsUseAction);
                if (supportsUseAction)
                {
                    bool useSelected = selectedAction == DockedStructureAction.Use;
                    string useLabel = canUseTarget
                        ? "KULLAN"
                        : (targetCooldownRemaining > 0.05f ? $"BEKLE {targetCooldownRemaining:F1}s" : "KULLAN (Yok)");

                    prompt.UsePillText.text = useSelected ? $"> {useLabel} <" : useLabel;
                    prompt.UsePillBg.color = useSelected
                        ? (canUseTarget ? new Color(0.18f, 0.72f, 0.42f, 1f) : new Color(0.55f, 0.48f, 0.20f, 0.95f))
                        : new Color(0.15f, 0.20f, 0.30f, 0.95f);
                    prompt.UsePillText.color = useSelected ? Color.white : new Color(0.76f, 0.82f, 0.90f);
                    prompt.UsePillButton.interactable = canAct && canUseTarget;
                }

                bool sellSelected = selectedAction == DockedStructureAction.Sell || !supportsUseAction;
                string sellLabel = $"SAT (+{sellRefundGold}g)";
                prompt.SellPillText.text = sellSelected ? $"> {sellLabel} <" : sellLabel;
                prompt.SellPillBg.color = sellSelected
                    ? new Color(0.86f, 0.36f, 0.20f, 1f)
                    : new Color(0.15f, 0.20f, 0.30f, 0.95f);
                prompt.SellPillText.color = sellSelected ? Color.white : new Color(0.82f, 0.85f, 0.90f);
                prompt.SellPillButton.interactable = canAct;

                prompt.StructureHintText.text = supportsUseAction
                    ? navHint
                    : (isLocalPvP
                        ? (seatId == 0 ? "Space: Sat · E: Ayrıl" : "RCtrl: Sat · Enter: Ayrıl")
                        : "Space/Tık: Sat · E: Ayrıl");
            }
        }

        private SeatPromptCanvas GetOrCreatePrompt(byte seatId)
        {
            if (_seatPrompts.TryGetValue(seatId, out SeatPromptCanvas existing) && existing.Root != null)
            {
                return existing;
            }

            Font font = GetFont();
            Sprite sprite = GetWhiteSprite();

            var root = new GameObject($"DockedPromptWorldCanvas_Seat{seatId}", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(420f, 148f);
            rootRect.localScale = new Vector3(WorldScale, WorldScale, 1f);

            // Outer border & dark backing
            Image borderImg = CreateStretchedImage("Border", rootRect, sprite, new Color(0.45f, 0.72f, 0.95f, 0.95f));
            Image bgImg = CreateInsetImage("PanelBg", rootRect, sprite, new Color(0.07f, 0.10f, 0.16f, 0.94f), 3f);

            // Downward pointer stem connecting UI cleanly to structure below
            var stemGo = new GameObject("PointerStem", typeof(RectTransform), typeof(Image));
            stemGo.transform.SetParent(rootRect, false);
            var stemRect = stemGo.GetComponent<RectTransform>();
            stemRect.anchorMin = new Vector2(0.5f, 0f);
            stemRect.anchorMax = new Vector2(0.5f, 0f);
            stemRect.pivot = new Vector2(0.5f, 1f);
            stemRect.anchoredPosition = new Vector2(0f, 0f);
            stemRect.sizeDelta = new Vector2(14f, 18f);
            var pointerStem = stemGo.GetComponent<Image>();
            pointerStem.sprite = sprite;

            // Header bar (28px tall at top)
            var headerGo = new GameObject("HeaderBar", typeof(RectTransform), typeof(Image));
            headerGo.transform.SetParent(bgImg.rectTransform, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 28f);
            var headerBar = headerGo.GetComponent<Image>();
            headerBar.sprite = sprite;

            Text headerTitle = CreateText("HeaderTitle", headerRect, font, "P1", 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(headerTitle.rectTransform, 6f, 2f);

            // Content body area below header
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(bgImg.rectTransform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(8f, 6f);
            contentRect.offsetMax = new Vector2(-8f, -32f);

            // --- Base View ---
            var baseView = new GameObject("BaseView", typeof(RectTransform));
            baseView.transform.SetParent(contentRect, false);
            Stretch(baseView.GetComponent<RectTransform>(), 0f, 0f);

            Text baseSelectedLabel = CreateText("SelectedCard", baseView.transform, font, "Slot 1", 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            SetAnchoredRow(baseSelectedLabel.rectTransform, topY: 0f, height: 24f);

            var carouselRow = CreateRow("CarouselRow", baseView.transform, topY: -26f, height: 32f, spacing: 6f);
            Button basePrevBtn = CreateButton("PrevBtn", carouselRow, font, "<", 36f, new Color(0.20f, 0.27f, 0.38f));
            Button baseBuyBtn = CreateButton("BuyBtn", carouselRow, font, "Al & Taşı", 0f, new Color(0.17f, 0.56f, 0.34f), expandWidth: true);
            Button baseNextBtn = CreateButton("NextBtn", carouselRow, font, ">", 36f, new Color(0.20f, 0.27f, 0.38f));

            var slotsRow = CreateRow("SlotPillsRow", baseView.transform, topY: -62f, height: 28f, spacing: 4f);
            var slotPills = new SlotPill[DeckRules.DeckSize];
            for (int s = 0; s < slotPills.Length; s++)
            {
                var pillGo = new GameObject($"Slot_{s}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                pillGo.transform.SetParent(slotsRow, false);
                var layoutElem = pillGo.GetComponent<LayoutElement>();
                layoutElem.flexibleWidth = 1f;
                layoutElem.minHeight = 26f;
                var pillImg = pillGo.GetComponent<Image>();
                pillImg.sprite = sprite;
                Text pillTxt = CreateText("Label", pillGo.transform, font, $"{s + 1}", 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                Stretch(pillTxt.rectTransform, 2f, 1f);
                slotPills[s] = new SlotPill { Background = pillImg, Label = pillTxt };
            }

            Text baseHintText = CreateText("BaseHint", baseView.transform, font, "", 11, FontStyle.Normal, new Color(0.75f, 0.82f, 0.92f), TextAnchor.MiddleCenter);
            SetAnchoredRow(baseHintText.rectTransform, topY: -94f, height: 18f);

            // --- Structure View (Use / Sell) ---
            var structureView = new GameObject("StructureView", typeof(RectTransform));
            structureView.transform.SetParent(contentRect, false);
            Stretch(structureView.GetComponent<RectTransform>(), 0f, 0f);

            var actionRow = CreateRow("ActionRow", structureView.transform, topY: -4f, height: 38f, spacing: 8f);
            Button useBtn = CreateButton("UseBtn", actionRow, font, "KULLAN", 0f, new Color(0.18f, 0.55f, 0.34f), expandWidth: true);
            Button sellBtn = CreateButton("SellBtn", actionRow, font, "SAT", 0f, new Color(0.72f, 0.32f, 0.20f), expandWidth: true);

            Text structureHintText = CreateText("StructureHint", structureView.transform, font, "", 12, FontStyle.Normal, new Color(0.78f, 0.84f, 0.94f), TextAnchor.MiddleCenter);
            SetAnchoredRow(structureHintText.rectTransform, topY: -48f, height: 20f);

            // --- Weapon Control View ---
            var weaponView = new GameObject("WeaponView", typeof(RectTransform));
            weaponView.transform.SetParent(contentRect, false);
            Stretch(weaponView.GetComponent<RectTransform>(), 0f, 0f);

            Text weaponHintText = CreateText("WeaponHint", weaponView.transform, font, "", 12, FontStyle.Bold, new Color(1f, 0.92f, 0.55f), TextAnchor.MiddleCenter);
            SetAnchoredRow(weaponHintText.rectTransform, topY: 0f, height: 22f);

            var weaponRow = CreateRow("WeaponRow", weaponView.transform, topY: -26f, height: 28f, spacing: 8f);
            Button weaponMenuBtn = CreateButton("WeaponMenuBtn", weaponRow, font, "Menüye Dön", 0f, new Color(0.24f, 0.30f, 0.42f), expandWidth: true);
            Button weaponUndockBtn = CreateButton("WeaponUndockBtn", weaponRow, font, "Ayrıl", 84f, new Color(0.55f, 0.24f, 0.26f));

            // Wire click events for mouse/touch convenience too
            byte capturedSeatId = seatId;
            basePrevBtn.onClick.AddListener(() => _bootstrap?.RequestCycleDockedSelection(capturedSeatId, -1));
            baseNextBtn.onClick.AddListener(() => _bootstrap?.RequestCycleDockedSelection(capturedSeatId, 1));
            baseBuyBtn.onClick.AddListener(() => _bootstrap?.RequestConfirmDockedSelection(capturedSeatId));
            useBtn.onClick.AddListener(() => _bootstrap?.RequestConfirmDockedSelection(capturedSeatId));
            sellBtn.onClick.AddListener(() => _bootstrap?.RequestConfirmDockedSelection(capturedSeatId));
            weaponMenuBtn.onClick.AddListener(() => _bootstrap?.RequestExitWeaponControl(capturedSeatId));
            weaponUndockBtn.onClick.AddListener(() => _bootstrap?.RequestToggleDock(capturedSeatId));

            var prompt = new SeatPromptCanvas
            {
                Root = root,
                RootRect = rootRect,
                Canvas = canvas,
                BorderImage = borderImg,
                HeaderBar = headerBar,
                HeaderTitle = headerTitle,
                PointerStem = pointerStem,
                BaseView = baseView,
                BaseSelectedLabel = baseSelectedLabel,
                BasePrevButton = basePrevBtn,
                BaseNextButton = baseNextBtn,
                BaseBuyButton = baseBuyBtn,
                BaseBuyButtonText = baseBuyBtn.GetComponentInChildren<Text>(),
                BaseHintText = baseHintText,
                SlotPills = slotPills,
                StructureView = structureView,
                UsePillButton = useBtn,
                UsePillBg = useBtn.GetComponent<Image>(),
                UsePillText = useBtn.GetComponentInChildren<Text>(),
                SellPillButton = sellBtn,
                SellPillBg = sellBtn.GetComponent<Image>(),
                SellPillText = sellBtn.GetComponentInChildren<Text>(),
                StructureHintText = structureHintText,
                WeaponView = weaponView,
                WeaponHintText = weaponHintText,
                WeaponMenuButton = weaponMenuBtn,
                WeaponUndockButton = weaponUndockBtn
            };

            _seatPrompts[seatId] = prompt;
            return prompt;
        }

        private Font GetFont()
        {
            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }

        private Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null)
            {
                return _whiteSprite;
            }

            _whiteTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            _whiteTexture.SetPixels32(new[]
            {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255)
            });
            _whiteTexture.Apply();
            _whiteSprite = Sprite.Create(_whiteTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            _whiteSprite.hideFlags = HideFlags.DontSave;
            return _whiteSprite;
        }

        private static Image CreateStretchedImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Stretch(rect, 0f, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Image CreateInsetImage(string name, Transform parent, Sprite sprite, Color color, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Stretch(rect, inset, inset);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text CreateText(string name, Transform parent, Font font, string content, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = alignment;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            return txt;
        }

        private static Transform CreateRow(string name, Transform parent, float topY, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            SetAnchoredRow(go.GetComponent<RectTransform>(), topY, height);
            var group = go.GetComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            return go.transform;
        }

        private static Button CreateButton(string name, Transform parent, Font font, string label, float fixedWidth, Color bgColor, bool expandWidth = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            if (expandWidth || fixedWidth <= 0f)
            {
                layout.flexibleWidth = 1f;
            }
            else
            {
                layout.preferredWidth = fixedWidth;
                layout.minWidth = fixedWidth;
            }

            var img = go.GetComponent<Image>();
            img.sprite = null;
            img.color = bgColor;

            var btn = go.GetComponent<Button>();
            Text txt = CreateText("Text", go.transform, font, label, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(txt.rectTransform, 4f, 2f);
            return btn;
        }

        private static void Stretch(RectTransform rect, float padX, float padY)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }

        private static void SetAnchoredRow(RectTransform rect, float topY, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, topY);
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static string ResolveCardName(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.DisplayName
                : $"#{cardId}";
        }

        private static int ResolveCardCost(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.BuildCostGold
                : 0;
        }

        private static string Abbreviate(string displayName, int maxLen)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return "?";
            }

            return displayName.Length <= maxLen ? displayName : displayName.Substring(0, maxLen);
        }

        private sealed class SlotPill
        {
            public Image Background;
            public Text Label;
        }

        private sealed class SeatPromptCanvas
        {
            public GameObject Root;
            public RectTransform RootRect;
            public Canvas Canvas;
            public Image BorderImage;
            public Image HeaderBar;
            public Text HeaderTitle;
            public Image PointerStem;

            public GameObject BaseView;
            public Text BaseSelectedLabel;
            public Button BasePrevButton;
            public Button BaseNextButton;
            public Button BaseBuyButton;
            public Text BaseBuyButtonText;
            public Text BaseHintText;
            public SlotPill[] SlotPills;

            public GameObject StructureView;
            public Button UsePillButton;
            public Image UsePillBg;
            public Text UsePillText;
            public Button SellPillButton;
            public Image SellPillBg;
            public Text SellPillText;
            public Text StructureHintText;

            public GameObject WeaponView;
            public Text WeaponHintText;
            public Button WeaponMenuButton;
            public Button WeaponUndockButton;
        }
    }
}
