using BaseLib.Extensions;
using BaseLib.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// Represents a dynamic variable for the "Scry" keyword, responsible for calculating 
/// and updating the scry value shown on card previews, including active combat modifiers.
/// </summary>
public class ScryVar : DynamicVar
{
    /// <summary>
    /// Creates a new scry variable named <c>"Scry"</c> and registers its hover tooltip
    /// via <see cref="DynamicVarExtensions.WithTooltip{TDynamicVar}"/>, which resolves the
    /// <c>BASELIB-SCRY</c> localization entries (preferring <c>.smartDescription</c> when available).
    /// </summary>
    /// <param name="baseValue">The base scry amount before modifiers are applied.</param>
    public ScryVar(decimal baseValue) : base("Scry", baseValue)
    {
        this.WithTooltip();
    }
    
    
    /// <summary>
    /// Updates the preview value of the scry variable on the card.
    /// Passes the current integer value through global scry modification hooks if enabled.
    /// </summary>
    /// <param name="card">The card model displaying this variable.</param>
    /// <param name="previewMode">The mode dictating how the card preview is rendered.</param>
    /// <param name="target">The target creature of the card action, if any.</param>
    /// <param name="runGlobalHooks">
    /// If <see langword="true"/>, routes the base value through <see cref="BaseLibHooks.ModifyScryAmount"/> 
    /// to account for relics, powers, or status effects that alter scry counts.
    /// </param>
    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode previewMode,
        Creature? target,
        bool runGlobalHooks)
    {
        var scryAmount = IntValue;
        
        if (runGlobalHooks)
        {
            // Passes the value through global listeners (relics, powers, etc.) to get the modified preview total
            scryAmount = BaseLibHooks.ModifyScryAmount(card.Owner, scryAmount, out _);
        }
            
        PreviewValue = scryAmount;
    }
}