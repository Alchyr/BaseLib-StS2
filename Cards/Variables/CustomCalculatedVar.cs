using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// A calculated var that allows multiple on a single model and works on relics and powers.
/// Requires two additional vars with the same name ending in "Base" and "Extra".
/// A CustomCalculatedVar named "Fire" would need "FireBase" and "FireExtra".
/// WithMultiplier should be called and provide a multiplier calc using the variable's owner's type.
/// A multiplier calc can be individually defined for cards, relics, and powers.
/// </summary>
public class CustomCalculatedVar : CalculatedVar
{
    private Func<RelicModel, Creature?, decimal>? _relicCalc = null;
    private Func<PowerModel, Creature?, decimal>? _powerCalc = null;
    
    public CustomCalculatedVar(string name) : base(name)
    {
        
    }

    protected virtual decimal CalculateCustom(Creature? target)
    {
        switch (_owner)
        {
            case CardModel:
                return Calculate(target);
            case PowerModel power:
                return _powerCalc?.Invoke(power, target) ??
                       throw new InvalidOperationException(
                           $"CustomCalculatedVar {Name} does not have multiplier calc defined for powers in {_owner.Id}");
            case RelicModel relic:
                return _relicCalc?.Invoke(relic, target) ??
                       throw new InvalidOperationException(
                           $"CustomCalculatedVar {Name} does not have multiplier calc defined for relics in {_owner.Id}");
            default:
                return BaseValue;
        }
    }
    
    public CalculatedVar WithMultiplier(Func<RelicModel, Creature?, decimal> multiplierCalc)
    {
        if (_relicCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for relic on CustomCalculatedVar {Name} twice!");
        _relicCalc = multiplierCalc.Target is not AbstractModel ? multiplierCalc : throw new InvalidOperationException("Multiplier calc must be static!");
        return this;
    }
    public CalculatedVar WithMultiplier(Func<PowerModel, Creature?, decimal> multiplierCalc)
    {
        if (_powerCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for power on CustomCalculatedVar {Name} twice!");
        _powerCalc = multiplierCalc.Target is not AbstractModel ? multiplierCalc : throw new InvalidOperationException("Multiplier calc must be static!");
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