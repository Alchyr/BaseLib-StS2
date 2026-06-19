using System.Globalization;
using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Cards.Variables;

/// <summary>
/// A CalculatedDamageVar that allows a custom name and can have multiple on one model.
/// Also works on relics and powers.
/// Requires two additional vars with the same name ending in "Base" and "Extra".
/// A CalculatedBlockVar named "Fire" would need "FireBase" and "FireExtra".
/// WithMultiplier should be called and provide a multiplier calc using the variable's owner's type.
/// A multiplier calc can be individually defined for cards, relics, and powers.
/// </summary>
public class CustomCalculatedDamageVar : CalculatedDamageVar
{
    private static Action<DynamicVar, string>? _nameSetter = ReflectionUtils.GetSetterForProperty<DynamicVar, string>("Name");
    
    private Func<RelicModel, Creature?, decimal>? _relicCalc = null;
    private Func<PowerModel, Creature?, decimal>? _powerCalc = null;
    private Func<DynamicVarSource, Creature?, decimal>? _generalCalc = null;
    
    public CustomCalculatedDamageVar(string name, ValueProp props) : base(props)
    {
        _nameSetter?.Invoke(this, name);
    }

    /// <summary>
    /// Calculates the final value of this variable.
    /// </summary>
    public virtual decimal CalculateCustom(Creature? target)
    {
        return CustomCalculatedVar.CalculateCustomVar(this, GetBaseVar(), GetExtraVar(), target,
            Calculate, _relicCalc, _powerCalc, _generalCalc);
    }
    
    /// <summary>
    /// Sets a multiplier calculation for a relic. Note that calculation for relics is allowed to occur out of combat,
    /// so combat state may be null.
    /// </summary>
    public CalculatedVar WithMultiplier(Func<RelicModel, Creature?, decimal> multiplierCalc)
    {
        if (_relicCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for relic on CustomCalculatedDamageVar {Name} twice!");
        _relicCalc = multiplierCalc.Target is not AbstractModel ? multiplierCalc : throw new InvalidOperationException("Multiplier calc must be static!");
        return this;
    }
    
    /// <summary>
    /// Sets a multiplier calculation for a power.
    /// </summary>
    public CalculatedVar WithMultiplier(Func<PowerModel, Creature?, decimal> multiplierCalc)
    {
        if (_powerCalc != null)
            throw new InvalidOperationException($"Tried to set multiplier calc for power on CustomCalculatedDamageVar {Name} twice!");
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
            throw new InvalidOperationException($"Tried to set multiplier calc for CustomCalculatedDamageVar {Name} twice!");
        if (multiplierCalc.Target is AbstractModel) throw new InvalidOperationException("Multiplier calc must be static!");

        WithMultiplier((CardModel card, Creature? c) => multiplierCalc(card, c));
        _powerCalc = (pow, target) => multiplierCalc(pow, target);
        _relicCalc = (pow, target) => multiplierCalc(pow, target);
        _generalCalc = multiplierCalc;
        return this;
    }

    /// <inheritdoc />
    protected override DynamicVar GetBaseVar()
    {
        return _owner!.GetDynamicVar($"{Name}Base");
    }

    /// <inheritdoc />
    protected override DynamicVar GetExtraVar()
    {
        return _owner!.GetDynamicVar($"{Name}Extra");
    }

    /// <inheritdoc />
    protected override decimal GetBaseValueForIConvertible() => CalculateCustom(null);

    /// <inheritdoc />
    public override string ToString() => CalculateCustom(null).ToString(CultureInfo.InvariantCulture);
}