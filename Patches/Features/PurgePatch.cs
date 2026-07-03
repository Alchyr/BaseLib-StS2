using System.Reflection;
using BaseLib.Cards;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Features;

[HarmonyPatch]
public class PurgePatch
{
    [HarmonyPatch(typeof(CardModel))]
    static class OldPurgePatch
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
    
        [HarmonyPrefix]
        static bool GoAwayForever(CardModel __instance, ref PileType __result)
        {
            if (ShouldPurge(__instance))
            {
                __result = PileType.None;
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(CardModel))]
    static class BetaPurgePatch
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
    
        [HarmonyPrefix]
        static bool GoAwayForever(CardModel __instance, ref (PileType, CardPilePosition) __result)
        {
            if (ShouldPurge(__instance))
            {
                __result = (PileType.None, CardPilePosition.Bottom);
                return false;
            }

            return true;
        }
    }

    public static bool ShouldPurge(CardModel c)
    {
        return c.Keywords.Contains(BaseLibKeywords.Purge);
    }
}