namespace Game.Core
{
    /// <summary>
    /// Pure helpers for Left/Right menu cycling while docked at Base or non-Base structures.
    /// </summary>
    public static class DockedMenuNavigator
    {
        public static int CycleDeckSlot(int currentSlot, int delta, int slotCount = DeckRules.DeckSize)
        {
            if (slotCount <= 0)
            {
                return 0;
            }

            int normalized = ((currentSlot % slotCount) + slotCount) % slotCount;
            if (delta == 0)
            {
                return normalized;
            }

            int step = delta > 0 ? 1 : -1;
            return ((normalized + step) % slotCount + slotCount) % slotCount;
        }

        public static DockedStructureAction CycleStructureAction(
            DockedStructureAction current,
            int delta,
            bool supportsUseAction = true)
        {
            if (!supportsUseAction)
            {
                return DockedStructureAction.Sell;
            }

            if (delta == 0)
            {
                return current;
            }

            return current == DockedStructureAction.Use
                ? DockedStructureAction.Sell
                : DockedStructureAction.Use;
        }
    }

    public static class StructureInteractionRules
    {
        /// <summary>
        /// Gold Mine (Economy) only passively generates gold and has no active 'Use' interaction;
        /// Base (Core) opens the deck shop; all other non-Base structures support both Use and Sell.
        /// </summary>
        public static bool SupportsUseAction(StructureCategory category)
        {
            return category != StructureCategory.Core && category != StructureCategory.Economy;
        }

        public static DockedStructureAction GetDefaultDockedAction(StructureCategory category)
        {
            return SupportsUseAction(category)
                ? DockedStructureAction.Use
                : DockedStructureAction.Sell;
        }
    }

    /// <summary>
    /// Server-side rules governing structure selling and gold refunds.
    /// Any non-Base (non-Core) allied alive structure can be sold; Base (Core) can never be sold.
    /// </summary>
    public static class StructureSellRules
    {
        public const float DefaultSellRefundRatio = 0.5f;

        public static bool CanSellStructure(
            StructureCategory category,
            bool isAlive,
            TeamId structureTeam,
            TeamId actorTeam)
        {
            if (category == StructureCategory.Core || !isAlive)
            {
                return false;
            }

            return structureTeam != TeamId.Neutral && structureTeam == actorTeam;
        }

        public static int ComputeSellRefund(int buildCostGold, float refundRatio = DefaultSellRefundRatio)
        {
            return EconomyLedger.ComputeRefund(buildCostGold, refundRatio);
        }
    }
}
