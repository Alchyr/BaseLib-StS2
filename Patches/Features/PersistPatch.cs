using System.Collections.Generic;
using System.Reflection;
using BaseLib.Cards.Variables;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Features;


[HarmonyPatch(typeof(CardModel))]
public static class PersistPatch
{
    static MethodInfo? TargetMethod = 
        AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeForCardPlay")
        ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileType")
        ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultPileTypeAndPositionForCardPlay")
        ?? AccessTools.DeclaredMethod(typeof(CardModel), "GetResultLocationForCardPlay");
        
    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TargetMethod != null) yield return TargetMethod;
    }

    static bool Prepare()
    {
        if (TargetMethod != null) return true;
        BaseLibMain.Logger.Info("No valid target found, skipping PersistPatch");
        return false;
    }
    
    [HarmonyTranspiler]
    static List<CodeInstruction> AltDestination(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .MatchFromEnd(new InstructionMatcher()
                .ldc_i4_3()
            )
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(PersistPatch), nameof(NormalOrPersist)),
            ]);
    }

    //patched to be lower priority than exhaust
    static PileType NormalOrPersist(PileType dest, CardModel model)
    {
        if (dest == PileType.Discard && model.IsPersist())
        {
            return PileType.Hand;
        }
        return dest;
    }
    
    public static bool IsPersist(this CardModel card)
    {
        var persistAmount = card.DynamicVars.TryGetValue(PersistVar.Key, out var val) ? val.IntValue : 0;
        return PersistVar.PersistCount(card, persistAmount) > 0;
    }
}
