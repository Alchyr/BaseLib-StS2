using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;


namespace BaseLib.Hooks;

/// <summary>
///     Central dispatchers for various hooks. Hook interfaces should be implemented on
///     <see cref="AbstractModel" /> subclasses in the active combat state to be picked up.
/// </summary>
public static class BaseLibHooks
{
    /// <summary>
    ///     Dispatches <see cref="IAfterScryed.AfterScryed" /> to all subscribed models after a
    ///     scry has fully resolved (viewed cards chosen, discards moved to the discard pile).
    ///     <para>
    ///         Only fires when a scry actually happened: the modified scry amount was greater
    ///         than zero <b>and</b> the draw pile contained at least one card. Scries that
    ///         resolve to nothing (amount reduced to 0 by modifiers, or an empty draw pile)
    ///         never reach this hook. No-op outside combat.
    ///     </para>
    /// </summary>
    /// <param name="ctx">Player choice context of the ongoing player decision; each listener is pushed onto it during its invocation.</param>
    /// <param name="player">The player who scryed.</param>
    /// <param name="scryAmount">
    ///     The scry amount after <see cref="ModifyScryAmount" /> modifiers, as requested —
    ///     <b>not</b> clamped to the draw pile size. May exceed the number of cards actually
    ///     viewed (e.g. Scry 5 with 2 cards in the draw pile passes 5).
    /// </param>
    /// <param name="discardedAmount">
    ///     Number of cards the player chose to discard; always equals the count of
    ///     <paramref name="discarded" />. May be 0 when the player kept everything.
    /// </param>
    /// <param name="seen">
    ///     The cards shown by this scry.
    /// </param>
    /// <param name="discarded">
    ///     The cards discarded by this scry, already added to the discard pile (their per-card
    ///     discard hooks already dispatched) by the time this hook runs. Empty when the player
    ///     kept everything.
    /// </param>
    public static Task AfterScryed(PlayerChoiceContext ctx, Player player, int scryAmount, int discardedAmount, List<CardModel> seen, List<CardModel> discarded)
    {
        return HookUtils.Dispatch<IAfterScryed>(player.Creature.CombatState, ctx, m => m.AfterScryed(ctx, player, scryAmount, discardedAmount, seen, discarded));
    }

    /// <summary>
    ///     Passes a scry amount through all <see cref="IModifyScryAmount" /> hook listeners in
    ///     listener order, letting each adjust the value, and reports which listeners changed it.
    ///     <para>
    ///         A listener counts as a modifier only if it changed the value it received
    ///         (per-step <see cref="int" /> equality): returning the input unchanged does not
    ///         register it in <paramref name="modifiers" />. The comparison is per listener, not
    ///         against the original amount.
    ///         Listeners whose changes cancel each other out
    ///         (+2 then −2) are <b>all</b> recorded, so <paramref name="modifiers" /> can be
    ///         non-empty even when the returned amount equals <paramref name="amount" />.
    ///     </para>
    ///     <para>
    ///         Outside of combat (no combat state), returns <paramref name="amount" /> unchanged
    ///         with an empty <paramref name="modifiers" /> set.
    ///     </para>
    /// </summary>
    /// <param name="player">The player about to scry.</param>
    /// <param name="amount">The base scry amount before modification.</param>
    /// <param name="modifiers">
    ///     The listeners that changed the value, for follow-up dispatch via
    ///     <see cref="AfterModifyingScryAmount" />.
    /// </param>
    /// <returns>
    ///     The final scry amount. Not clamped: may be zero or negative if listeners reduce it;
    ///     callers are expected to treat non-positive results as "no scry".
    /// </returns>
    public static int ModifyScryAmount(Player player, int amount, out IEnumerable<IModifyScryAmount> modifiers)
    {
        return HookUtils.Modify(player.Creature.CombatState, amount, (m, a) => m.ModifyScryAmount(player, a), out modifiers);
    }

    /// <summary>
    ///     Dispatches <see cref="IModifyScryAmount.AfterModifyingScryAmount" /> to each listener
    ///     that changed the scry amount in the preceding <see cref="ModifyScryAmount" /> call -
    ///     e.g. to play VFX or consume a charge.
    ///     <para>
    ///         Listeners that saw the value but left it unchanged are not called. Iteration
    ///         follows current hook-listener order, not the order of
    ///         <paramref name="modifiers" />. The amounts describe the whole modification pass:
    ///         every invoked listener receives the same pair, and
    ///         <paramref name="modifiedAmount" /> includes other listeners' changes.
    ///     </para>
    /// </summary>
    /// <param name="ctx">Player choice context of the ongoing player decision.</param>
    /// <param name="player">The player whose scry amount was modified.</param>
    /// <param name="modifiers">The modifier set returned by <see cref="ModifyScryAmount" />.</param>
    /// <param name="originalAmount">The base scry amount before any listener ran.</param>
    /// <param name="modifiedAmount">
    ///     The final amount after all listeners; not clamped, so it may be zero or negative
    ///     (in which case the scry itself is skipped).
    /// </param>
    public static Task AfterModifyingScryAmount(PlayerChoiceContext ctx, Player player, IEnumerable<IModifyScryAmount> modifiers, int originalAmount, int modifiedAmount)
    {
        return HookUtils.AfterModifying(player.Creature.CombatState!, modifiers, a => a.AfterModifyingScryAmount(ctx, player, originalAmount, modifiedAmount));
    }

    public static async Task AfterSpendCustomResource<T>(ICombatState combatState, T resource, AbstractModel? spender, int amount) where T : CustomResource
    {
        await HookUtils.Dispatch<IAfterSpendResource<T>>(combatState, m => m.AfterSpendResource(combatState, resource, spender, amount));
    }
    
