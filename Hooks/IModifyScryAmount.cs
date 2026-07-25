using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace BaseLib.Hooks;

/// <summary>
///     Hook for models (relics, powers, stances, ...) that adjust the amount of an upcoming scry.
///     Listeners are invoked in hook-listener order via
///     <see cref="BaseLibHooks.ModifyScryAmount" /> before the scry resolves.
/// </summary>
public interface IModifyScryAmount
{
    /// <summary>
    ///     Returns the adjusted scry amount. Called once per pending scry, receiving the value as
    ///     modified by any earlier listeners.
    ///     <para>
    ///         Return <paramref name="amount" /> unchanged to opt out — doing so also excludes
    ///         this listener from the <see cref="AfterModifyingScryAmount" /> follow-up. Changing
    ///         the value (per-call <see cref="int" /> equality) marks this listener as a modifier
    ///         even if a later listener cancels the change.
    ///     </para>
    ///     <para>
    ///         Results are not clamped: reducing the amount to zero or below cancels the scry
    ///         entirely (no cards viewed, no <see cref="IAfterScryed" /> dispatch). This method
    ///         must be pure with respect to game state - put side effects (VFX, charge
    ///         consumption) in <see cref="AfterModifyingScryAmount" /> instead, which only runs
    ///         when a change was actually made.
    ///     </para>
    /// </summary>
    /// <param name="player">The player about to scry.</param>
    /// <param name="amount">The scry amount so far, including earlier listeners' changes.</param>
    /// <returns>The new scry amount; may be lower, higher, or unchanged.</returns>
    int ModifyScryAmount(Player player, int amount);

    /// <summary>
    ///     Follow-up invoked after all listeners have run, but only on listeners whose
    ///     <see cref="ModifyScryAmount" /> changed the value they received. Use this for the
    ///     side effects of having modified the scry: visuals, sounds, consuming charges,
    ///     decrementing counters.
    ///     <para>
    ///         Runs before any cards are viewed, and runs even when the final amount ended up
    ///         equal to the original (canceling modifications still count) or non-positive (the
    ///         scry itself will then be skipped) - do not assume a scry follows.
    ///     </para>
    ///     <para>
    ///         The amounts describe the <b>whole</b> modification pass, not this listener's step:
    ///         every invoked listener receives the same pair, and
    ///         <paramref name="modifiedAmount" /> includes other listeners' changes. A listener
    ///         wanting its own delta must capture the values it saw in
    ///         <see cref="ModifyScryAmount" /> itself.
    ///     </para>
    /// </summary>
    /// <param name="ctx">Player choice context of the ongoing player decision.</param>
    /// <param name="player">The player whose scry amount was modified.</param>
    /// <param name="originalAmount">The base scry amount before any listener ran.</param>
    /// <param name="modifiedAmount">
    ///     The final amount after all listeners; not clamped, so it may be zero or negative
    ///     (in which case the scry is skipped). Equal amounts do not imply this listener did
    ///     nothing - see the canceling-modifications note above.
    /// </param>
    Task AfterModifyingScryAmount(PlayerChoiceContext ctx, Player player, int originalAmount, int modifiedAmount);
}