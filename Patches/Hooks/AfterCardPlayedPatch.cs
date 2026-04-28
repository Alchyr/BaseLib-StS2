using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Cards.Variables;
using BaseLib.Patches.Features;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
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

    private static async Task BeforeAfterPlayHooks(CardPlay cardPlay)
    {
        if (PostModInitPatch.CanModifyGameplay)
        {
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
}