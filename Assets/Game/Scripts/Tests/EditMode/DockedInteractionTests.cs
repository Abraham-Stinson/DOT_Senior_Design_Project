using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class DockedInteractionTests
    {
        [Test]
        public void DockedMenuNavigator_CyclesDeckSlotsWithWrapAround()
        {
            Assert.That(DockedMenuNavigator.CycleDeckSlot(0, -1, 5), Is.EqualTo(4));
            Assert.That(DockedMenuNavigator.CycleDeckSlot(4, 1, 5), Is.EqualTo(0));
            Assert.That(DockedMenuNavigator.CycleDeckSlot(1, 1, 5), Is.EqualTo(2));
            Assert.That(DockedMenuNavigator.CycleDeckSlot(2, -1, 5), Is.EqualTo(1));
            Assert.That(DockedMenuNavigator.CycleDeckSlot(3, 0, 5), Is.EqualTo(3));
        }

        [Test]
        public void DockedMenuNavigator_TogglesStructureActionBetweenUseAndSell()
        {
            Assert.That(
                DockedMenuNavigator.CycleStructureAction(DockedStructureAction.Use, 1),
                Is.EqualTo(DockedStructureAction.Sell));
            Assert.That(
                DockedMenuNavigator.CycleStructureAction(DockedStructureAction.Sell, 1),
                Is.EqualTo(DockedStructureAction.Use));
            Assert.That(
                DockedMenuNavigator.CycleStructureAction(DockedStructureAction.Use, -1),
                Is.EqualTo(DockedStructureAction.Sell));
            Assert.That(
                DockedMenuNavigator.CycleStructureAction(DockedStructureAction.Sell, 0),
                Is.EqualTo(DockedStructureAction.Sell));
        }

        [Test]
        public void StructureSellRules_AllowsAnyNonBaseAlliedStructureAndProtectsBase()
        {
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Offense, true, TeamId.TeamA, TeamId.TeamA), Is.True);
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Economy, true, TeamId.TeamA, TeamId.TeamA), Is.True);
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Territory, true, TeamId.TeamA, TeamId.TeamA), Is.True);
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Defense, true, TeamId.TeamA, TeamId.TeamA), Is.True);

            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Core, true, TeamId.TeamA, TeamId.TeamA), Is.False);
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Offense, false, TeamId.TeamA, TeamId.TeamA), Is.False);
            Assert.That(StructureSellRules.CanSellStructure(StructureCategory.Offense, true, TeamId.TeamB, TeamId.TeamA), Is.False);

            Assert.That(StructureSellRules.ComputeSellRefund(100), Is.EqualTo(50));
            Assert.That(StructureSellRules.ComputeSellRefund(75), Is.EqualTo(38));
        }

        [Test]
        public void StructurePlacementValidator_RejectsBeginCarryWhenNotDockedAtBase()
        {
            var input = new BuildValidationInput(
                phase: MatchPhase.Playing,
                seatExists: true,
                callerOwnsSeat: true,
                cardExists: true,
                cardInActiveLoadout: true,
                isOnCooldown: false,
                currentGold: 300,
                buildCostGold: 100,
                withinBuildRange: true,
                insideTeamTerritory: true,
                validTerrain: true,
                overlapsExistingStructure: false,
                requiresGoldDeposit: false,
                hasValidGoldDeposit: true,
                depositOccupied: false,
                withinGroundProximity: true,
                mustDockAtBaseSatisfied: false);

            Assert.That(StructurePlacementValidator.Validate(input), Is.EqualTo(BuildRejectReason.MustDockAtBase));
        }

        [Test]
        public void WeaponFireInputGate_BlocksCarriedOverConfirmPressAndFiresOnlyOnFreshPressAfterRelease()
        {
            var gate = new WeaponFireInputGate();
            gate.LockUntilRelease();

            // Same keypress / held confirm must never fire.
            Assert.That(gate.EvaluateFireInput(firePressed: true, fireHeld: true, confirmPressed: true), Is.False);
            Assert.That(gate.EvaluateFireInput(firePressed: false, fireHeld: true, confirmPressed: false), Is.False);
            Assert.That(gate.IsWaitingForRelease, Is.True);

            // Releasing button unlocks the gate without firing.
            Assert.That(gate.EvaluateFireInput(firePressed: false, fireHeld: false, confirmPressed: false), Is.False);
            Assert.That(gate.IsWaitingForRelease, Is.False);

            // Held without a rising edge does not fire.
            Assert.That(gate.EvaluateFireInput(firePressed: false, fireHeld: true, confirmPressed: false), Is.False);

            // Fresh rising-edge press fires cleanly.
            Assert.That(gate.EvaluateFireInput(firePressed: true, fireHeld: true, confirmPressed: true), Is.True);
        }
    }
}
