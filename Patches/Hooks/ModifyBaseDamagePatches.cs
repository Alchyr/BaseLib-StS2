using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Patches.Hooks;

/// <summary>
/// Patches for modifying the base damage of cards.
/// </summary>
[HarmonyPatch]
public class ModifyBaseDamagePatches
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
    static class ModifyDamageCalc
    {
        //Prefix is fine here because it does not need to be added to the modifiers list;
        //base value modifications function differently than the normal damage modification hook.
        [HarmonyPrefix]
        static void AdjustBaseAdditive(
            ref decimal damage,
            ValueProp props,
            CardModel? cardSource,
            ModifyDamageHookType modifyDamageHookType)
        {
            damage = ModifyBaseDamageAdditive(damage, props, cardSource, modifyDamageHookType);
        }

        [HarmonyTranspiler]
        static List<CodeInstruction> AdjustBaseMultiplicative(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            return new InstructionPatcher(code)
                .Match(new InstructionMatcher()
                    .ldargIndex(original.ArgIndex("damage"))
                    .stloc_any()
                )
                .Step(-1)
                .GetIndexOperand(out var damageLocal)
                .Match(new InstructionMatcher()
                    .ldargIndex(original.ArgIndex("target")))
                .Insert([
                    CodeInstruction.LoadLocal(damageLocal),
                    CodeInstruction.LoadArgument(original.ArgIndex("props")),
                    CodeInstruction.LoadArgument(original.ArgIndex("cardSource")),
                    CodeInstruction.LoadArgument(original.ArgIndex("modifyDamageHookType")),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ModifyBaseDamageMultiplicative)),
                    CodeInstruction.StoreLocal(damageLocal)
                ]);
        }
    }
    
    //TODO - Patches for damage/block vars, add to custom calculated vars. Updating for now for beta branch.

    /// <summary>
    /// Applies additional modifiers for base damage addition.
    /// </summary>
    public static decimal ModifyBaseDamageAdditive(decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType modifyDamageHookType)
    {
        if (!modifyDamageHookType.HasFlag(ModifyDamageHookType.Additive)) return damage;
        return ModifyBaseDamageAdditiveInternal(damage, props, cardSource);
    }

    /// <summary>
    /// Exists for convenience when patching in cases where additive modifiers are assumed to be applied.
    /// </summary>
    static decimal ModifyBaseDamageAdditiveInternal(decimal damage, ValueProp props, CardModel? cardSource)
    {
        if (cardSource != null)
        {
            foreach (var modifier in cardSource.GetModifiers())
            {
                damage += modifier.ModifyBaseDamageAdditive(damage, props);
            }
        }

        return Math.Max(damage, 0);
    }

    /// <summary>
    /// Applies additional modifiers for base damage multiplication.
    /// </summary>
    public static decimal ModifyBaseDamageMultiplicative(decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType modifyDamageHookType)
    {
        if (!modifyDamageHookType.HasFlag(ModifyDamageHookType.Multiplicative)) return damage;
        return ModifyBaseDamageMultiplicativeInternal(damage, props, cardSource);
    }
    
    /// <summary>
    /// Exists for convenience when patching in cases where multiplicative modifiers are assumed to be applied.
    /// </summary>
    static decimal ModifyBaseDamageMultiplicativeInternal(decimal damage, ValueProp props, CardModel? cardSource)
    {
        if (cardSource != null)
        {
            foreach (var modifier in cardSource.GetModifiers())
            {
                damage *= modifier.ModifyBaseDamageMultiplicative(damage, props);
            }
        }

        return Math.Max(damage, 0);
    }
}