using System.Reflection;
using BaseLib.Config;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.UI;

/// <summary>
/// Shows a "from which mod?" line for modded content. Content that already has its own text tip
/// (relics, powers, potions, enchantments, afflictions, orbs) gets the line folded into that tip;
/// cards, which have no own text tip on hover, get a dedicated tip instead. (Monsters are handled
/// on their nameplate by <see cref="MonsterSourceLabel"/>.)
/// </summary>
[HarmonyPatch]
static class ModSourceTooltip
{
    private const string FoldedLineColor = "#8a8a8a";

    private static readonly FieldInfo? DescriptionField =
        AccessTools.Field(typeof(HoverTip), "<Description>k__BackingField");

    private static LocString TitleLoc() => new("gameplay_ui", "BASELIB-MOD_SOURCE.title");

    /// <summary>
    /// Folds the source line into the model's own hover tip (the first tip, or the last for orbs).
    /// </summary>
    private static IEnumerable<IHoverTip> Fold(IEnumerable<IHoverTip> tips, AbstractModel model, bool foldLast = false)
    {
        var name = WhatMod.FindModName(model.GetType());
        if (name == null) return tips;

        var list = tips.ToList();
        var index = foldLast ? list.Count - 1 : 0;
        if (DescriptionField != null && index >= 0 && list[index] is HoverTip own)
        {
            // Box a copy and edit that, so a cached/shared original tip is never mutated.
            object boxed = own;
            var line = $"[color={FoldedLineColor}]{TitleLoc().GetFormattedText()}: {name}[/color]";
            DescriptionField.SetValue(boxed, $"{own.Description}\n{line}");
            list[index] = (IHoverTip)boxed;
        }
        else
        {
            list.Add(new HoverTip(TitleLoc(), name) { Id = $"BASELIB-MOD_SOURCE-{name}" });
        }
        return list;
    }

    /// <summary>
    /// Appends a dedicated source tip for content with no own text tip on hover.
    /// </summary>
    private static IEnumerable<IHoverTip> AppendBox(IEnumerable<IHoverTip> tips, AbstractModel model)
    {
        var name = WhatMod.FindModName(model.GetType());
        return name == null
            ? tips
            : tips.Append(new HoverTip(TitleLoc(), name) { Id = $"BASELIB-MOD_SOURCE-{name}" });
    }

    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
    private static class RelicTips
    {
        [HarmonyPostfix]
        static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowRelicModSource ? Fold(__result, __instance) : __result;
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
    private static class PotionTips
    {
        [HarmonyPostfix]
        static void Postfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowPotionModSource ? Fold(__result, __instance) : __result;
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
    private static class CardTips
    {
        [HarmonyPostfix]
        static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowCardModSource ? AppendBox(__result, __instance) : __result;
    }

    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
    private static class PowerTips
    {
        [HarmonyPostfix]
        static void Postfix(PowerModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowCombatElementModSource ? Fold(__result, __instance) : __result;
    }

    [HarmonyPatch(typeof(OrbModel), nameof(OrbModel.HoverTips), MethodType.Getter)]
    private static class OrbTips
    {
        [HarmonyPostfix]
        static void Postfix(OrbModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowCombatElementModSource ? Fold(__result, __instance, foldLast: true) : __result;
    }

    [HarmonyPatch(typeof(EnchantmentModel), nameof(EnchantmentModel.HoverTips), MethodType.Getter)]
    private static class EnchantmentTips
    {
        [HarmonyPostfix]
        static void Postfix(EnchantmentModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowCombatElementModSource ? Fold(__result, __instance) : __result;
    }

    [HarmonyPatch(typeof(AfflictionModel), nameof(AfflictionModel.HoverTips), MethodType.Getter)]
    private static class AfflictionTips
    {
        [HarmonyPostfix]
        static void Postfix(AfflictionModel __instance, ref IEnumerable<IHoverTip> __result)
            => __result = BaseLibConfig.ShowCombatElementModSource ? Fold(__result, __instance) : __result;
    }
}
