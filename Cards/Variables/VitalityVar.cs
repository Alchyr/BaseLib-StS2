using BaseLib.Extensions;
using BaseLib.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// Represents a dynamic variable for the "Vitality" keyword, responsible for calculating 
/// and updating the vitality value shown on card previews, including active combat modifiers.
/// </summary>
public class VitalityVar : DynamicVar
{
    /// <summary>
    /// Creates a new vitality variable named <c>"Vitality"</c> and registers its hover tooltip
    /// via <see cref="DynamicVarExtensions.WithTooltip{TDynamicVar}"/>, which resolves the
    /// <c>BASELIB-VITALITY</c> localization entries (preferring <c>.smartDescription</c> when available).
    /// </summary>
    /// <param name="baseValue">The base vitality amount before modifiers are applied.</param>
    public VitalityVar(decimal baseValue) : base("Vitality", baseValue)
    {
        this.WithTooltip();
    }
    
    /// <summary>
    /// Updates the preview value of the vitality variable on the card.
    /// Passes the current integer value through global vitality modification hooks if enabled.
    /// </summary>
    /// <param name="card">The card model displaying this variable.</param>
    /// <param name="previewMode">The mode dictating how the card preview is rendered.</param>
    /// <param name="target">The target creature of the card action, if any.</param>
    /// <param name="runGlobalHooks">
    /// If <see langword="true"/>, routes the base value through <see cref="BaseLibHooks.ModifyVitalityAmount"/> 
    /// to account for relics, powers, or status effects that alter vitality counts.
    /// </param>
    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode previewMode,
        Creature? target,
        bool runGlobalHooks)
    {
        var vitalityAmount = BaseValue;
        if (runGlobalHooks)
            vitalityAmount = BaseLibHooks.ModifyVitalityAmount(card.CombatState, card.Owner.Creature, BaseValue, card, null, out _);
        PreviewValue = vitalityAmount;
    }
}