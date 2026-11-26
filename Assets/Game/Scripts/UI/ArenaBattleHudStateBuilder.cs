using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.UI
{
    public readonly struct ArenaHudCardEntry
    {
        public readonly ushort CardId;
        public readonly string DisplayName;
        public readonly int BuildCostGold;

        public ArenaHudCardEntry(ushort cardId, string displayName, int buildCostGold)
        {
            CardId = cardId;
            DisplayName = displayName ?? string.Empty;
            BuildCostGold = buildCostGold;
        }
    }

    public sealed class ArenaSeatHudSnapshot
    {
        public byte SeatId;
        public TeamId Team;
        public bool Exists;
        public bool IsLocallyOwned;
        public int Gold;
        public float IncomePerSecond;
        public IReadOnlyList<ArenaHudCardEntry> DeckCards;
        public bool IsDockedAtBase;
        public int SelectedDeckSlot;
        public bool IsCarrying;
        public string CarriedCardName;
        public int CarriedReservedGold;
        public string StatusNotice;
    }

    public sealed class ArenaHudSnapshot
    {
        public MatchPhase Phase;
        public TeamId WinnerTeam;
        public string OutcomeReason;
        public bool IsDisconnected;
        public bool IsUsingPersistentOnlineSession;
        public bool HasPresetLoadout;
        public float BlueHqCurrent;
        public float BlueHqMax;
        public float RedHqCurrent;
        public float RedHqMax;
        public readonly ArenaSeatHudSnapshot LeftSeat = new() { SeatId = 0, Team = TeamId.TeamA };
        public readonly ArenaSeatHudSnapshot RightSeat = new() { SeatId = 1, Team = TeamId.TeamB };
    }

    public struct ArenaShopCardViewModel
    {
        public bool Occupied;
        public string Title;
        public string PriceText;
        public int GlyphIndex;
        public bool Affordable;
        public bool Selected;
    }

    public sealed class ArenaSeatHudState
    {
        public byte SeatId;
        public TeamId Team;
        public bool ShowGoldPanel;
        public string GoldText;
        public string IncomeText;
        public bool ShowShopPanel;
        public int SelectedSlot;
        public bool CanBuySelected;
        public string ShopTitle;
        public string SelectHint;
        public string BuyHint;
        public string LeaveHint;
        public bool ShowCarryPanel;
        public bool IsCarrying;
        public string DeployLabel;
        public string CancelLabel;
        public string StatusNotice;
        public readonly ArenaShopCardViewModel[] Cards = new ArenaShopCardViewModel[DeckRules.DeckSize];
    }

    public sealed class ArenaBattleHudState
    {
        public readonly ArenaSeatHudState LeftSeat = new();
        public readonly ArenaSeatHudState RightSeat = new();
        public int BlueHqCurrent;
        public int BlueHqMax;
        public int RedHqCurrent;
        public int RedHqMax;
        public float BlueHqFraction => BlueHqMax > 0 ? Mathf.Clamp01((float)BlueHqCurrent / BlueHqMax) : 0f;
        public float RedHqFraction => RedHqMax > 0 ? Mathf.Clamp01((float)RedHqCurrent / RedHqMax) : 0f;
        public bool PauseInteractable;
        public bool IsEnded;
        public bool ShowVictoryScreen;
        public string ResultHeader;
        public bool ShowRematchButton;
        public string PauseNote;
    }

    public static class ArenaBattleHudStateBuilder
    {
        public static ArenaBattleHudState Build(ArenaHudSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            bool isEnded = snapshot.IsDisconnected || snapshot.Phase == MatchPhase.Ended;
            bool isPlaying = !isEnded && snapshot.Phase == MatchPhase.Playing;

            int localOwnedCount = 0;
            if (snapshot.LeftSeat.Exists && snapshot.LeftSeat.IsLocallyOwned) localOwnedCount++;
            if (snapshot.RightSeat.Exists && snapshot.RightSeat.IsLocallyOwned) localOwnedCount++;

            var state = new ArenaBattleHudState
            {
                BlueHqCurrent = Mathf.Max(0, Mathf.RoundToInt(snapshot.BlueHqCurrent)),
                BlueHqMax = Mathf.Max(0, Mathf.RoundToInt(snapshot.BlueHqMax)),
                RedHqCurrent = Mathf.Max(0, Mathf.RoundToInt(snapshot.RedHqCurrent)),
                RedHqMax = Mathf.Max(0, Mathf.RoundToInt(snapshot.RedHqMax)),
                PauseInteractable = !isEnded,
                IsEnded = isEnded,
                ShowVictoryScreen = ShouldShowVictoryScreen(snapshot),
                ResultHeader = FormatResultHeader(snapshot),
                ShowRematchButton = !snapshot.IsUsingPersistentOnlineSession && !snapshot.IsDisconnected,
                PauseNote = snapshot.IsUsingPersistentOnlineSession
                    ? "Online session continues • local menu"
                    : "Match paused"
            };

            PopulateSeatState(snapshot, snapshot.LeftSeat, localOwnedCount, isPlaying, isEnded, state.LeftSeat);
            PopulateSeatState(snapshot, snapshot.RightSeat, localOwnedCount, isPlaying, isEnded, state.RightSeat);

            return state;
        }

        private static void PopulateSeatState(
            ArenaHudSnapshot root,
            ArenaSeatHudSnapshot seat,
            int localOwnedCount,
            bool isPlaying,
            bool isEnded,
            ArenaSeatHudState target)
        {
            target.SeatId = seat.SeatId;
            target.Team = seat.Team;

            bool showOwned = seat.Exists && seat.IsLocallyOwned;
            target.ShowGoldPanel = showOwned;
            target.GoldText = seat.Gold.ToString();
            int perMin = Mathf.RoundToInt(seat.IncomePerSecond * 60f);
            target.IncomeText = perMin >= 0 ? $"+{perMin}/m" : $"{perMin}/m";

            bool usePlayerTwoBindings = localOwnedCount >= 2 && seat.SeatId == 1;
            string teamTitle = seat.Team == TeamId.TeamB ? "RED BASE • BUILDINGS" : "BLUE BASE • BUILDINGS";
            string selectHint = usePlayerTwoBindings ? "←/→ Select" : "A/D Select";
            string buyKey = usePlayerTwoBindings ? "RCtrl" : "Space";
            string leaveKey = usePlayerTwoBindings ? "Enter" : "E";
            string cancelKey = usePlayerTwoBindings ? "Bksp" : "Q";

            target.ShopTitle = teamTitle;
            target.SelectHint = selectHint;
            target.BuyHint = $"{buyKey}  BUY";
            target.LeaveHint = $"{leaveKey}  LEAVE";

            bool showShop = isPlaying && showOwned && seat.IsDockedAtBase;
            target.ShowShopPanel = showShop;
            target.SelectedSlot = Mathf.Clamp(seat.SelectedDeckSlot, 0, DeckRules.DeckSize - 1);

            int selectedCost = 0;
            bool hasSelectedCard = false;
            Array.Clear(target.Cards, 0, target.Cards.Length);

            if (seat.DeckCards != null)
            {
                int count = Mathf.Min(target.Cards.Length, seat.DeckCards.Count);
                for (int i = 0; i < count; i++)
                {
                    ArenaHudCardEntry entry = seat.DeckCards[i];
                    if (entry.CardId == 0)
                    {
                        continue;
                    }

                    bool affordable = seat.Gold >= entry.BuildCostGold;
                    bool selected = i == target.SelectedSlot;
                    target.Cards[i] = new ArenaShopCardViewModel
                    {
                        Occupied = true,
                        Title = string.IsNullOrEmpty(entry.DisplayName) ? $"#{entry.CardId}" : entry.DisplayName,
                        PriceText = $"{entry.BuildCostGold}g",
                        GlyphIndex = MapFallbackGlyph(entry.CardId),
                        Affordable = affordable,
                        Selected = selected
                    };

                    if (selected)
                    {
                        hasSelectedCard = true;
                        selectedCost = entry.BuildCostGold;
                    }
                }
            }

            target.CanBuySelected = showShop && hasSelectedCard && seat.Gold >= selectedCost;

            string statusNotice = seat.StatusNotice ?? string.Empty;
            if (string.IsNullOrEmpty(statusNotice) && !isEnded && showOwned && root.Phase == MatchPhase.PreMatchDraft)
            {
                statusNotice = root.HasPresetLoadout ? "Starting match..." : "Select your deck...";
            }

            bool showCarry = isPlaying && showOwned && !showShop && seat.IsCarrying;
            bool showFeedback = !isEnded && showOwned && !showShop && !string.IsNullOrEmpty(statusNotice);
            target.ShowCarryPanel = showCarry || showFeedback;
            target.IsCarrying = showCarry;
            target.StatusNotice = statusNotice;

            if (showCarry)
            {
                string carriedTitle = string.IsNullOrEmpty(seat.CarriedCardName) ? "Structure" : seat.CarriedCardName;
                target.DeployLabel = $"{buyKey}  DEPLOY {carriedTitle.ToUpperInvariant()}";
                target.CancelLabel = $"{cancelKey}  CANCEL (+{seat.CarriedReservedGold}g)";
            }
            else
            {
                target.DeployLabel = string.Empty;
                target.CancelLabel = string.Empty;
            }
        }

        public static bool ShouldShowVictoryScreen(ArenaHudSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsDisconnected || snapshot.Phase != MatchPhase.Ended)
            {
                return false;
            }

            int ownedCount = 0;
            TeamId singleOwnedTeam = TeamId.Neutral;
            if (snapshot.LeftSeat.Exists && snapshot.LeftSeat.IsLocallyOwned)
            {
                ownedCount++;
                singleOwnedTeam = snapshot.LeftSeat.Team;
            }
            if (snapshot.RightSeat.Exists && snapshot.RightSeat.IsLocallyOwned)
            {
                ownedCount++;
                singleOwnedTeam = snapshot.RightSeat.Team;
            }

            if (ownedCount >= 2 || ownedCount == 0)
            {
                return snapshot.WinnerTeam != TeamId.Neutral;
            }

            return snapshot.WinnerTeam != TeamId.Neutral && snapshot.WinnerTeam == singleOwnedTeam;
        }

        public static string FormatResultHeader(ArenaHudSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            if (snapshot.IsDisconnected)
            {
                return string.IsNullOrEmpty(snapshot.OutcomeReason)
                    ? "CONNECTION LOST"
                    : $"CONNECTION LOST — {snapshot.OutcomeReason}";
            }

            string teamTitle = snapshot.WinnerTeam switch
            {
                TeamId.TeamA => "BLUE TEAM VICTORY",
                TeamId.TeamB => "RED TEAM VICTORY",
                _ => "DRAW"
            };

            return string.IsNullOrEmpty(snapshot.OutcomeReason)
                ? teamTitle
                : $"{teamTitle} — {snapshot.OutcomeReason}";
        }

        private static int MapFallbackGlyph(ushort cardId)
        {
            return cardId switch
            {
                1 => 0,  // Balloon (circle)
                20 => 1, // Gold Mine (triangle)
                10 => 2, // Cannon (plus/cross)
                _ => 3   // Diamond / Tower
            };
        }
    }
}
