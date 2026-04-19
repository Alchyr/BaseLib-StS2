using System.Reflection.Emit;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

/// <summary>
/// Contains patches allowing mods to customize card descriptions globally.
/// These are intended for mods that add new keyword-like effects that don't necessarily work as actual keywords.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForPile),
    typeof(PileType), typeof(CardModel.DescriptionPreviewType), typeof(Creature))]
public static class DescriptionOverrides
{
    public delegate void CustomizeDescriptionHandler(CardModel card, Creature? target, ref string description);
    
    /// <summary>
    /// Allows customizing a card's description before it is processed by the game.
    /// </summary>
    public static event CustomizeDescriptionHandler? CustomizeDescription;

    /// <summary>
    /// Allow customizing a card's description after it has been processed by the game.
    /// </summary>
    public static event CustomizeDescriptionHandler? CustomizeDescriptionPost;
    
    [HarmonyTranspiler]
    static List<CodeInstruction> TranspileGetDescriptionForPile(IEnumerable<CodeInstruction> instructionsIn)
    {
        return new InstructionPatcher(instructionsIn)
            .Match(new InstructionMatcher()
                    .ldloc_0()
                    .callvirt(typeof(LocString), nameof(LocString.GetFormattedText))
                    .opcode(OpCodes.Stind_Ref)
                    .stloc_s() //Store list with description in index 0
            )
            .Step(-1)
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.LoadArgument(3),
                CodeInstruction.Call(typeof(DescriptionOverrides), nameof(InvokeCustomize))
            ]);
    }
    
    [HarmonyPostfix]
    internal static void InvokeCustomizePost(CardModel __instance, Creature? target, ref string __result)
    {
        CustomizeDescriptionPost?.Invoke(__instance, target, ref __result);
    }

    internal static List<string> InvokeCustomize(List<string> descriptionList, CardModel card, Creature? target)
    {
        if (descriptionList.Count == 0) return descriptionList;

        var s = descriptionList[0];
        CustomizeDescription?.Invoke(card, target, ref s);
        descriptionList[0] = s;
        return descriptionList;
    }
}