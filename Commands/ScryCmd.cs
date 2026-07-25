using BaseLib.Extensions;
using BaseLib.Hooks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Commands;

/// <summary>
/// Represents the completed result of a resolved scry action.
/// </summary>
public readonly record struct ScryResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScryResult"/> struct with the given discarded cards.
    /// </summary>
    /// <param name="discarded">The collection of card models chosen to be sent to the discard pile.</param>
    public ScryResult(IReadOnlyList<CardModel> discarded) => Discarded = discarded;

    /// <summary>
    /// Gets the read-only list of cards that were discarded during the scry action. 
    /// Returns an empty list if no cards were discarded or the scry was aborted.
    /// </summary>
    public IReadOnlyList<CardModel> Discarded => field ?? [];

    /// <summary>
    /// Gets a default instance representing a skipped or zero-card scry result.
    /// </summary>
    public static ScryResult Empty => default;
}

/// <summary>
/// Command utility responsible for executing the scry mechanic process, including 
/// hook modification, player choice grid generation, card movement, and event history logging.
/// </summary>
public static class ScryCmd
{
    /// <summary>
    /// Executes a scry action using the dynamic "Scry" variable value defined on the provided card.
    /// </summary>
    /// <param name="choiceContext">The multiplayer choice context tracking current player choices.</param>
    /// <param name="card">The card model initiating the scry action.</param>
    /// <returns>A <see cref="Task{ScryResult}"/> tracking the execution and resolution details of the scry.</returns>
    public static Task<ScryResult> Execute(PlayerChoiceContext choiceContext, CardModel card)
    {
        return Execute(choiceContext, card.Owner, card.DynamicVars.Scry().IntValue);
    }
    
    /// <summary>
    /// Executes a standard scry action by running dynamic modifiers, prompting the player to select 
    /// cards from the top of their draw pile, routing chosen discards, and firing cleanup hooks.
    /// </summary>
    /// <param name="choiceContext">The multiplayer choice context tracking current player choices.</param>
    /// <param name="player">The player performing the scry.</param>
    /// <param name="amount">The base number of cards to reveal before modifiers run.</param>
    /// <returns>
    /// A <see cref="ScryResult"/> containing a list of cards the player ultimately chose to discard. 
    /// Returns <see cref="ScryResult.Empty"/> if the amount was reduced to zero or if combat is inactive.
    /// </returns>
    public static async Task<ScryResult> Execute(PlayerChoiceContext choiceContext, Player player, int amount)
    {
        var modifiedAmount = BaseLibHooks.ModifyScryAmount(player, amount, out var modifiers);
        await BaseLibHooks.AfterModifyingScryAmount(choiceContext, player, modifiers, amount, modifiedAmount);

        if (modifiedAmount <= 0) return default;
        
        var drawPile = PileType.Draw.GetPile(player);
        var discardPile = PileType.Discard.GetPile(player);
        var combatState = player.Creature.CombatState;
        if (combatState == null) return default;
        var cardsToScry = drawPile.Cards.Take(modifiedAmount).ToList();
        if (cardsToScry.Count == 0) return default;
        var prefs = new CardSelectorPrefs(
            CardSelectorPrefs.DiscardSelectionPrompt,
            0,
            cardsToScry.Count
        );

        var cardsToDiscard = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            cardsToScry,
            player,
            prefs
        )).ToList();
       
        // we don't want sly, so can't use CardCmd.Discard.
        foreach (var card in cardsToDiscard)
        {
           
            await CardPileCmd.Add(card, discardPile);
            CombatManager.Instance.History.CardDiscarded(combatState, card);
            await Hook.AfterCardDiscarded(combatState, choiceContext, card);
        }
        discardPile.InvokeContentsChanged();
        
        await BaseLibHooks.AfterScryed(choiceContext, player, modifiedAmount, cardsToDiscard.Count, 
            cardsToScry, cardsToDiscard);
        return new ScryResult(cardsToDiscard);
    }
}