using BaseLib.Cards.Variables;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Features;

[HarmonyPatch(typeof(CardModel), "GetResultPileType")]
public static class ExhaustivePatch
{
    [HarmonyPostfix]
    static void ExhaustForExhaustive(CardModel __instance, ref PileType __result)
    {
        if (ExhaustForExhaustive(__instance))
        {
            __result = PileType.Exhaust;
        }
    }

    static bool ExhaustForExhaustive(CardModel card)
    {
        return GetExhaustive(card) == 1;
    }
    

    public static int GetExhaustive(CardModel card)
    {
        var exhaustiveAmount = card.DynamicVars.TryGetValue(ExhaustiveVar.Key, out var val) ? val.IntValue : 0;
        return ExhaustiveVar.ExhaustiveCount(card, exhaustiveAmount);
    }
}