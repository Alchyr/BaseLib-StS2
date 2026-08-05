using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Patches.Hooks;
    
//TODO - Patches for block vars

/// <summary>
/// Patches for modifying the base damage of cards.
/// DynVar properties:
/// BaseValue - Base value, as expected. For calculated vars, the base var's value.
/// EnchantedValue - BaseValue increased by enchantments. Note for calculated vars it DOES NOT include extra amounts.
/// PreviewValue - The amount displayed on the card, which should generally be equivalent to final calculated damage.
/// </summary>
[HarmonyPatch]
public static class ModifyBaseDamagePatches
{
    /// <summary>
    /// Modifies calculation used for actual damage dealing.
    /// </summary>
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

    // Patch to modify "base damage" used for preview when not in combat, and to determine highlighting when in combat
    [HarmonyPatch]
    static class ModifyDamageVars
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(DamageVar).Method(nameof(DynamicVar.UpdateCardPreview));
            //yield return typeof(ExtraDamageVar).Method(nameof(DynamicVar.UpdateCardPreview)); only apply multiplicative
            yield return typeof(OstyDamageVar).Method(nameof(DynamicVar.UpdateCardPreview));
            yield return typeof(CalculatedDamageVar).Method(nameof(CalculatedDamageVar.UpdateCardPreview));
        }

        //Patch for modidfy calculation for enchanted cards
        [HarmonyTranspiler]
        static List<CodeInstruction> AdjustBaseEnchanted(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var runGlobalHooksIndex = original.ArgIndex("runGlobalHooks");
            var enchantAdditive = new CallMatcher(typeof(EnchantmentModel)
                .Method(nameof(EnchantmentModel.EnchantDamageAdditive)));
            var enchantMultiplicative = typeof(EnchantmentModel)
                .Method(nameof(EnchantmentModel.EnchantDamageMultiplicative));
            var setPreview = typeof(DynamicVar).PropertySetter(nameof(DynamicVar.PreviewValue));

            var patcher = new InstructionPatcher(code);
            
            // Patch for the case where the card is enchanted.
            patcher.Match(enchantAdditive)
                .Match(new InstructionMatcher().stloc_any())
                .Step(-1).GetIndexOperand(out var calcLocIndex).Step()
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ModifyBaseDamageAdditiveInternal)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ])
                .Match(new CallMatcher(enchantMultiplicative))
                .Match(new InstructionMatcher().stloc_any())
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ModifyBaseDamageMultiplicativeInternal)),
                    CodeInstruction.StoreLocal(calcLocIndex),
                ]);
            
            //Patch for modifying second calculation in calculated vars
            //When run hooks are disabled, and card is enchanted
            patcher.TryMatch(enchantAdditive)?
                .TryMatch(new InstructionMatcher().stloc_any())?
                .Step(-1).GetIndexOperand(out calcLocIndex).Step()
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ModifyBaseDamageAdditiveInternal)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ])
                .TryMatch(new CallMatcher(enchantMultiplicative))?
                .TryMatch(new InstructionMatcher().stloc_any())?
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ModifyBaseDamageMultiplicativeInternal)),
                    CodeInstruction.StoreLocal(calcLocIndex),
                ]);
            
            // If runGlobalHooks is false, and not enchanted, perform separate calculation to update preview
            // set preview value directly and also return a value, store in "num" variable
            patcher.MatchFromEnd(new InstructionMatcher()
                    .ldloc_any()
                    .call_any(setPreview)
                )
                .Step(-2).GetIndexOperand(out var numLocIndex)
                .ResetPosition()
                .Match(new InstructionMatcher()
                    .ldargIndex(runGlobalHooksIndex))
                .Insert([
                    CodeInstruction.LoadLocal(numLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(AdjustBaseUnenchanted)),
                    CodeInstruction.StoreLocal(numLocIndex),
                    CodeInstruction.LoadArgument(runGlobalHooksIndex) //restore stack
                ]); 

            return patcher;
        }
    }

    [HarmonyPatch(typeof(ExtraDamageVar), nameof(ExtraDamageVar.UpdateCardPreview))]
    static class ModifyExtraDamageVar
    {
        //Patch for modidfy calculation for enchanted cards
        [HarmonyTranspiler]
        static List<CodeInstruction> AdjustBaseEnchanted(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var enchantMultiplicative = typeof(EnchantmentModel)
                .Method(nameof(EnchantmentModel.EnchantDamageMultiplicative));
            var setPreview = typeof(DynamicVar).PropertySetter(nameof(DynamicVar.PreviewValue));

            var patcher = new InstructionPatcher(code);
            
            // Patch for the case where the card is enchanted.
            patcher.Match(new CallMatcher(enchantMultiplicative))
                .Match(new InstructionMatcher().stloc_any())
                .Step(-1).GetIndexOperand(out var calcLocIndex).Step()
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches),
                        nameof(ModifyBaseDamageMultiplicativeInternal)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ]);
            
            // If not enchanted, perform separate calculation to update preview
            patcher.MatchFromEnd(new InstructionMatcher()
                    .ldloc_any()
                    .call_any(setPreview)
                )
                .Step(-2).GetIndexOperand(out var numLocIndex).Step(1)
                .Insert([ //Have damage on stack, add var, props, and card owner, then store result and restore stack
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseDamagePatches), nameof(AdjustExtraUnenchanted)),
                    CodeInstruction.StoreLocal(numLocIndex),
                    CodeInstruction.LoadLocal(numLocIndex)
                ]); 

            return patcher;
        }
    }
    
    static decimal AdjustBaseUnenchanted(bool runGlobalHooks, decimal num, DynamicVar dynVar, CardModel card)
    {
        if (card.Enchantment != null) return num;

        var modifiedBase = dynVar is CalculatedVar ? dynVar.BaseValue : num;
        var props = ValuePropForVar(dynVar);

        modifiedBase = ModifyBaseDamageAdditiveInternal(modifiedBase, props, card);
        modifiedBase = ModifyBaseDamageMultiplicativeInternal(modifiedBase, props, card);
        
        if (!card.IsEnchantmentPreview)
        {
            dynVar.EnchantedValue = modifiedBase;
        }
        else
        {
            // Card is an enchantment preview, but has no enchantment.
            dynVar.PreviewValue = modifiedBase;
        }

        //If running global hooks, apply calculation only to set EnchantedValue to control highlighting color
        return runGlobalHooks ? num : modifiedBase;
    }

    static decimal AdjustExtraUnenchanted(decimal num, DynamicVar dynVar, ValueProp props, CardModel? card)
    {
        if (card == null || card.Enchantment != null) return num;

        var modifiedBase = ModifyBaseDamageMultiplicativeInternal(num, props, card);
        
        if (!card.IsEnchantmentPreview)
        {
            dynVar.EnchantedValue = modifiedBase;
        }
        else
        {
            // Card is an enchantment preview, but has no enchantment.
            dynVar.PreviewValue = modifiedBase;
        }

        return modifiedBase;
    }

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
    private static decimal ModifyBaseDamageAdditiveInternal(decimal damage, ValueProp props, CardModel? cardSource)
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
    /// Returns modified damage.
    /// </summary>
    public static decimal ModifyBaseDamageMultiplicative(decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType modifyDamageHookType)
    {
        if (!modifyDamageHookType.HasFlag(ModifyDamageHookType.Multiplicative)) return damage;
        return ModifyBaseDamageMultiplicativeInternal(damage, props, cardSource);
    }
    
    /// <summary>
    /// Exists for convenience when patching in cases where multiplicative modifiers are assumed to be applied.
    /// </summary>
    private static decimal ModifyBaseDamageMultiplicativeInternal(decimal damage, ValueProp props, CardModel? cardSource)
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

    private static ValueProp ValuePropForVar(DynamicVar dynVar)
    {
        return dynVar switch
        {
            DamageVar damage => damage.Props,
            OstyDamageVar ostyDamage => ostyDamage.Props,
            CalculatedDamageVar calculatedDamage => calculatedDamage.Props,
            _ => ValueProp.Move
        };
    }

    private static CardModel? CardOwnerForVar(DynamicVar dynVar)
    {
        return dynVar._owner as CardModel;
    }
}