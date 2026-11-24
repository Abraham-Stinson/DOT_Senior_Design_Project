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

        private readonly List<ushort> _loadoutBuffer = new();
        private Vector2 _scrollPosition;

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
                    ? "P1: WASD(Uç/Nişan) + E(Topa Gir) + [1-5](Taşı) + Space(Bırak/Ateş) + Q(İptal)\nP2: Oklar(Uç/Nişan) + Enter(Topa Gir) + [6-0/Num1-5] + RCtrl/Num0(Bırak/Ateş) + Backspace"
                    : "Kontroller · WASD/Fare(Nişan) + E/F(Topa Gir) + [1-5](Taşı) + Space/Tık(Bırak/Ateş) + Q/SağTık(İptal)");
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

                    if (_bootstrap.TryCopyActiveLoadout(seatId, _loadoutBuffer))
                    {
                        DrawBuildHotbar(seatId, gold, canBuild, isCarrying ? carriedSlot : -1, _loadoutBuffer, catalog);
                    }

                    if (_bootstrap.TryGetLastBuildFeedback(seatId, out BuildFeedback feedback))
                    {
                        string cardTitle = ResolveCardName(feedback.CardId, catalog);
                        string outcomeText = feedback.Accepted
                            ? $"Onay ({cardTitle} @ {feedback.Position.x:F1}, {feedback.Position.y:F1})"
                            : $"Red ({cardTitle}): {feedback.RejectReason}";
                        GUILayout.Label("  " + outcomeText);
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

        private void DrawBuildHotbar(
            byte seatId,
            int gold,
            bool canBuild,
            int carriedSlot,
            List<ushort> cardIds,
            CardCatalogSO catalog)
        {
            GUILayout.BeginHorizontal();
            bool prevEnabled = GUI.enabled;

            for (int slotIndex = 0; slotIndex < cardIds.Count; slotIndex++)
            {
                ushort cardId = cardIds[slotIndex];
                StructureCardSO card = null;
                bool hasCard = catalog != null && catalog.TryGetCard(cardId, out card) && card != null;
                int cost = hasCard ? card.BuildCostGold : 0;
                bool isLockedZero = slotIndex == 0 || (hasCard && card.IsLockedInSlotZero);
                var vm = new BuildSlotViewModel(slotIndex, cardId, isLockedZero, gold >= cost);
                bool isCarriedSlot = slotIndex == carriedSlot;

                string shortName = hasCard ? Abbreviate(card.DisplayName) : $"#{vm.CardId}";
                string prefix = isCarriedSlot ? "TAŞI*" : (vm.IsLockedSlotZero ? "S0*" : $"S{vm.SlotIndex}");
                string label = hasCard ? $"{prefix}\n{shortName}\n({cost}g)" : $"{prefix}\n{shortName}";

                GUI.enabled = canBuild && (vm.IsAffordable || isCarriedSlot);
                if (GUILayout.Button(label, GUILayout.Height(46f)))
                {
                    GUIUtility.keyboardControl = 0;
                    _bootstrap.RequestBuildFromSlot(seatId, vm.SlotIndex);
                }
            }

            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();
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
