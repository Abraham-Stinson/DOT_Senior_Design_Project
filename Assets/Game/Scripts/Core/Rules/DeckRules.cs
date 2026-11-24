using System.Collections.Generic;

namespace Game.Core
{
    public readonly struct CardRuleMetadata
    {
        public readonly ushort CardId;
        public readonly bool IsLockedInSlotZero;
        public readonly bool IsDeckSelectable;
        public readonly bool UnlockedByDefault;

        public CardRuleMetadata(
            ushort cardId,
            bool isLockedInSlotZero,
            bool isDeckSelectable,
            bool unlockedByDefault)
        {
            CardId = cardId;
            IsLockedInSlotZero = isLockedInSlotZero;
            IsDeckSelectable = isDeckSelectable;
            UnlockedByDefault = unlockedByDefault;
        }
    }

    public interface ICardCatalogLookup
    {
        ushort TerritoryBalloonCardId { get; }
        bool TryGetCardRules(ushort cardId, out CardRuleMetadata metadata);
    }

    public readonly struct LoadoutValidationResult
    {
        public readonly bool IsValid;
        public readonly LoadoutValidationError Error;
        public readonly int FailingSlotIndex;
        public readonly ushort FailingCardId;

        private LoadoutValidationResult(
            bool isValid,
            LoadoutValidationError error,
            int failingSlotIndex,
            ushort failingCardId)
        {
            IsValid = isValid;
            Error = error;
            FailingSlotIndex = failingSlotIndex;
            FailingCardId = failingCardId;
        }

        public static LoadoutValidationResult Ok()
        {
            return new LoadoutValidationResult(true, LoadoutValidationError.None, -1, 0);
        }

        public static LoadoutValidationResult Fail(
            LoadoutValidationError error,
            int failingSlotIndex = -1,
            ushort failingCardId = 0)
        {
            return new LoadoutValidationResult(false, error, failingSlotIndex, failingCardId);
        }
    }

    /// <summary>
    /// Enforces the 5-slot deck model:
    /// Slot 0 is permanently locked to the Territory Balloon card; Slots 1..4 are freely chosen
    /// from eligible unlocked cards. Gold Mine is never auto-included outside these 5 slots.
    /// </summary>
    public static class DeckRules
    {
        public const int DeckSize = 5;
        public const int LockedBalloonSlotIndex = 0;
        public const int FreeSlotStartIndex = 1;
        public const int FreeSlotCount = DeckSize - FreeSlotStartIndex;
    }

    public static class LoadoutValidator
    {
        public static LoadoutValidationResult Validate(
            IReadOnlyList<ushort> submittedCardIds,
            ICardCatalogLookup catalog,
            IUnlockedCardProvider requiredUnlockedPool = null)
        {
            if (catalog == null
                || catalog.TerritoryBalloonCardId == 0
                || !catalog.TryGetCardRules(catalog.TerritoryBalloonCardId, out CardRuleMetadata balloonMeta)
                || !balloonMeta.IsLockedInSlotZero)
            {
                return LoadoutValidationResult.Fail(LoadoutValidationError.InvalidCatalog);
            }

            if (submittedCardIds == null || submittedCardIds.Count != DeckRules.DeckSize)
            {
                return LoadoutValidationResult.Fail(LoadoutValidationError.InvalidDeckSize);
            }

            ushort slotZeroId = submittedCardIds[DeckRules.LockedBalloonSlotIndex];
            if (slotZeroId != catalog.TerritoryBalloonCardId)
            {
                return LoadoutValidationResult.Fail(
                    LoadoutValidationError.SlotZeroMustBeTerritoryBalloon,
                    DeckRules.LockedBalloonSlotIndex,
                    slotZeroId);
            }

            var seen = new HashSet<ushort> { slotZeroId };

            for (int i = DeckRules.FreeSlotStartIndex; i < DeckRules.DeckSize; i++)
            {
                ushort cardId = submittedCardIds[i];

                if (!seen.Add(cardId))
                {
                    return LoadoutValidationResult.Fail(LoadoutValidationError.DuplicateCard, i, cardId);
                }

                if (cardId == 0 || !catalog.TryGetCardRules(cardId, out CardRuleMetadata meta))
                {
                    return LoadoutValidationResult.Fail(LoadoutValidationError.UnknownCard, i, cardId);
                }

                if (!meta.IsDeckSelectable || meta.IsLockedInSlotZero)
                {
                    return LoadoutValidationResult.Fail(LoadoutValidationError.CardNotSelectable, i, cardId);
                }

                if (requiredUnlockedPool != null && !requiredUnlockedPool.IsCardUnlocked(cardId))
                {
                    return LoadoutValidationResult.Fail(LoadoutValidationError.CardLocked, i, cardId);
                }
            }

            return LoadoutValidationResult.Ok();
        }
    }

    public readonly struct DraftValidationOutcome
    {
        public readonly bool IsValid;
        public readonly DraftRejectReason RejectReason;
        public readonly LoadoutValidationError LoadoutError;
        public readonly int FailingSlotIndex;
        public readonly ushort FailingCardId;

        private DraftValidationOutcome(
            bool isValid,
            DraftRejectReason rejectReason,
            LoadoutValidationError loadoutError,
            int failingSlotIndex,
            ushort failingCardId)
        {
            IsValid = isValid;
            RejectReason = rejectReason;
            LoadoutError = loadoutError;
            FailingSlotIndex = failingSlotIndex;
            FailingCardId = failingCardId;
        }

        public static DraftValidationOutcome Ok()
        {
            return new DraftValidationOutcome(
                true,
                DraftRejectReason.None,
                LoadoutValidationError.None,
                -1,
                0);
        }

        public static DraftValidationOutcome Fail(
            DraftRejectReason rejectReason,
            LoadoutValidationError loadoutError = LoadoutValidationError.None,
            int failingSlotIndex = -1,
            ushort failingCardId = 0)
        {
            return new DraftValidationOutcome(false, rejectReason, loadoutError, failingSlotIndex, failingCardId);
        }
    }

    public static class DraftSubmissionValidator
    {
        public static DraftValidationOutcome Validate(
            MatchPhase currentPhase,
            MatchMode mode,
            bool seatExists,
            bool callerOwnsSeat,
            IReadOnlyList<ushort> submittedCardIds,
            ICardCatalogLookup catalog,
            IUnlockedCardProvider localOwnerPool,
            bool isSenderLocalHost)
        {
            if (currentPhase != MatchPhase.PreMatchDraft)
            {
                return DraftValidationOutcome.Fail(DraftRejectReason.NotInDraftPhase);
            }

            if (!seatExists)
            {
                return DraftValidationOutcome.Fail(DraftRejectReason.InvalidSeat);
            }

            if (!callerOwnsSeat)
            {
                return DraftValidationOutcome.Fail(DraftRejectReason.UnauthorizedSender);
            }

            IUnlockedCardProvider poolToEnforce = (mode == MatchMode.LocalPvP || isSenderLocalHost)
                ? localOwnerPool
                : null;

            LoadoutValidationResult deckResult = LoadoutValidator.Validate(submittedCardIds, catalog, poolToEnforce);
            if (!deckResult.IsValid)
            {
                return DraftValidationOutcome.Fail(
                    DraftRejectReason.LoadoutInvalid,
                    deckResult.Error,
                    deckResult.FailingSlotIndex,
                    deckResult.FailingCardId);
            }

            return DraftValidationOutcome.Ok();
        }
    }
}
