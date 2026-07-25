using BaseLib.Extensions;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

/// <summary>
/// Adds additional tips to a model's hovertips.
/// </summary>
[HarmonyPatch]
public class ExtraTooltips
{
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
    [HarmonyTranspiler]
    static List<CodeInstruction> AddCustomCardTips(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .callvirt(AccessTools.PropertyGetter(typeof(CardModel), "ExtraHoverTips"))
                .call(null)
                .stloc_0()
            )
            .Insert([
                CodeInstruction.LoadLocal(0), //Load stored list
                CodeInstruction.LoadArgument(0), //Load card
                CodeInstruction.Call(typeof(ExtraTooltips), "AddTips"), //add tips to list
            ]);
    }
    
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
    [HarmonyPostfix]
    static IEnumerable<IHoverTip> AddCustomRelicTips(IEnumerable<IHoverTip> __result, RelicModel __instance)
    {
        if (__result is ICollection<IHoverTip> tipCollection)
        {
            AddTipsGeneric(tipCollection, __instance);
        }

        return __result;
    }
    
    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
    [HarmonyPostfix]
    static IEnumerable<IHoverTip> AddCustomPowerTips(IEnumerable<IHoverTip> __result, PowerModel __instance)
    {
        if (__result is ICollection<IHoverTip> tipCollection)
        {
            AddTipsGeneric(tipCollection, __instance);
        }

        return __result;
    }

    /// <summary>
    /// Adds additional tips to a card model's hovertips.
    /// </summary>
    public static void AddTips(List<IHoverTip> tips, CardModel card)
    {
        AddTipsGeneric(tips, card);
        foreach (var cardMod in card.GetModifiers())
        {
            cardMod.AddTips(tips);
        }
    }

    static void AddTipsGeneric(ICollection<IHoverTip> tips, DynamicVarSource dynVarSource)
    {
        foreach (var dynVar in dynVarSource.DynamicVars.Values)
        {
            var tip = DynamicVarExtensions.DynamicVarTips[dynVar]?.Invoke(dynVar);
            if (tip != null) tips.Add(tip);
        }
    }
}