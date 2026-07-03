using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;

namespace BaseLib.Extensions;

public static class DynamicVarExtensions
{
    public static readonly SpireField<DynamicVar, Func<DynamicVar, IHoverTip>> DynamicVarTips = new(() => null);
    public static readonly SpireField<DynamicVar, decimal?> DynamicVarUpgrades = new(() => null);

    public static TDynamicVar WithUpgrade<TDynamicVar>(this TDynamicVar dynamicVar, decimal upgradeValue) where TDynamicVar : DynamicVar
    {
        if (upgradeValue != 0) DynamicVarUpgrades[dynamicVar] = upgradeValue;
        return dynamicVar;
    }

    //At the moment cardPlay being null seems fine - may change in the future
    public static decimal CalculateBlock(this DynamicVar var, Creature creature, ValueProp props, CardPlay? cardPlay = null, CardModel? cardSource = null)
    {
        decimal amount = var.BaseValue;

        if (!CombatManager.Instance.IsInProgress)
        {
            return amount;
        }

        if (CombatManager.Instance.IsEnding)
        {
            return amount;
        }

        var combatState = creature.CombatState;
        if (combatState == null) return amount;

        var enchantment = cardSource?.Enchantment;
        if (enchantment != null)
        {
            amount += enchantment.EnchantBlockAdditive(amount);
            amount *= enchantment.EnchantBlockMultiplicative(amount);
        }

        amount = Hook.ModifyBlock(combatState, creature, amount, props, cardSource, cardPlay, out var modifiers);
        amount = Math.Max(amount, 0m);
        return amount;
    }

    /// <summary>
    /// Adds a tooltip to a card this DynamicVar is attached to.
    /// By default will pull from the static_hover_tips table.
    /// The key will be the variable's name, with a mod prefix added, in the form of "PREFIX-NAME" (all capitalized).
    /// </summary>
    /// <returns></returns>
    public static TDynamicVar WithTooltip<TDynamicVar>(this TDynamicVar var, string? locKey = null, string locTable = "static_hover_tips") where TDynamicVar : DynamicVar
    {
        string key = locKey ?? var.GetType().GetPrefix() + StringHelper.Slugify(var.Name);

        DynamicVarTips[var] = (locVar) =>
        {
            LocString locString = new(locTable, key + ".title");
            LocString locString2 = new(locTable, key + ".description");

            locString.Add(locVar); //Dynamic var tip should not refer to any variables other than itself...
            locString2.Add(locVar);

            return new HoverTip(locString, locString2);
        };

        return var;
    }
    
    //Patch required to copy SpireField values to DynamicVar clones
    [HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.Clone))]
    class CloneTooltips
    {
        [HarmonyPostfix]
        static DynamicVar Copy(DynamicVar __result, DynamicVar __instance)
        {
            DynamicVarTips[__result] = DynamicVarTips[__instance];
            DynamicVarUpgrades[__result] = DynamicVarUpgrades[__instance];

            return __result;
        }
    }
}