    /// <summary>
    /// See <see cref="IModifyResourceCostInCombat{T}.ModifyResourceCostInCombat" />.
    /// </summary>
    public static decimal ModifyResourceCostInCombat<T>(
        ICombatState combatState,
        T resource,
        CardModel card,
        decimal originalCost) where T : CustomResource
    {
        if (originalCost < 0M)
            return originalCost;
        var modifiedCost = HookUtils.Modify<IModifyResourceCostInCombat<T>, decimal>(
            combatState,
            originalCost,
            (modifier, amt) => modifier.ModifyResourceCostInCombat(card, resource, amt),
            out _);
        return modifiedCost;
    }
    
    /// <summary>
    ///     Passes a scry amount through all <see cref="IModifyVitalityAmount" /> hook listeners in
    ///     listener order, letting each adjust the value, and reports which listeners changed it.
    ///     <para>
    ///         A listener counts as a modifier only if it changed the value it received
    ///         (per-step <see cref="int" /> equality): returning the input unchanged does not
    ///         register it in <paramref name="modifiers" />. The comparison is per listener, not
    ///         against the original amount.
    ///         Listeners whose changes cancel each other out
    ///         (+2 then −2) are <b>all</b> recorded, so <paramref name="modifiers" /> can be
    ///         non-empty even when the returned amount equals <paramref name="amount" />.
    ///     </para>
    ///     <para>
    ///         Outside of combat (no combat state), returns <paramref name="amount" /> unchanged
    ///         with an empty <paramref name="modifiers" /> set.
    ///     </para>
    /// </summary>
    /// <param name="combatState">ICombatState of the creature gaining Vitality.</param>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">The original vitality amount before <see cref="ModifyVitalityAmount" /> modifiers.</param>
    /// <param name="cardSource">Passes the cardSource if there is one.</param>
    /// <param name="cardPlay">Similarly passes the cardPlay if there is one.</param>
    /// <param name="modifiers">
    ///     The listeners that changed the value, for follow-up dispatch via
    ///     <see cref="AfterModifyingVitalityAmount" />.
    /// </param>
    /// <returns>
    ///     The final Vitality amount. Not clamped: may be zero or negative if listeners reduce it;
    ///     callers are expected to treat non-positive results as "no vitality" or clamp it themselves.
    /// </returns>
    public static decimal ModifyVitalityAmount(
        ICombatState combatState,
        Creature creature,
        decimal amount,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out IEnumerable<IModifyVitalityAmount> modifiers)
    {
        var additive = HookUtils.Modify(combatState, amount, (m, a) => m.ModifyVitalityAdditive(creature, a, cardSource, cardPlay), out modifiers);
        return HookUtils.ModifyMultiplicative(combatState, additive, modifiers, (m, a) => m.ModifyVitalityMultiplicative(creature, a, cardSource, cardPlay), out modifiers);
    }
    
    /// <summary>
    ///     Dispatches <see cref="IModifyVitalityAmount.AfterModifyingVitalityAmount" /> to each listener
    ///     that changed the vitality amount in the preceding <see cref="ModifyVitalityAmount" /> call -
    ///     e.g. to play VFX or consume a charge.
    ///     <para>
    ///         Listeners that saw the value but left it unchanged are not called. Iteration
    ///         follows current hook-listener order, not the order of
    ///         <paramref name="modifiers" />.
    ///     </para>
    /// </summary>
    /// <param name="combatState">ICombatState of the creature gaining Vitality.</param>
    /// <param name="amount">The original vitality amount before <see cref="ModifyVitalityAmount" /> modifiers.</param>
    /// <param name="cardPlay">Similarly passes the cardSource if there is one.</param>
    /// <param name="modifiers">
    ///     The listeners that changed the value, for follow-up dispatch via
    ///     <see cref="AfterModifyingVitalityAmount" />.
    /// </param>
    public static Task AfterModifyingVitalityAmount(
        ICombatState combatState,
        decimal amount,
        CardPlay? cardPlay,
        IEnumerable<IModifyVitalityAmount> modifiers)
    {
        return HookUtils.AfterModifying(combatState, modifiers, a => a.AfterModifyingVitalityAmount(amount, cardPlay));
    }

    
    /// <summary>
    ///     Dispatches <see cref="IVitalityHooks.BeforeVitalityGained" /> to  
    ///     all subscribed models before the Vitality is given.
    /// </summary>
    /// <param name="combatState">ICombatState of the creature gaining Vitality.</param>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">The vitality amount BEFORE <see cref="ModifyVitalityAmount" /> modifiers.</param>
    /// <param name="cardSource">Gives source card if there is one. </param>
    public static Task BeforeVitalityGained(
        ICombatState combatState,
        Creature creature,
        decimal amount,
        CardModel? cardSource)
    {
        return HookUtils.Dispatch<IVitalityHooks>(combatState, a => a.BeforeVitalityGained(creature, amount, cardSource));
    }
    
    /// <summary>
    ///     Dispatches <see cref="IVitalityHooks.AfterVitalityGained" /> to  
    ///     all subscribed models after the Vitality is given.
    /// </summary>
    /// <param name="combatState">ICombatState of the creature gaining Vitality.</param>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">The vitality amount after <see cref="ModifyVitalityAmount" /> modifiers.</param>
    /// <param name="cardSource">Gives source card if there is one. </param>
    public static Task AfterVitalityGained(
        ICombatState combatState,
        Creature creature,
        decimal amount,
        CardModel? cardSource)
    {
        return HookUtils.Dispatch<IVitalityHooks>(combatState, a => a.AfterVitalityGained(creature, amount, cardSource));
    }
}