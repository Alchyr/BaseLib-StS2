using BaseLib.Cards.Variables;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;

namespace BaseLib.Patches.Features;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
class AfterCardPlayedPatch
{
    [HarmonyPostfix]
    static void AfterPlay(CombatState combatState,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay, ref Task __result)
    {
        __result = ExecuteAfterPlay(cardPlay, __result);
    }

    private static async Task ExecuteAfterPlay(CardPlay cardPlay, Task followTask)
    {
        await followTask;
        
        var refundAmount = cardPlay.Card.DynamicVars.TryGetValue(RefundVar.Key, out var val) ? val.IntValue : 0;
        if (refundAmount > 0 && cardPlay.Resources.EnergySpent > 0)
        {
            await PlayerCmd.GainEnergy(Math.Min(refundAmount, cardPlay.Resources.EnergySpent), cardPlay.Card.Owner);
        }

        if (PurgePatch.ShouldPurge(cardPlay.Card))
        {
            var deckCard = cardPlay.Card.DeckVersion;
            if (deckCard != null)
            {
                await CardPileCmd.RemoveFromDeck(deckCard, false);
            }
        }
    }
}