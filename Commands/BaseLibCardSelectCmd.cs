using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;


namespace BaseLib.Commands;

/// <summary>
/// Card selection helpers that aren't available in <see cref="CardSelectCmd"/>.
/// </summary>
public static class BaseLibCardSelectCmd
{
    /// <summary>
    /// Like <see cref="CardSelectCmd.FromCombatPile(PlayerChoiceContext, CardPile, Player, CardSelectorPrefs, Func{CardModel, bool}?)"/>,
    /// but the draw pile is displayed in the order it has instead of being sorted by rarity.
    /// </summary>
    /// <param name="context">The choice context of the current action.</param>
    /// <param name="pile">A combat pile of the player.</param>
    /// <param name="player">The player making the selection.</param>
    /// <param name="prefs">Selection preferences. <see cref="CardSelectorPrefs.Comparison"/> should be left unset to keep pile order.</param>
    /// <param name="filter">Optional filter applied to the pile's cards.</param>
    /// <returns>The selected cards.</returns>
    public static async Task<IEnumerable<CardModel>> FromCombatPileInOrder(PlayerChoiceContext context, CardPile pile,
        Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter = null)
    {
        if (CombatManager.Instance.IsEnding) return [];
        if (!pile.IsCombatPile) throw new InvalidOperationException("Cannot perform on a non combat pile");

        if (CombatManager.Instance.IsPlayerReadyToEndTurn(player)
            && player.Creature.CombatState is { CurrentSide: CombatSide.Player })
        {
            CombatManager.Instance.UndoReadyToEndTurn(player);
        }

        uint? choiceId = null;
        if (CardSelectCmd.Selector == null)
        {
            choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
            await context.SignalPlayerChoiceBegunCompatibility(player, PlayerChoiceOptions.None);
        }

        var cards = (filter == null ? pile.Cards : pile.Cards.Where(filter)).ToList();
        IEnumerable<CardModel> result;
        if (cards.Count == 0)
        {
            result = [];
        }
        else if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
        {
            result = cards;
        }
        else if (CardSelectCmd.Selector != null)
        {
            result = (await CardSelectCmd.Selector.GetSelectedCards(cards, prefs.MinSelect, prefs.MaxSelect)).ToList();
        }
        else if (TestMode.IsOn)
        {
            result = cards.Take(prefs.MinSelect).ToList();
        }
        else if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay)
        {
            NPlayerHand.Instance?.CancelAllCardPlay();
            var screen = NCombatPileCardSelectScreen.Create(pile, prefs, filter);
            NOverlayStack.Instance?.Push(screen);
            Action? stopKeepingOrder = null;
            try
            {
                if (pile.Type == PileType.Draw) stopKeepingOrder = KeepPileOrder(screen, pile, filter);
                result = (await screen.CardsSelected()).ToList();
            }
            finally
            {
                stopKeepingOrder?.Invoke();
            }
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(player, choiceId!.Value,
                PlayerChoiceResult.FromMutableCombatCards(result));
        }
        else
        {
            result = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId!.Value))
                .AsCombatCards();
        }

        if (choiceId.HasValue) await context.SignalPlayerChoiceEnded();
        return result;
    }

    /// <summary>
    /// The combat pile screen always sorts the draw pile by rarity. Re-applies the pile order to its grid
    /// now and whenever the pile changes. Returns an action which stops this.
    /// </summary>
    private static Action KeepPileOrder(NCombatPileCardSelectScreen screen, CardPile pile, Func<CardModel, bool>? filter)
    {
        var grid = screen._grid;
        Reorder();
        pile.ContentsChanged += Reorder;
        return () => pile.ContentsChanged -= Reorder;

        void Reorder()
        {
            if (!GodotObject.IsInstanceValid(grid)) return;
            var cards = (filter == null ? pile.Cards : pile.Cards.Where(filter)).ToList();
            if (cards.Count == 0) return;

            grid.SetCards(cards, pile.Type, [SortingOrders.Ascending]);
        }
    }
}
