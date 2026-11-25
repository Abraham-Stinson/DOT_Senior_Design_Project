using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Minimal greybox HUD displaying synchronized per-seat economy, territory extent,
    /// carry-and-place state, available gold deposits, and clickable 5-slot build hotbar without FishNet coupling.
    /// </summary>
    public sealed class ArenaBootstrapHud : MonoBehaviour
    {
        [SerializeField] private ArenaSessionBootstrap _bootstrap = null;

        private struct WorldDockedPromptData
        {
            public byte SeatId;
            public TeamId Team;
            public int Gold;
            public bool CanAct;
            public DockedTargetKind TargetKind;
            public DockedInteractionSubMode SubMode;
            public int SelectedDeckSlot;
            public DockedStructureAction SelectedAction;
            public ushort TargetCardId;
            public int SellRefundGold;
            public bool CanUseTarget;
            public float TargetCooldownRemaining;
            public Vector2 WorldAnchor;
        }

        private readonly List<ushort> _loadoutBuffer = new();
        private readonly List<WorldDockedPromptData> _worldPrompts = new();
        private Vector2 _scrollPosition;
        private GUIStyle _floatingBoxStyle;
        private Texture2D _floatingBgTexture;

        private void OnDestroy()
        {
            if (_floatingBgTexture != null)
            {
                Destroy(_floatingBgTexture);
                _floatingBgTexture = null;
            }
        }

        private void OnGUI()
        {
            if (_bootstrap == null)
            {
                return;
            }

            float width = 470f;
            float height = Mathf.Min(600f, Mathf.Max(280f, Screen.height - 32f));
            float x = Mathf.Max(16f, Screen.width - width - 16f);

            GUILayout.BeginArea(new Rect(x, 16f, width, height), GUI.skin.box);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Label("CannonBrawlArena (M1.5 Savaş)");
            GUILayout.Label($"Mod: {_bootstrap.ActiveMode} | Transport: {_bootstrap.TransportModeSummary}");
            GUILayout.Label($"Oturum: {_bootstrap.SessionStateSummary} | Başlatıldı: {_bootstrap.IsMatchInitialized}");

            if (_bootstrap.HasGameMode)
            {
                GUILayout.Label($"Maç Fazı: {_bootstrap.CurrentPhase} | Yapı: {_bootstrap.BuiltStructureCount} | Boş Damar: {_bootstrap.AvailableGoldDepositCount}");

                bool hasHqA = _bootstrap.TryGetHqHealth(TeamId.TeamA, out float hqACur, out float hqAMax);
                bool hasHqB = _bootstrap.TryGetHqHealth(TeamId.TeamB, out float hqBCur, out float hqBMax);
                if (hasHqA || hasHqB)
                {
                    string hqAText = hasHqA ? $"{hqACur:F0}/{hqAMax:F0}" : "YOK EDİLDİ";
                    string hqBText = hasHqB ? $"{hqBCur:F0}/{hqBMax:F0}" : "YOK EDİLDİ";
                    GUILayout.Label($"HQ Can · TeamA: {hqAText}  |  TeamB: {hqBText}");
                }
            }

            if (_bootstrap.CurrentPhase == MatchPhase.Ended)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                string winnerLabel = _bootstrap.WinnerTeam == TeamId.Neutral
                    ? "MAÇ SONUCU: BERABERE"
                    : $"MAÇ BİTTİ — KAZANAN: {_bootstrap.WinnerTeam}";
                GUILayout.Label(winnerLabel);
                if (!string.IsNullOrEmpty(_bootstrap.MatchOutcomeReason))
                {
                    GUILayout.Label(_bootstrap.MatchOutcomeReason);
                }
                if (GUILayout.Button("Ana Menüye Dön (Maç Sonu)"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.ReturnToMainMenu();
                }
                GUILayout.EndVertical();
            }
            else if (_bootstrap.CurrentPhase == MatchPhase.Playing)
            {
                GUILayout.Label(_bootstrap.ActiveMode == MatchMode.LocalPvP
                    ? "P1: WASD(Uç/A-D Seç/Nişan) + E(Kenetlen/Ayrıl) + Space(Onayla/Ateş/Bırak) + Q(İptal)\nP2: Oklar(Uç/Sol-Sağ Seç/Nişan) + Enter(Kenetlen/Ayrıl) + RCtrl/Num0(Onayla/Ateş/Bırak) + Backspace"
                    : "Kontroller · WASD/Oklar(Uç/Seç) + E/F(Kenetlen/Ayrıl) + Space/Tık(Onayla/Ateş/Bırak) + Q/SağTık(İptal)");
            }

            _worldPrompts.Clear();
            int seatCount = _bootstrap.SeatCount;
            Camera worldCam = Camera.main;
            bool canProjectWorldUi = worldCam != null;

            if (seatCount > 0)
            {
                CardCatalogSO catalog = _bootstrap.CardCatalog;
                bool canBuild = _bootstrap.CurrentPhase == MatchPhase.Playing;
                GUILayout.Label($"Koltuk Sayısı: {seatCount}");

                for (int i = 0; i < seatCount; i++)
                {
                    if (!_bootstrap.TryGetSeatEconomyInfo(
                            i,
                            out byte seatId,
                            out TeamId team,
                            out SeatControlType controlType,
                            out int gold,
                            out float incomePerSecond))
                    {
                        continue;
                    }

                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"Seat {seatId}: {team} ({controlType}) | Altın: {gold} (+{incomePerSecond:F1}/sn)");

                    if (_bootstrap.TryGetTeamTerritorySummary(team, out int zoneCount, out float minX, out float maxX))
                    {
                        int teamStructures = _bootstrap.GetStructureCountForTeam(team);
                        GUILayout.Label($"  Bölge: {zoneCount} zon [X: {minX:F1}..{maxX:F1}] | Yapı: {teamStructures}");
                    }

                    if (_bootstrap.TryGetAirshipInfo(seatId, out Vector2 shipPos, out AirshipDockMode dockMode))
                    {
                        string hpText = string.Empty;
                        if (_bootstrap.TryGetAirshipHealth(seatId, out float shipHp, out float shipMaxHp, out bool isDowned))
                        {
                            hpText = isDowned
                                ? " | DÜŞTÜ (Yeniden Doğuyor)"
                                : $" | Can: {shipHp:F0}/{shipMaxHp:F0}";
                        }

                        GUILayout.Label($"  Airship: ({shipPos.x:F1}, {shipPos.y:F1}) · {dockMode}{hpText}");
                    }

                    bool isCarrying = _bootstrap.TryGetAirshipCarryInfo(
                        seatId,
                        out _,
                        out ushort carriedCardId,
                        out int carriedSlot,
                        out int reservedGold);

                    bool isDocked = _bootstrap.TryGetDockedInteractionInfo(
                        seatId,
                        out DockedTargetKind targetKind,
                        out DockedInteractionSubMode subMode,
                        out int selectedDeckSlot,
                        out DockedStructureAction selectedAction,
                        out ushort targetCardId,
                        out int sellRefundGold,
                        out bool canUseTarget,
                        out float targetCooldownRemaining,
                        out Vector2 dockedWorldAnchor);

                    if (isCarrying)
                    {
                        string carriedName = ResolveCardName(carriedCardId, catalog);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"  Taşınıyor: {carriedName} ({reservedGold}g)");
                        if (canBuild && GUILayout.Button("Yerleştir", GUILayout.Width(80f)))
                        {
                            GUIUtility.keyboardControl = 0;
                            _bootstrap.RequestDeployCarried(seatId);
                        }
                        if (canBuild && GUILayout.Button($"İptal (+{reservedGold}g)", GUILayout.Width(96f)))
                        {
                            GUIUtility.keyboardControl = 0;
                            _bootstrap.RequestCancelCarry(seatId);
                        }
                        GUILayout.EndHorizontal();
                    }
                    else if (isDocked)
                    {
                        if (canProjectWorldUi)
                        {
                            _worldPrompts.Add(new WorldDockedPromptData
                            {
                                SeatId = seatId,
                                Team = team,
                                Gold = gold,
                                CanAct = canBuild,
                                TargetKind = targetKind,
                                SubMode = subMode,
                                SelectedDeckSlot = selectedDeckSlot,
                                SelectedAction = selectedAction,
                                TargetCardId = targetCardId,
                                SellRefundGold = sellRefundGold,
                                CanUseTarget = canUseTarget,
                                TargetCooldownRemaining = targetCooldownRemaining,
                                WorldAnchor = dockedWorldAnchor
                            });
                            GUILayout.Label($"  Kenetli ({subMode}) · Panel yapı üzerinde açık.");
                        }
                        else
                        {
                            DrawDockedInteractionPanel(
                                seatId,
                                team,
                                gold,
                                canBuild,
                                targetKind,
                                subMode,
                                selectedDeckSlot,
                                selectedAction,
                                targetCardId,
                                sellRefundGold,
                                canUseTarget,
                                targetCooldownRemaining,
                                catalog);
                        }
                    }
                    else
                    {
                        GUILayout.Label("  Kart seçmek ve taşımak için Base (HQ)'ya kenetlen (E / Enter).");
                    }

                    if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                    {
                        int highlightedSlot = isDocked && subMode == DockedInteractionSubMode.BaseDeckSelect
                            ? selectedDeckSlot
                            : (isCarrying ? carriedSlot : -1);
                        DrawDeckPreviewRow(gold, isCarrying ? carriedSlot : -1, highlightedSlot, _loadoutBuffer, catalog);
                    }

                    if (_bootstrap.TryGetLastBuildFeedback(seatId, out BuildFeedback feedback))
                    {
                        string cardTitle = ResolveCardName(feedback.CardId, catalog);
                        string outcomeText = feedback.Accepted
                            ? $"Onay ({cardTitle} @ {feedback.Position.x:F1}, {feedback.Position.y:F1})"
                            : $"Red ({cardTitle}): {feedback.RejectReason}";
                        GUILayout.Label("  " + outcomeText);
                    }

                    if (_bootstrap.TryGetLastActionNotice(seatId, out string actionNotice) && !string.IsNullOrEmpty(actionNotice))
                    {
                        GUILayout.Label("  Son İşlem: " + actionNotice);
                    }

                    GUILayout.EndVertical();
                }
            }

            if (!string.IsNullOrEmpty(_bootstrap.LastError))
            {
                GUILayout.Label("Hata: " + _bootstrap.LastError);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Ana Menüye Dön"))
            {
                _bootstrap.ReturnToMainMenu();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (canProjectWorldUi && _worldPrompts.Count > 0)
            {
                CardCatalogSO catalog = _bootstrap.CardCatalog;
                for (int i = 0; i < _worldPrompts.Count; i++)
                {
                    DrawWorldAnchoredDockedPrompt(worldCam, _worldPrompts[i], catalog);
                }
            }
        }

        private void DrawWorldAnchoredDockedPrompt(Camera worldCam, WorldDockedPromptData prompt, CardCatalogSO catalog)
        {
            Vector3 screenPt = worldCam.WorldToScreenPoint(new Vector3(prompt.WorldAnchor.x, prompt.WorldAnchor.y + 1.15f, 0f));
            if (screenPt.z <= 0f)
            {
                return;
            }

            float panelWidth = prompt.SubMode == DockedInteractionSubMode.BaseDeckSelect ? 330f : 290f;
            float panelHeight = prompt.SubMode == DockedInteractionSubMode.BaseDeckSelect ? 116f : 96f;

            float rawX = screenPt.x - panelWidth * 0.5f;
            float rawY = (Screen.height - screenPt.y) - panelHeight - 10f;

            float clampedX = Mathf.Clamp(rawX, 12f, Mathf.Max(12f, Screen.width - panelWidth - 12f));
            float clampedY = Mathf.Clamp(rawY, 12f, Mathf.Max(12f, Screen.height - panelHeight - 12f));

            EnsureFloatingBoxStyle();
            GUILayout.BeginArea(new Rect(clampedX, clampedY, panelWidth, panelHeight), _floatingBoxStyle);
            DrawDockedInteractionPanel(
                prompt.SeatId,
                prompt.Team,
                prompt.Gold,
                prompt.CanAct,
                prompt.TargetKind,
                prompt.SubMode,
                prompt.SelectedDeckSlot,
                prompt.SelectedAction,
                prompt.TargetCardId,
                prompt.SellRefundGold,
                prompt.CanUseTarget,
                prompt.TargetCooldownRemaining,
                catalog);
            GUILayout.EndArea();
        }

        private void EnsureFloatingBoxStyle()
        {
            if (_floatingBoxStyle != null && _floatingBgTexture != null)
            {
                return;
            }

            _floatingBgTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave
            };
            var bgColor = new Color32(16, 22, 34, 232);
            _floatingBgTexture.SetPixels32(new[] { bgColor, bgColor, bgColor, bgColor });
            _floatingBgTexture.Apply();

            _floatingBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6)
            };
            _floatingBoxStyle.normal.background = _floatingBgTexture;
        }

        private void DrawDockedInteractionPanel(
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
            float targetCooldownRemaining,
            CardCatalogSO catalog)
        {
            string playerTag = _bootstrap.ActiveMode == MatchMode.LocalPvP
                ? (seatId == 0 ? $"P1 ({team})" : $"P2 ({team})")
                : $"{team}";
            string hintKeys = _bootstrap.ActiveMode == MatchMode.LocalPvP
                ? (seatId == 0 ? "A/D · Space · E" : "←/→ · RCtrl · Enter")
                : "A/D · Space · E";

            if (subMode == DockedInteractionSubMode.BaseDeckSelect || targetKind == DockedTargetKind.Base)
            {
                string cardName = ResolveCardName(targetCardId, catalog);
                int cost = ResolveCardCost(targetCardId, catalog);
                bool canAfford = gold >= cost;

                GUILayout.Label($"{playerTag} · BASE [{hintKeys}]");
                GUILayout.Label($"Slot {selectedDeckSlot + 1}: {cardName} ({cost}g)" + (canAfford ? "" : " · Yetersiz"));
                GUILayout.BeginHorizontal();
                bool prevEnabled = GUI.enabled;
                GUI.enabled = canAct;

                if (GUILayout.Button("<", GUILayout.Width(32f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestCycleDockedSelection(seatId, -1);
                }

                GUI.enabled = canAct && canAfford && targetCardId != 0;
                if (GUILayout.Button($"Al & Taşı ({cost}g)"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestConfirmDockedSelection(seatId);
                }

                GUI.enabled = canAct;
                if (GUILayout.Button(">", GUILayout.Width(32f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestCycleDockedSelection(seatId, 1);
                }

                if (GUILayout.Button("Ayrıl", GUILayout.Width(50f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestToggleDock(seatId);
                }

                GUI.enabled = prevEnabled;
                GUILayout.EndHorizontal();

                if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                {
                    GUILayout.BeginHorizontal();
                    for (int s = 0; s < _loadoutBuffer.Count; s++)
                    {
                        bool active = s == selectedDeckSlot;
                        string n = ResolveCardName(_loadoutBuffer[s], catalog);
                        GUILayout.Box(active ? $">{Abbreviate(n)}<" : $"{s + 1}:{Abbreviate(n)}", GUILayout.Height(20f), GUILayout.ExpandWidth(true));
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else if (subMode == DockedInteractionSubMode.WeaponManualControl)
            {
                string structureName = ResolveCardName(targetCardId, catalog);
                string fireHint = _bootstrap.ActiveMode == MatchMode.LocalPvP
                    ? (seatId == 0 ? "WASD Nişan · Space Ateş · Q Menü" : "Oklar Nişan · RCtrl Ateş · Backspace Menü")
                    : "Hareket/Fare Nişan · Tık/Space Ateş · Q Menü";
                GUILayout.Label($"{playerTag} · {structureName} [SİLAH KONTROLÜ]");
                GUILayout.Label(fireHint);
                GUILayout.BeginHorizontal();
                bool prevEnabled = GUI.enabled;
                GUI.enabled = canAct;
                if (GUILayout.Button("Menüye Dön"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestExitWeaponControl(seatId);
                }
                if (GUILayout.Button("Ayrıl", GUILayout.Width(54f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestToggleDock(seatId);
                }
                GUI.enabled = prevEnabled;
                GUILayout.EndHorizontal();
            }
            else
            {
                string structureName = ResolveCardName(targetCardId, catalog);
                string useLabel = canUseTarget
                    ? "KULLAN"
                    : (targetCooldownRemaining > 0.05f ? $"KULLAN ({targetCooldownRemaining:F1}s)" : "KULLAN (Yok)");
                string sellLabel = $"SAT (+{sellRefundGold}g)";

                string useHighlight = selectedAction == DockedStructureAction.Use ? $"> [{useLabel}] <" : useLabel;
                string sellHighlight = selectedAction == DockedStructureAction.Sell ? $"> [{sellLabel}] <" : sellLabel;

                GUILayout.Label($"{playerTag} · {structureName} [{hintKeys}]");
                GUILayout.Label($"{useHighlight}  |  {sellHighlight}");
                GUILayout.BeginHorizontal();
                bool prevEnabled = GUI.enabled;
                GUI.enabled = canAct;

                if (GUILayout.Button("<", GUILayout.Width(32f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestCycleDockedSelection(seatId, -1);
                }

                bool canConfirm = selectedAction == DockedStructureAction.Sell || canUseTarget;
                GUI.enabled = canAct && canConfirm;
                if (GUILayout.Button(selectedAction == DockedStructureAction.Sell ? $"Onayla: {sellLabel}" : $"Onayla: {useLabel}"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestConfirmDockedSelection(seatId);
                }

                GUI.enabled = canAct;
                if (GUILayout.Button(">", GUILayout.Width(32f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestCycleDockedSelection(seatId, 1);
                }

                if (GUILayout.Button("Ayrıl", GUILayout.Width(50f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestToggleDock(seatId);
                }

                GUI.enabled = prevEnabled;
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawDeckPreviewRow(
            int gold,
            int carriedSlot,
            int highlightedSlot,
            List<ushort> cardIds,
            CardCatalogSO catalog)
        {
            GUILayout.BeginHorizontal();

            for (int slotIndex = 0; slotIndex < cardIds.Count; slotIndex++)
            {
                ushort cardId = cardIds[slotIndex];
                StructureCardSO card = null;
                bool hasCard = catalog != null && catalog.TryGetCard(cardId, out card) && card != null;
                int cost = hasCard ? card.BuildCostGold : 0;
                bool isLockedZero = slotIndex == 0 || (hasCard && card.IsLockedInSlotZero);
                var vm = new BuildSlotViewModel(slotIndex, cardId, isLockedZero, gold >= cost);
                bool isCarriedSlot = slotIndex == carriedSlot;
                bool isSelectedSlot = slotIndex == highlightedSlot;

                string shortName = hasCard ? Abbreviate(card.DisplayName) : $"#{vm.CardId}";
                string prefix = isCarriedSlot
                    ? "TAŞI*"
                    : (isSelectedSlot ? $">S{vm.SlotIndex}<" : (vm.IsLockedSlotZero ? "S0*" : $"S{vm.SlotIndex}"));
                string label = hasCard ? $"{prefix}\n{shortName}\n({cost}g)" : $"{prefix}\n{shortName}";

                GUILayout.Box(label, GUILayout.Height(46f), GUILayout.ExpandWidth(true));
            }

            GUILayout.EndHorizontal();
        }

        private static int ResolveCardCost(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.BuildCostGold
                : 0;
        }

        private static string ResolveCardName(ushort cardId, CardCatalogSO catalog)
        {
            return catalog != null && catalog.TryGetCard(cardId, out StructureCardSO card) && card != null
                ? card.DisplayName
                : $"#{cardId}";
        }

        private static string Abbreviate(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return "?";
            }

            return displayName.Length <= 8 ? displayName : displayName.Substring(0, 8);
        }
    }
}
