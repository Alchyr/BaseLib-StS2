using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// Use this on a card that targets enemies, but applies a power to yourself, so that the preview value is calculated with the correct target.
/// </summary>
public class SelfPowerVar<T> : PowerVar<T> where T : PowerModel
{
    public SelfPowerVar(decimal powerAmount) : base(typeof(T).Name, powerAmount) { }

    public SelfPowerVar(string name, decimal powerAmount) : base(name, powerAmount) { }

    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        base.UpdateCardPreview(card, previewMode, card.Owner.Creature, runGlobalHooks);
    }
}
