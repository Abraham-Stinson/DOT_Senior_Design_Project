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
        [SerializeField] private bool _renderLegacyImGui = false;

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
            if (!_renderLegacyImGui || _bootstrap == null)
            {
                return;
            }

            float width = 470f;
            float height = Mathf.Min(600f, Mathf.Max(280f, Screen.height - 32f));
            float x = Mathf.Max(16f, Screen.width - width - 16f);

            GUILayout.BeginArea(new Rect(x, 16f, width, height), GUI.skin.box);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Label("Savaş Alanı");

            if (_bootstrap.IsSessionDisconnected)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Bağlantı Kesildi");
                if (!string.IsNullOrEmpty(_bootstrap.LastError))
                {
                    GUILayout.Label(_bootstrap.LastError);
                }
                if (GUILayout.Button("Ana Menü"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.ReturnToMainMenu();
                }
                GUILayout.EndVertical();
            }
            else if (!_bootstrap.IsMatchInitialized && _bootstrap.ActiveMode == MatchMode.OnlinePvP)
            {
                GUILayout.Label("Oyuncular bekleniyor...");
            }

            if (_bootstrap.HasGameMode)
            {
                bool hasHqA = _bootstrap.TryGetHqHealth(TeamId.TeamA, out float hqACur, out float hqAMax);
                bool hasHqB = _bootstrap.TryGetHqHealth(TeamId.TeamB, out float hqBCur, out float hqBMax);
                if (hasHqA || hasHqB)
                {
                    string hqAText = hasHqA ? $"{hqACur:F0}/{hqAMax:F0}" : "Yıkıldı";
                    string hqBText = hasHqB ? $"{hqBCur:F0}/{hqBMax:F0}" : "Yıkıldı";
                    GUILayout.Label($"Mavi Üs: {hqAText}  |  Kırmızı Üs: {hqBText}");
                }
            }

            if (_bootstrap.CurrentPhase == MatchPhase.Ended)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                string winnerLabel = _bootstrap.WinnerTeam switch
                {
                    TeamId.TeamA => "ZAFER — MAVİ TAKIM",
                    TeamId.TeamB => "ZAFER — KIRMIZI TAKIM",
                    _ => "BERABERE"
                };
                GUILayout.Label(winnerLabel);
                if (!string.IsNullOrEmpty(_bootstrap.MatchOutcomeReason))
                {
                    GUILayout.Label(_bootstrap.MatchOutcomeReason);
                }
                if (GUILayout.Button("Ana Menü"))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.ReturnToMainMenu();
                }
                GUILayout.EndVertical();
            }
            else if (_bootstrap.CurrentPhase == MatchPhase.Playing)
            {
                GUILayout.Label(_bootstrap.ActiveMode == MatchMode.LocalPvP
                    ? "P1: WASD · E Kenetlen · Space Onay/Ateş · Q İptal\nP2: Oklar · Enter Kenetlen · RCtrl Onay/Ateş · Backspace İptal"
                    : "WASD/Oklar Hareket · E Kenetlen · Space/Tık Onay/Ateş · Q/Sağ Tık İptal");
            }

            int seatCount = _bootstrap.SeatCount;
            if (seatCount > 0)
            {
                CardCatalogSO catalog = _bootstrap.CardCatalog;
                bool canBuild = _bootstrap.CurrentPhase == MatchPhase.Playing;

                for (int i = 0; i < seatCount; i++)
                {
                    if (!_bootstrap.TryGetSeatEconomyInfo(
                            i,
                            out byte seatId,
                            out TeamId team,
                            out SeatControlType _,
                            out int gold,
                            out float incomePerSecond))
                    {
                        continue;
                    }

                    bool isLocalSeat = _bootstrap.IsSeatLocallyOwned(seatId);
                    string seatSuffix = _bootstrap.ActiveMode == MatchMode.OnlinePvP
                        ? (isLocalSeat ? " (Sen)" : " (Rakip)")
                        : (seatId == 0 ? " (P1)" : " (P2)");

                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"{FormatTeamName(team)}{seatSuffix}  ·  {gold}g (+{incomePerSecond:F0}/sn)");

                    if (_bootstrap.TryGetTeamTerritorySummary(team, out int zoneCount, out float _, out float _))
                    {
                        int teamStructures = _bootstrap.GetStructureCountForTeam(team);
                        GUILayout.Label($"  Yapı: {teamStructures}  ·  Bölge: {zoneCount}");
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
                        out bool targetIsConstructed,
                        out float targetBuildProgress,
                        out Vector2 _);

                    if (isCarrying)
                    {
                        string carriedName = ResolveCardName(carriedCardId, catalog);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"  Taşınıyor: {carriedName} ({reservedGold}g)");
                        if (canBuild && isLocalSeat && GUILayout.Button("Kur", GUILayout.Width(64f)))
                        {
                            GUIUtility.keyboardControl = 0;
                            _bootstrap.RequestDeployCarried(seatId);
                        }
                        if (canBuild && isLocalSeat && GUILayout.Button($"İptal (+{reservedGold}g)", GUILayout.Width(96f)))
                        {
                            GUIUtility.keyboardControl = 0;
                            _bootstrap.RequestCancelCarry(seatId);
                        }
                        GUILayout.EndHorizontal();
                    }
                    else if (isDocked)
                    {
                        GUILayout.Label(!targetIsConstructed
                            ? $"  Kenetli · İnşa: %{Mathf.Clamp(Mathf.RoundToInt(targetBuildProgress * 100f), 0, 99)}"
                            : "  Kenetli · Yapı menüsü açık");
                    }
                    else if (isLocalSeat)
                    {
                        GUILayout.Label("  Kart almak için Üs'e kenetlen.");
                    }

                    if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                    {
                        int highlightedSlot = isDocked && subMode == DockedInteractionSubMode.BaseDeckSelect
                            ? selectedDeckSlot
                            : (isCarrying ? carriedSlot : -1);
                        DrawDeckPreviewRow(gold, isCarrying ? carriedSlot : -1, highlightedSlot, _loadoutBuffer, catalog);
                    }

                    if (_bootstrap.TryGetLastBuildFeedback(seatId, out BuildFeedback feedback) && !feedback.Accepted)
                    {
                        string cardTitle = ResolveCardName(feedback.CardId, catalog);
                        GUILayout.Label($"  {cardTitle}: {FormatBuildRejectReason(feedback.RejectReason)}");
                    }

                    if (_bootstrap.TryGetLastActionNotice(seatId, out string actionNotice) && !string.IsNullOrEmpty(actionNotice))
                    {
                        GUILayout.Label("  " + actionNotice);
                    }

                    GUILayout.EndVertical();
                }
            }

            if (!string.IsNullOrEmpty(_bootstrap.LastError))
            {
                GUILayout.Label("Uyarı: " + _bootstrap.LastError);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Ana Menü"))
            {
                _bootstrap.ReturnToMainMenu();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string FormatTeamName(TeamId team)
        {
            return team switch
            {
                TeamId.TeamA => "Mavi",
                TeamId.TeamB => "Kırmızı",
                _ => "Tarafsız"
            };
        }

        private static string FormatBuildRejectReason(BuildRejectReason reason)
        {
            return reason switch
            {
                BuildRejectReason.InsufficientGold => "Altın yetersiz",
                BuildRejectReason.OutsideTerritory => "Bölge dışında",
                BuildRejectReason.OutOfBuildRange => "Menzil dışında",
                BuildRejectReason.TooFarFromGround => "Yere daha yakın olmalısın",
                BuildRejectReason.InvalidTerrain => "Zemin uygun değil",
                BuildRejectReason.OverlappingStructure => "Burada başka yapı var",
                BuildRejectReason.RequiresGoldDeposit => "Altın damarı gerekli",
                BuildRejectReason.DepositOccupied => "Bu damar dolu",
                BuildRejectReason.MustDockAtBase => "Üs'e kenetlenmelisin",
                BuildRejectReason.OnCooldown => "Biraz bekle",
                _ => "Kurulamadı"
            };
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
                    ? "ELDE"
                    : (isSelectedSlot ? $"[{vm.SlotIndex + 1}]" : $"{vm.SlotIndex + 1}.");
                string label = hasCard ? $"{prefix} {shortName}\n{cost}g" : $"{prefix} {shortName}";

                GUILayout.Box(label, GUILayout.Height(42f), GUILayout.ExpandWidth(true));
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
