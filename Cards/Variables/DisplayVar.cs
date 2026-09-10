using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

public class DisplayVar<T> : DynamicVar where T : class
{
    private T? _tOwner;
    private readonly Func<T, string> _displayText;
    
    public DisplayVar(string name, Func<T, string> displayText) : base(name, 0)
    {
        _displayText = displayText;
    }

    public override void SetOwner(AbstractModel owner)
    {
        base.SetOwner(owner);
        _tOwner = owner as T;
    }
    
    protected override decimal GetBaseValueForIConvertible()
    {
        return NumericValue;
    }

    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        PreviewValue = NumericValue;
    }

    public decimal NumericValue => _tOwner != null && decimal.TryParse(_displayText(_tOwner), out decimal result) ? result : BaseValue;
    
    public override string ToString()
    {
        return _tOwner == null ? $"Owner of DisplayVar '' is wrong type [{_owner?.GetType()}]" : _displayText(_tOwner);
    }
}