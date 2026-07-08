using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

/// <summary>
/// Represents a class that contains a DynamicVarSet.
/// </summary>
public sealed class DynamicVarSource()
{
    public required DynamicVarSet DynamicVars { get; init; }
    public required Creature? Owner { get; init; }
    
    //Used as cardsource when passing a DynamicVarSource to common actions
    public CardModel? Card { get; init; }
    //Unused
    public RelicModel? Relic { get; init; }
    //Unused
    public PowerModel? Power { get; init; }
    
    
    
    public static implicit operator DynamicVarSource(CardModel card)
    {
        return new DynamicVarSource
        {
            DynamicVars = card.DynamicVars,
            Owner = card is { IsMutable: true, Owner: not null } ? card.Owner.Creature : null,
            Card = card
        };
    }
    
    public static implicit operator DynamicVarSource(RelicModel relic)
    {
        return new DynamicVarSource
        {
            DynamicVars = relic.DynamicVars,
            Owner = relic is { IsMutable: true, Owner: not null } ? relic.Owner.Creature : null,
            Relic = relic
        };
    }
    
    public static implicit operator DynamicVarSource(PowerModel power)
    {
        return new DynamicVarSource
        {
            DynamicVars = power.DynamicVars,
            Owner = power is { IsMutable: true, Owner: not null } ? power.Owner : null,
            Power = power
        };
    }
    
    public static implicit operator DynamicVarSource(PotionModel potion)
    {
        return new DynamicVarSource
        {
            DynamicVars = potion.DynamicVars,
            Owner = potion is { IsMutable: true, Owner: not null } ? potion.Owner.Creature : null
        };
    }
    
    public static implicit operator DynamicVarSource(EnchantmentModel enchant)
    {
        return new DynamicVarSource
        {
            DynamicVars = enchant.DynamicVars,
            Owner = enchant is { IsMutable: true, Card: not null, Card.IsMutable: true, Card.Owner: not null } ? enchant.Card.Owner.Creature : null,
            Card = enchant.Card
        };
    }
    
    public static implicit operator DynamicVarSource(CardModifier modifier)
    {
        return new DynamicVarSource
        {
            DynamicVars = modifier.DynamicVars,
            Owner = modifier.Owner?.IsMutable == true ? modifier.Owner?.Owner.Creature : null,
            Card = modifier.Owner
        };
    }
}