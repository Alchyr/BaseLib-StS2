using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// A calculated var that allows multiple on a single model and works on relics, powers, and potions.
/// Requires two additional vars with the same name ending in "Base" and "Extra".
/// A CustomCalculatedVar named "Fire" would need "FireBase" and "FireExtra".
/// WithMultiplier should be called and provide a multiplier calc using the variable's owner's type.
/// A multiplier calc can be individually defined for cards, relics, and powers.
/// </summary>
public class CustomCalculatedVar : CalculatedVar
{
    private Func<RelicModel, Creature?, decimal>? _relicCalc = null;
    private Func<PowerModel, Creature?, decimal>? _powerCalc = null;
    private Func<DynamicVarSource, Creature?, decimal>? _generalCalc = null;
    
    public CustomCalculatedVar(string name) : base(name)
    {
        
    }

    public virtual decimal CalculateCustom(Creature? target)
    {
        return CalculateCustomVar(this, GetBaseVar(), GetExtraVar(), target,
            Calculate, _relicCalc, _powerCalc, _generalCalc);
    }

    public static decimal CalculateCustomVar(DynamicVar dynVar, DynamicVar baseVar, DynamicVar extraVar,
        Creature? target,
        Func<Creature?, decimal> cardCalc,
        Func<RelicModel, Creature?, decimal>? relicCalc,
        Func<PowerModel, Creature?, decimal>? powerCalc,
        Func<DynamicVarSource, Creature?, decimal>? generalCalc)
    {
        var owner = dynVar._owner;
        switch (owner)
        {
            case CardModel:
                return cardCalc(target);
            case PowerModel power:
                var mult = powerCalc?.Invoke(power, target) ??
                                 throw new InvalidOperationException(
                                     $"{dynVar.GetType().Name} {dynVar.Name} does not have multiplier calc defined for powers in {owner.Id}");
                return baseVar.BaseValue + extraVar.BaseValue * mult;
            case RelicModel relic:
                mult = relicCalc?.Invoke(relic, target) ?? throw new InvalidOperationException(
                         $"{dynVar.GetType().Name} {dynVar.Name} does not have multiplier calc defined for relics in {owner.Id}");
                return baseVar.BaseValue + extraVar.BaseValue * mult;
            case PotionModel potion:
                mult = generalCalc?.Invoke(potion, target) ?? throw new InvalidOperationException(
                    $"{dynVar.GetType().Name} {dynVar.Name} does not have multiplier calc defined for potions in {owner.Id}");
                return baseVar.BaseValue + extraVar.BaseValue * mult; 
            //Treated same as normal cards where calculation is disabled out of combat
            case EnchantmentModel enchant:
                mult = (!CombatManager.Instance.IsInProgress || 
                        enchant.Card.Owner.Creature.CombatState == null) ? 0 : 
                    generalCalc?.Invoke(enchant, target) ?? throw new InvalidOperationException(
                    $"{dynVar.GetType().Name} {dynVar.Name} does not have multiplier calc defined for enchantments in {owner.Id}");
                return baseVar.BaseValue + extraVar.BaseValue * mult;
            case CardModifier modifier:
                mult = (!CombatManager.Instance.IsInProgress || 
                        modifier.Owner!.Owner.Creature.CombatState == null) ? 0 : 
                    generalCalc?.Invoke(modifier, target) ?? throw new InvalidOperationException(
                        $"{dynVar.GetType().Name} {dynVar.Name} does not have multiplier calc defined for card modifiers in {owner.Id}");
                return baseVar.BaseValue + extraVar.BaseValue * mult;
            default:
                return dynVar.BaseValue;
        }
    }
    
    /// <summary>
    /// Sets a multiplier calculation for a relic. Note that calculation for relics is allowed to occur out of combat,
    /// so combat state may be null.
    /// </summary>
    public CalculatedVar WithMultiplier(Func<RelicModel, Creature?, decimal> multiplierCalc)
    {
        if (_relicCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for relic on CustomCalculatedVar {Name} twice!");
        _relicCalc = multiplierCalc.Target is not AbstractModel ? multiplierCalc : throw new InvalidOperationException("Multiplier calc must be static!");
        return this;
    }
    
    /// <summary>
    /// Sets a multiplier calculation for a power.
    /// </summary>
    public CalculatedVar WithMultiplier(Func<PowerModel, Creature?, decimal> multiplierCalc)
    {
        if (_powerCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for power on CustomCalculatedVar {Name} twice!");
        _powerCalc = multiplierCalc.Target is not AbstractModel ? multiplierCalc : throw new InvalidOperationException("Multiplier calc must be static!");
        return this;
    }
    
    /// <summary>
    /// Sets a calculation that supports any type that can be cast to <see cref="DynamicVarSource"/>.
    /// Note that calculation for relics and potions is allowed to occur out of combat, so combat state may be null.
    /// </summary>
    public CalculatedVar GeneralMultiplier(Func<DynamicVarSource, Creature?, decimal> multiplierCalc)
    {
        if (_generalCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for CustomCalculatedVar {Name} twice!");
        if (multiplierCalc.Target is AbstractModel) throw new InvalidOperationException("Multiplier calc must be static!");

        WithMultiplier((CardModel card, Creature? c) => multiplierCalc(card, c));
        _powerCalc = (pow, target) => multiplierCalc(pow, target);
        _relicCalc = (pow, target) => multiplierCalc(pow, target);
        _generalCalc = multiplierCalc;
        return this;
    }

    protected override DynamicVar GetBaseVar()
    {
        return _owner!.GetDynamicVar($"{Name}Base");
    }

    protected override DynamicVar GetExtraVar()
    {
        return _owner!.GetDynamicVar($"{Name}Extra");
    }

    protected override decimal GetBaseValueForIConvertible() => CalculateCustom(null);

    /// <inheritdoc />
    public override string ToString() => CalculateCustom(null).ToString();
}