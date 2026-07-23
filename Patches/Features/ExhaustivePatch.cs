using System.Reflection;
using BaseLib.Cards.Variables;
using BaseLib.Utils.Patching;
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
            if (TargetMethod != null) return true;
            BaseLibMain.Logger.Info("No valid target found, skipping old ExhaustivePatch");
            return false;
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
            AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeAndPositionForCardPlay")
            ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultLocationForCardPlay");
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (TargetMethod != null) yield return TargetMethod;
        }

        static bool Prepare()
        {
            if (TargetMethod != null) return true;
            BaseLibMain.Logger.Info("No valid target found, skipping beta ExhaustivePatch");
            return false;
        }
    
        [HarmonyTranspiler]
        static List<CodeInstruction> ExhaustForExhaustive(IEnumerable<CodeInstruction> code)
        {
            return new InstructionPatcher(code)
                .Match(new CallMatcher(typeof(CardModel).PropertyGetter(nameof(CardModel.ExhaustOnNextPlay))))
                .Insert([
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(BetaExhaustivePatch), nameof(AlterResult))
                ]);
        }

        private static bool AlterResult(bool origIsExhaustNextUse, CardModel card)
        {
            return origIsExhaustNextUse || ShouldExhaustForExhaustive(card);
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