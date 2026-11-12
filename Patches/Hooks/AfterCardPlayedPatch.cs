using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Cards.Variables;
using BaseLib.Patches.Features;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;

namespace BaseLib.Patches.Hooks;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed), MethodType.Async)]
class AfterCardPlayedPatch
{
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> AfterPlay(ILGenerator generator, IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        return AsyncMethodCall.Create(generator, instructions, original, 
            AccessTools.Method(typeof(AfterCardPlayedPatch), nameof(BeforeAfterPlayHooks)), beforeState: original);
    }

    private static async Task BeforeAfterPlayHooks(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (PostModInitPatch.CanModifyGameplay)
        {
            // OnPlay is awaited, and a modifier is free to remove (or add) itself or another modifier while it
            // runs (CardModifier.RemoveModifier/AddModifier). Modifiers() is only a read-only wrapper over the
            // card's live list (CardModifier.cs:201), so iterating it directly threw InvalidOperationException
            // as soon as one OnPlay touched that list. Snapshot first: same order, same serial awaits, and
            // changes made during a play take effect from the next play.
            foreach (var modifier in CardModifier.Modifiers(cardPlay.Card).ToList())
            {
                await modifier.OnPlay(choiceContext, cardPlay);
            }
            
            var refundAmount = cardPlay.Card.DynamicVars.TryGetValue(RefundVar.Key, out var val) ? val.IntValue : 0;
            if (refundAmount > 0 && cardPlay.Resources.EnergySpent > 0)
            {
                await PlayerCmd.GainEnergy(Math.Min(refundAmount, cardPlay.Resources.EnergySpent), cardPlay.Card.Owner);
            }

            if (PurgePatch.ShouldPurge(cardPlay.Card))
            {
                var deckCard = cardPlay.Card.DeckVersion;
                if (deckCard?.Pile?.Type == PileType.Deck) // Guard against Replay (first play sets Pile to null)
                {
                    await CardPileCmd.RemoveFromDeck(deckCard, false);
                }
            }
        }
    }
}