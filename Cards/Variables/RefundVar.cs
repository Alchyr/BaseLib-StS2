using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace BaseLib.Cards.Variables;

public class RefundVar : DynamicVar
{
    public const string Key = "Refund";

    public RefundVar(decimal refundAmount) : base(Key, refundAmount)
    {
        this.WithTooltip();
    }

    /*public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        PreviewValue = IntValue;
    }*/
}