using BaseLib.Abstracts;
using BaseLib.Hooks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Hooks;

/// <summary>
/// IHealAmountModifier.ModifyHealAdditive() -> AbstractModel.ModifyHealAmount() -> IHealAmountModifier.ModifyHealMultiplicative()
/// reserve AbstractModel.ModifyHealAmount() in the process for compatibility
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyHealAmount))]
public static class ModifyHealAmountPatches
{
    [HarmonyPrefix]
    static void Prefix(IRunState runState, CombatState? combatState, Creature creature, ref decimal amount)
    {
        decimal num = amount;

        foreach (var item in runState.IterateHookListeners(combatState))
        {
            if (item is IHealAmountModifier mod)
                num += mod.ModifyHealAdditive(creature, amount); // pass the amount before any addition
        }

        amount = num;
    }

    [HarmonyPostfix]
    static void Postfix(IRunState runState, CombatState? combatState, Creature creature, ref decimal __result)
    {
        decimal num = __result;

        foreach (var item in runState.IterateHookListeners(combatState))
        {
            if (item is IHealAmountModifier mod)
                num *= mod.ModifyHealMultiplicative(creature, __result); // pass the amount before any multiplication
        }

        __result = Math.Max(0m, num);
    }
}
