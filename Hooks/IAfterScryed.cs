using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
/// Defines a hook listener that runs automatically after a scry action has fully resolved 
/// (the player has viewed the cards and chosen which ones to move to the discard pile).
/// <para>
/// Hook interfaces should be implemented on <see cref="AbstractModel"/> subclasses in the active 
/// combat state to be picked up by the central dispatch pipelines.
/// </para>
/// </summary>
public interface IAfterScryed
{
    /// <summary>
    /// Invoked after a scry action has fully completed, cards have been chosen, and discards have moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Execution Conditions:</b> This hook only fires when a scry actually happened: the final modified scry 
    /// amount was greater than zero <b>and</b> the draw pile contained at least one card. Scries that resolve 
    /// to nothing (amount reduced to 0 by modifiers, or an empty draw pile) never reach this hook. 
    /// This is a no-op outside of active combat states.
    /// </para>
    /// <para>
    /// <b>State Timing:</b> When this method runs, any cards chosen for discard have already been added to the 
    /// discard pile, and their individual per-card discard hooks have already finished executing.
    /// </para>
    /// </remarks>
    /// <param name="ctx">Player choice context of the ongoing player decision; each listener is pushed onto it during its invocation.</param>
    /// <param name="player">The player who scryed.</param>
    /// <param name="scryAmount">
    /// The scry amount after <see cref="BaseLibHooks.ModifyScryAmount"/> modifiers, as requested.
    /// <b>Not</b> clamped to the draw pile size. May exceed the number of cards actually viewed 
    /// (e.g. Scry 5 with 2 cards in the draw pile passes 5).
    /// </param>
    /// <param name="discardAmount">
    /// Number of cards the player chose to discard; always equals the count of <paramref name="discarded"/>. 
    /// May be 0 when the player kept everything.
    /// </param>
    /// <param name="seen">
    /// The cards shown by this scry.
    /// </param>
    /// <param name="discarded">
    /// The cards discarded by this scry, already added to the discard pile (their per-card discard hooks 
    /// already dispatched) by the time this hook runs. Empty when the player kept everything.
    /// </param>
    /// <returns>A <see cref="Task"/> tracking the asynchronous execution of this follow-up hook logic.</returns>
    Task AfterScryed(PlayerChoiceContext ctx, Player player, int scryAmount, int discardAmount, List<CardModel> seen, List<CardModel> discarded);
}