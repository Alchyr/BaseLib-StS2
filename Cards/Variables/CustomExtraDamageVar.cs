using BaseLib.Patches.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Cards.Variables;

/// <summary>
/// ExtraDamageVar whose value is modified by damage enchantments and can be named,
/// for use with CustomCalculatedDamageVar.
/// </summary>
/// <param name="baseName">The name of the CustomCalculatedDamageVar this is for. This variable's name is the base name + "Extra".</param>
/// <param name="damage">The amount of bonus damage that will be multiplied by the calculated variable's multiplier calc.</param>
public class CustomExtraDamageVar(string baseName, decimal damage) : DynamicVar(baseName + "Extra", damage)
{
    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode previewMode,
        Creature? target,
        bool runGlobalHooks)
    {
        var num = BaseValue;
        
        num = ModifyBaseDamagePatches.ModifyBaseDamageMultiplicative(num, ValueProp.Move, card, ModifyDamageHookType.All);
        EnchantedValue = num;
        
        var enchantment = card.Enchantment;
        if (enchantment != null)
        {
            num *= enchantment.EnchantDamageMultiplicative(num, ValueProp.Move);
            if (!card.IsEnchantmentPreview)
                EnchantedValue = num;
        }
        
        PreviewValue = num;
    }
}