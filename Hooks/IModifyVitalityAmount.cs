using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
///     Hook for models (relics, powers, stances, ...) that adjust the amount of vitality given.
///     Listeners are invoked in hook-listener order via
///     <see cref="BaseLibHooks.ModifyVitalityAmount" /> before it resolves.
/// </summary>
public interface IModifyVitalityAmount
{
    /// <summary>
    ///     Returns the adjusted vitality amount. Called once per pending vitality, receiving the value as
    ///     modified by any earlier listeners. Always runs before <see cref="ModifyVitalityMultiplicative" /> as to not cause ordering issues.
    ///     <para>
    ///         Return <paramref name="amount" /> unchanged to opt out — doing so also excludes
    ///         this listener from the <see cref="AfterModifyingVitalityAmount" /> follow-up. Changing
    ///         the value (per-call <see cref="int" /> equality) marks this listener as a modifier
    ///         even if a later listener cancels the change.
    ///     </para>
    ///     <para>
    ///         Results are not clamped. This method must be pure with respect to game state -
    ///         put side effects (VFX, charge consumption) in <see cref="AfterModifyingVitalityAmount" />
    ///         instead, which only runs when a change was actually made.
    ///     </para>
    /// </summary>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">
    /// The vitality amount so far, including earlier listeners' changes.
    /// This value is clamped to a minimum of zero.
    /// </param>
    /// <param name="cardSource">Passes the cardSource if there is one.</param>
    /// <param name="cardPlay">Similarly passes the cardPlay if there is one.</param>
    /// <returns>The new vitality amount; may be lower, higher, or unchanged.</returns>
    decimal ModifyVitalityAdditive(Creature creature, decimal amount, CardModel? cardSource, CardPlay? cardPlay) => 0m;
    
    /// <summary>
    ///     Follow-up invoked after all listeners have run, but only on listeners whose
    ///     <see cref="ModifyVitalityAdditive" /> or <see cref="ModifyVitalityMultiplicative" /> changed the value they received. Use this for the
    ///     side effects of having modified the vitality: visuals, sounds, consuming charges,
    ///     decrementing counters.
    ///     <para>
    ///         The amounts describe the <b>whole</b> modification pass, not this listener's step:
    ///         every invoked listener receives the same pair, and
    ///         <paramref name="amount" /> includes other listeners' changes. A listener
    ///         wanting its own delta must capture the values it saw in
    ///         <see cref="ModifyVitalityAdditive" /> or <see cref="ModifyVitalityMultiplicative" /> themselves.
    ///     </para>
    /// </summary>
    /// <param name="amount">The original vitality amount before <see cref="ModifyVitalityAdditive" /> or <see cref="ModifyVitalityMultiplicative" /> modifiers.</param>
    /// <param name="cardPlay">Similarly passes the cardSource if there is one.</param>
    Task AfterModifyingVitalityAmount(decimal amount, CardPlay? cardPlay) => Task.CompletedTask;
    
    /// <summary>
    ///     Returns a multiplier to alter the vitality amount by. Called once per pending vitality addition, receiving the value as
    ///     modified by any earlier listeners. Always runs after <see cref="ModifyVitalityAdditive" /> as to not cause ordering issues.
    ///     <para>
    ///         Return <paramref name="amount" /> unchanged to opt out — doing so also excludes
    ///         this listener from the <see cref="AfterModifyingVitalityAmount" /> follow-up. Changing
    ///         the value (per-call <see cref="int" /> equality) marks this listener as a modifier
    ///         even if a later listener cancels the change.
    ///     </para>
    ///     <para>
    ///         Results are not clamped. This method must be pure with respect to game state -
    ///         put side effects (VFX, charge consumption) in <see cref="AfterModifyingVitalityAmount" />
    ///         instead, which only runs when a change was actually made.
    ///     </para>
    /// </summary>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">The vitality amount so far, including earlier listeners' changes.</param>
    /// <param name="cardSource">Passes the cardSource if there is one.</param>
    /// <param name="cardPlay">Similarly passes the cardPlay if there is one.</param>
    /// <returns>The <b>multiplier</b> to alter the current vitality amount by.</returns>
    decimal ModifyVitalityMultiplicative(Creature creature, decimal amount, CardModel? cardSource, CardPlay? cardPlay) => 1m;
}