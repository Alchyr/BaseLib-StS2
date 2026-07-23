using System.Reflection;
using BaseLib.Cards;
using BaseLib.Utils.Patching;
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
            if (TargetMethod != null) return true;
            BaseLibMain.Logger.Info("No valid target found, skipping old PurgePatch");
            return false;
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
            AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeAndPositionForCardPlay")
            ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultLocationForCardPlay");
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (TargetMethod != null) yield return TargetMethod;
        }

        static bool Prepare()
        {
            if (TargetMethod != null) return true;
            BaseLibMain.Logger.Info("No valid target found, skipping beta PurgePatch");
            return false;
        }
    
        [HarmonyTranspiler]
        static List<CodeInstruction> GoAwayForever(IEnumerable<CodeInstruction> code)
        {
            return new InstructionPatcher(code)
                .Match(new CallMatcher(typeof(CardModel).PropertyGetter("IsDupe")))
                .Insert([
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(BetaPurgePatch), nameof(BetaPurgePatch.AlterResult))
                ]);
        }

        private static bool AlterResult(bool origIsDupe, CardModel card)
        {
            return origIsDupe || ShouldPurge(card);
        }
    }

    public static bool ShouldPurge(CardModel c)
    {
        return c.Keywords.Contains(BaseLibKeywords.Purge);
    }
}