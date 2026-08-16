using BaseLib.Hooks;
using BaseLib.Patches.Features;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BaseLib.Commands;

/// <summary>
/// Command utility responsible for executing the vitality mechanic process, including 
/// hook modification, finding current values, and combatHistory entries.
/// </summary>
public class VitalityCmd
{
    /// <summary>Make this creature gain the specified amount of vitality.</summary>
    /// <param name="creature">Creature that should gain vitality.</param>
    /// <param name="amount">Amount of vitality they should gain.</param>
    /// <param name="cardPlay">
    /// The CardPlay that caused the vitality gain.
    /// Null if it was not directly caused by a card play.
    /// </param>
    /// <param name="fast">If true, the wait that is performed after vitality gain is very small. Should be used in scenarios
    /// where vitality gain happens quickly in sequence (i.e. like Afterimage for block).</param>
    /// <returns>The amount of vitality that the creature gained after all modifications were applied.</returns>
    public static async Task<decimal> GainVitality(
        Creature creature,
        decimal amount,
        CardPlay? cardPlay,
        bool fast = false)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return 0M;
        var combatState = creature.CombatState;
        await BaseLibHooks.BeforeVitalityGained(combatState, creature, amount, cardPlay?.Card);
        var modifiedAmount = BaseLibHooks.ModifyVitalityAmount(combatState, creature, amount, cardPlay?.Card, cardPlay, out var modifiers);
        modifiedAmount = Math.Max(modifiedAmount, 0M);
        await BaseLibHooks.AfterModifyingVitalityAmount(combatState, modifiedAmount, cardPlay, modifiers);
        if (modifiedAmount > 0M)
        {
            SfxCmd.Play("event:/sfx/heal");
            VfxCmd.PlayOnCreatureCenter(creature, "vfx/vfx_cross_heal");
            VitalityPatch.VitalityField.SetVitality(creature, (int) amount + VitalityPatch.VitalityField.GetVitality(creature));
            CombatManager.Instance.History.Add(combatState, new VitalityGainedEntry((int)modifiedAmount, cardPlay, creature, combatState.RoundNumber, combatState.CurrentSide, CombatManager.Instance.History, combatState.Players));
            if (fast)
                await Cmd.CustomScaledWait(0.0f, 0.03f);
            else
                await Cmd.CustomScaledWait(0.1f, 0.25f);
        }
        await BaseLibHooks.AfterVitalityGained(combatState, creature, modifiedAmount, cardPlay?.Card);
        return modifiedAmount;
    }
    
    /// <summary>Returns the amount vitality a specific creature has.</summary>
    /// <param name="creature">Creature you're trying to get the vitality amount of.</param>
    /// <returns>The amount of vitality that the creature has currently.</returns>
    public static decimal Get(Creature creature)
    {
        return VitalityPatch.VitalityField.GetVitality(creature);
    }
    
    /// <summary>Removes all vitality a specific creature has.</summary>
    /// <param name="creature">Creature you're trying to remove the vitality from.</param>
    public static void RemoveAll(Creature creature)
    {
        VitalityPatch.VitalityField.SetVitality(creature, 0);
    }
    
    private class VitalityGainedEntry : CombatHistoryEntry
    {
        public int Amount { get; }

        public Creature Receiver => Actor;

        public CardPlay? CardPlay { get; }

        public override string Description => $"{GetId(Receiver)} gained {Amount} vitality";

        public VitalityGainedEntry(
            int amount,
            CardPlay? cardPlay,
            Creature receiver,
            int roundNumber,
            CombatSide currentSide,
            CombatHistory history,
            IEnumerable<Player> players)
            : base(receiver, roundNumber, currentSide, history, players)
        {
            Amount = amount;
            CardPlay = cardPlay;
        }

        private static string GetId(Creature creature)
        {
            return !creature.IsPlayer ? creature.Monster.Id.Entry : creature.Player.Character.Id.Entry;
        }
    }
}