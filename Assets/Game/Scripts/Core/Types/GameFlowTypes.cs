using System;
using System.Collections.Generic;

namespace Game.Core
{
    public static class GameSceneNames
    {
        public const string MainMenu = "MainMenu";
        public const string CannonBrawlArena = "CannonBrawlArena";
        public const string SteamOnlineLobby = "NetworkTest";
    }

    public enum MenuNavigationAction : byte
    {
        LoadArenaLocalPvP = 0,
        LoadSteamOnlineLobby = 1,
        BlockedWorkInProgress = 2
    }

    public readonly struct MenuRouteDecision
    {
        public readonly MatchMode Mode;
        public readonly MenuNavigationAction Action;
        public readonly string TargetScene;
        public readonly bool IsEnabled;
        public readonly string StatusMessage;

        public MenuRouteDecision(
            MatchMode mode,
            MenuNavigationAction action,
            string targetScene,
            bool isEnabled,
            string statusMessage)
        {
            Mode = mode;
            Action = action;
            TargetScene = targetScene ?? string.Empty;
            IsEnabled = isEnabled;
            StatusMessage = statusMessage ?? string.Empty;
        }
    }

    public static class MainMenuRouter
    {
        public static MenuRouteDecision Resolve(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.LocalPvP:
                    return new MenuRouteDecision(
                        MatchMode.LocalPvP,
                        MenuNavigationAction.LoadArenaLocalPvP,
                        GameSceneNames.CannonBrawlArena,
                        true,
                        "Arena yükleniyor...");

                case MatchMode.OnlinePvP:
                    return new MenuRouteDecision(
                        MatchMode.OnlinePvP,
                        MenuNavigationAction.LoadSteamOnlineLobby,
                        GameSceneNames.SteamOnlineLobby,
                        true,
                        "Steam lobisi açılıyor...");

                case MatchMode.Story:
                default:
                    return new MenuRouteDecision(
                        MatchMode.Story,
                        MenuNavigationAction.BlockedWorkInProgress,
                        string.Empty,
                        false,
                        "Hikaye modu yakında eklenecek.");
            }
        }

