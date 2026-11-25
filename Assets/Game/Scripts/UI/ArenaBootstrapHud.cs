using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Minimal greybox HUD displaying synchronized per-seat economy, territory extent,
    /// carry-and-place state, HQ health, and match outcome without FishNet coupling.
    /// Docked structure prompts are rendered in-world by DockedStructureWorldUI.
    /// </summary>
    public sealed class ArenaBootstrapHud : MonoBehaviour
    {
        [SerializeField] private ArenaSessionBootstrap _bootstrap = null;

        private readonly List<ushort> _loadoutBuffer = new();
        private Vector2 _scrollPosition;

        private void Awake()
        {
            if (!TryGetComponent(out DockedStructureWorldUI worldUi))
            {
                worldUi = gameObject.AddComponent<DockedStructureWorldUI>();
            }

            worldUi.BindBootstrap(_bootstrap);
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

            int seatCount = _bootstrap.SeatCount;
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
                        out DockedTargetKind _,
                        out DockedInteractionSubMode subMode,
                        out int selectedDeckSlot,
                        out DockedStructureAction _,
                        out ushort _,
                        out int _,
                        out bool _,
                        out bool _,
                        out float _,
                        out Vector2 _);

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
                        GUILayout.Label($"  Kenetli ({subMode}) · Panel yapının üzerinde açık.");
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
