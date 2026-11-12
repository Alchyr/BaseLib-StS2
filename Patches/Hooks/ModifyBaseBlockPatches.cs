using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Patches.Hooks;

/* TODO -
    Unlike damage, calculation extra variables are not modified by multiplication because there is no specific extra "block" variable.
    Can consider patching to add a check if an extra variable is referenced by a calculated block var in the same set,
    and if so, apply multiplication
 */

/// <summary>
/// Patches for modifying the base block of cards.
/// </summary>
[HarmonyPatch]
public static class ModifyBaseBlockPatches
{
    /// <summary>
    /// Modifies calculation used for actual block gain.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyBlock))]
    static class ModifyBlockCalc
    {
        //Prefix is fine here because it does not need to be added to the modifiers list;
        //base value modifications function differently than the normal modification hook.
        [HarmonyPrefix]
        static void AdjustBaseAdditive(
            ref decimal block,
            ValueProp props,
            CardModel? cardSource)
        {
            block = ModifyBaseBlockAdditive(block, props, cardSource);
        }

        [HarmonyTranspiler]
        static List<CodeInstruction> AdjustBaseMultiplicative(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            return new InstructionPatcher(code)
                .Match(new InstructionMatcher()
                    .ldargIndex(original.ArgIndex("block"))
                    .stloc_any()
                )
                .Step(-1)
                .GetIndexOperand(out var blockLocal)
                .Match(new InstructionMatcher()
                    .ldargIndex(original.ArgIndex("combatState")))
                .Step(-1)
                .TakeLabels(out var labels)
                .Insert([
                    CodeInstruction.LoadLocal(blockLocal).WithLabels(labels),
                    CodeInstruction.LoadArgument(original.ArgIndex("props")),
                    CodeInstruction.LoadArgument(original.ArgIndex("cardSource")),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ModifyBaseBlockMultiplicative)),
                    CodeInstruction.StoreLocal(blockLocal)
                ]);
        }
    }

    // Patch to modify "base block" used for preview when not in combat, and to determine highlighting when in combat
    [HarmonyPatch]
    static class ModifyBlockVars
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(BlockVar).Method(nameof(DynamicVar.UpdateCardPreview));
            yield return typeof(CalculatedBlockVar).Method(nameof(DynamicVar.UpdateCardPreview));
        }
        
        [HarmonyTranspiler]
        static List<CodeInstruction> AdjustBase(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var runGlobalHooksIndex = original.ArgIndex("runGlobalHooks");
            var enchantAdditive = new CallMatcher(typeof(EnchantmentModel)
                .Method(nameof(EnchantmentModel.EnchantBlockAdditive)));
            var enchantMultiplicative = typeof(EnchantmentModel)
                .Method(nameof(EnchantmentModel.EnchantBlockMultiplicative));
            var setPreview = typeof(DynamicVar).PropertySetter(nameof(DynamicVar.PreviewValue));

            var patcher = new InstructionPatcher(code);
            
            // Patch for the case where the card is enchanted.
            patcher.Match(enchantAdditive)
                .Match(new InstructionMatcher().stloc_any())
                .Step(-1).GetIndexOperand(out var calcLocIndex).Step()
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ModifyBaseBlockAdditive)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ])
                .Match(new CallMatcher(enchantMultiplicative))
                .Match(new InstructionMatcher().stloc_any())
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ModifyBaseBlockMultiplicative)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ]);
            
            //Patch for modifying second calculation in calculated vars
            //When run hooks are disabled, and card is enchanted
            patcher.TryMatch(enchantAdditive)?
                .TryMatch(new InstructionMatcher().stloc_any())?
                .Step(-1).GetIndexOperand(out calcLocIndex).Step()
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ModifyBaseBlockAdditive)),
                    CodeInstruction.StoreLocal(calcLocIndex)
                ])
                .TryMatch(new CallMatcher(enchantMultiplicative))?
                .TryMatch(new InstructionMatcher().stloc_any())?
                .Insert([
                    CodeInstruction.LoadLocal(calcLocIndex),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ValuePropForVar)),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(ModifyBaseBlockMultiplicative)),
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
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(CardOwnerForVar)),
                    CodeInstruction.Call(typeof(ModifyBaseBlockPatches), nameof(AdjustBaseUnenchanted)),
                    CodeInstruction.StoreLocal(numLocIndex),
                    CodeInstruction.LoadArgument(runGlobalHooksIndex) //restore stack
                ]);

            return patcher;
        }
    }
    
    /* This call is inserted before runGlobalHooks is checked.
     * 
     * For block var - num passed through is set to actual calculated value if runGlobalHooks
     * then PreviewValue is set to num
     *
     * For calculated block - num passed in is Calculate result.
     * Then, if runGlobalHooks, num is used as base value for calculation.
     * if !runGlobalHooks, num is used for preview value
     *
     * When runGlobalHooks is false, returned value should be "final" preview value
     */
    static decimal AdjustBaseUnenchanted(bool runGlobalHooks, decimal num, DynamicVar dynVar, CardModel? card)
    {
        if (card == null || card.Enchantment != null) return num;

        //First, calculate EnchantedValue to control highlighting.
        var modifiedBase = dynVar is CalculatedVar ? dynVar.BaseValue : num;
        var props = ValuePropForVar(dynVar);

        modifiedBase = ModifyBaseBlockAdditive(modifiedBase, props, card);
        modifiedBase = ModifyBaseBlockMultiplicative(modifiedBase, props, card);
        
        if (!card.IsEnchantmentPreview)
        {
            dynVar.EnchantedValue = modifiedBase;
        }
        
        //Now, calculate preview value.
        var preview = modifiedBase;
        if (dynVar is CalculatedVar) //use num (calculated base value) to calculate display value
        {
            preview = num;
            preview = ModifyBaseBlockAdditive(preview, props, card);
            preview = ModifyBaseBlockMultiplicative(preview, props, card);
        }
        
        //Have to set preview value manually for this case due to not having an enchantment
        if (card.IsEnchantmentPreview)
        {
            dynVar.PreviewValue = preview;
        }

        //If running global hooks, return base value (which will have calculation applied)
        //this method is used only to set EnchantedValue in this case.
        return runGlobalHooks ? num : preview;
    }
    
    private static decimal ModifyBaseBlockAdditive(decimal block, ValueProp props, CardModel? cardSource)
    {
        if (cardSource == null) return block;
        
        foreach (var modifier in cardSource.GetModifiers())
        {
            block += modifier.ModifyBaseBlockAdditive(block, props);
        }

        return block;
    }
    
    private static decimal ModifyBaseBlockMultiplicative(decimal block, ValueProp props, CardModel? cardSource)
    {
        if (cardSource == null) return block;
        
        foreach (var modifier in cardSource.GetModifiers())
        {
            block *= modifier.ModifyBaseBlockMultiplicative(block, props);
        }

        return block;
    }
    
    
    private static ValueProp ValuePropForVar(DynamicVar dynVar)
    {
        return dynVar switch
        {
            BlockVar block => block.Props,
            CalculatedBlockVar calculatedBlock => calculatedBlock.Props,
            _ => ValueProp.Move
        };
    }

    private static CardModel? CardOwnerForVar(DynamicVar dynVar)
    {
        return dynVar._owner switch
        {
            CardModel card => card,
            EnchantmentModel enchant => enchant._card,
            CardModifier modifier => modifier.Owner,
            _ => null
        };
    }
}