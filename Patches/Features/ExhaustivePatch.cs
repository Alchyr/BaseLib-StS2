using System.Reflection;
using BaseLib.Cards.Variables;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Features;

[HarmonyPatch]
public static class ExhaustivePatch
{
    [HarmonyPatch(typeof(CardModel))]
    static class OldExhaustivePatch
    {
        static MethodInfo? TargetMethod = AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeForCardPlay")
                                          ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileType");
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (TargetMethod != null) yield return TargetMethod;
        }

        static bool Prepare()
        {
            return TargetMethod != null;
        }
    
        [HarmonyPostfix]
        static void ExhaustForExhaustive(CardModel __instance, ref PileType __result)
        {
            if (ShouldExhaustForExhaustive(__instance))
            {
                __result = PileType.Exhaust;
            }
        }
    }
    
    [HarmonyPatch(typeof(CardModel))]
    static class BetaExhaustivePatch
    {
        private static MethodInfo? TargetMethod =
            AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeAndPositionForCardPlay");
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (TargetMethod != null) yield return TargetMethod;
        }

        static bool Prepare()
        {
            return TargetMethod != null;
        }
    
        [HarmonyPostfix]
        static void ExhaustForExhaustive(CardModel __instance, ref (PileType, CardPilePosition) __result)
        {
            if (ShouldExhaustForExhaustive(__instance))
            {
                __result = (PileType.Exhaust, CardPilePosition.Bottom);
            }
        }
    }

    static bool ShouldExhaustForExhaustive(CardModel card)
    {
        return GetExhaustive(card) == 1;
    }
    

    public static int GetExhaustive(CardModel card)
    {
        var exhaustiveAmount = card.DynamicVars.TryGetValue(ExhaustiveVar.Key, out var val) ? val.IntValue : 0;
        return ExhaustiveVar.ExhaustiveCount(card, exhaustiveAmount);
    }
}