        public static bool CanLaunch(MatchMode mode)
        {
            return Resolve(mode).IsEnabled;
        }
    }

    /// <summary>
    /// Lightweight cross-scene handoff container carrying the menu's selected MatchMode
    /// and any pre-selected deck loadouts from preparation/lobby screens.
    /// </summary>
    public static class MatchLaunchConfig
    {
        private static readonly ushort[][] s_seatPresetLoadouts = new ushort[2][];
        private static ushort[] s_localOnlinePresetLoadout;

        public static MatchMode SelectedMode { get; private set; } = MatchMode.LocalPvP;
        public static bool HasExplicitSelection { get; private set; }

        public static bool HasAnyPresetLoadout =>
            s_seatPresetLoadouts[0] != null
            || s_seatPresetLoadouts[1] != null
            || s_localOnlinePresetLoadout != null;

        public static void SelectMode(MatchMode mode)
        {
            SelectedMode = mode;
            HasExplicitSelection = true;
        }

        public static void SetPresetLoadout(byte seatId, ushort[] cardIds)
        {
            if (seatId >= s_seatPresetLoadouts.Length)
            {
                return;
            }

            s_seatPresetLoadouts[seatId] = cardIds != null ? (ushort[])cardIds.Clone() : null;
        }

        public static bool TryGetPresetLoadout(byte seatId, out ushort[] cardIds)
        {
            if (seatId < s_seatPresetLoadouts.Length && s_seatPresetLoadouts[seatId] != null)
            {
                cardIds = (ushort[])s_seatPresetLoadouts[seatId].Clone();
                return true;
            }

            cardIds = null;
            return false;
        }

        public static bool HasPresetForSeat(byte seatId)
        {
            return (seatId < s_seatPresetLoadouts.Length && s_seatPresetLoadouts[seatId] != null)
                || s_localOnlinePresetLoadout != null;
        }

        public static void SetLocalOnlinePresetLoadout(ushort[] cardIds)
        {
            s_localOnlinePresetLoadout = cardIds != null ? (ushort[])cardIds.Clone() : null;
        }

        public static bool TryGetLocalOnlinePresetLoadout(out ushort[] cardIds)
        {
            if (s_localOnlinePresetLoadout != null)
            {
                cardIds = (ushort[])s_localOnlinePresetLoadout.Clone();
                return true;
            }

            cardIds = null;
            return false;
        }

        public static void Reset()
        {
            SelectedMode = MatchMode.LocalPvP;
            HasExplicitSelection = false;
            s_seatPresetLoadouts[0] = null;
            s_seatPresetLoadouts[1] = null;
            s_localOnlinePresetLoadout = null;
        }
    }

    /// <summary>
    /// Maps Tulpar UI PreviewCatalog card indices into Game.Data CardIds and normalizes
    /// partial preparation decks into valid 5-card decks for DraftLoadoutController.
    /// </summary>
    public static class MatchLoadoutHandoffResolver
    {
        public static ushort MapPreviewCardIndexToGameplayId(int previewCardIndex)
        {
            return previewCardIndex switch
            {
                0 => 1,   // Territory Balloon
                1 => 20,  // Mining Camp -> Gold Mine
                2 => 10,  // Cannon Tower -> Cannon
                3 => 11,  // Shield Tower -> Shield Generator
                4 => 12,  // Repair Tower -> Flak Tower
                5 => 21,  // Missile Tower -> Heavy Mortar
                6 => 22,  // Laser Tower -> Laser Tower
                _ => 0
            };
        }

        public static ushort[] MapPreviewLoadoutToGameplayIds(IReadOnlyList<int> previewCardIndices)
        {
            var mapped = new ushort[DeckRules.DeckSize];
            if (previewCardIndices == null)
            {
                return mapped;
            }

            int count = Math.Min(DeckRules.DeckSize, previewCardIndices.Count);
            for (int i = 0; i < count; i++)
            {
                mapped[i] = previewCardIndices[i] >= 0
                    ? MapPreviewCardIndexToGameplayId(previewCardIndices[i])
                    : (ushort)0;
            }

            return mapped;
        }

        public static ushort[] NormalizeToValidDeck(
            IReadOnlyList<ushort> requestedCardIds,
            ICardCatalogLookup catalog,
            IReadOnlyList<ushort> fallbackSelectableCardIds,
            IUnlockedCardProvider unlockedPool = null)
        {
            var result = new ushort[DeckRules.DeckSize];
            if (catalog == null || catalog.TerritoryBalloonCardId == 0)
            {
                return result;
            }

            ushort balloonId = catalog.TerritoryBalloonCardId;
            result[DeckRules.LockedBalloonSlotIndex] = balloonId;
            var used = new HashSet<ushort> { balloonId };

            int writeIndex = DeckRules.FreeSlotStartIndex;
            if (requestedCardIds != null)
            {
                for (int i = 0; i < requestedCardIds.Count && writeIndex < DeckRules.DeckSize; i++)
                {
                    ushort candidate = requestedCardIds[i];
                    if (IsEligibleFreeCard(candidate, catalog, unlockedPool, used))
                    {
                        result[writeIndex++] = candidate;
                        used.Add(candidate);
                    }
                }
            }

            if (fallbackSelectableCardIds != null)
            {
                for (int i = 0; i < fallbackSelectableCardIds.Count && writeIndex < DeckRules.DeckSize; i++)
                {
                    ushort candidate = fallbackSelectableCardIds[i];
                    if (IsEligibleFreeCard(candidate, catalog, unlockedPool, used))
                    {
                        result[writeIndex++] = candidate;
                        used.Add(candidate);
                    }
                }
            }

            return result;
        }

        private static bool IsEligibleFreeCard(
            ushort cardId,
            ICardCatalogLookup catalog,
            IUnlockedCardProvider unlockedPool,
            HashSet<ushort> used)
        {
            if (cardId == 0 || used.Contains(cardId))
            {
                return false;
            }

            if (!catalog.TryGetCardRules(cardId, out CardRuleMetadata meta))
            {
                return false;
            }

            if (!meta.IsDeckSelectable || meta.IsLockedInSlotZero)
            {
                return false;
            }

            return unlockedPool == null || unlockedPool.IsCardUnlocked(cardId);
        }
    }
}
