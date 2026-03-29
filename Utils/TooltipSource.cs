using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

public class TooltipSource
{
    private readonly Func<CardModel, IHoverTip> _makeTip;
    
    public TooltipSource(Func<CardModel, IHoverTip> tip)
    {
        _makeTip = tip;
    }

    public IHoverTip Tip(CardModel card) => _makeTip(card);

    public static implicit operator TooltipSource(Type t)
    {
        if (t.IsAssignableTo(typeof(PowerModel)))
        {
            return new((card)=>HoverTipFactory.FromPower(ModelDb.GetById<PowerModel>(ModelDb.GetId(t))));
        }
        if (t.IsAssignableTo(typeof(CardModel)))
        {
            return new((card)=>HoverTipFactory.FromCard(ModelDb.GetById<CardModel>(ModelDb.GetId(t))));
        }
        if (t.IsAssignableTo(typeof(PotionModel)))
        {
            return new((card)=>HoverTipFactory.FromPotion(ModelDb.GetById<PotionModel>(ModelDb.GetId(t))));
        }
        throw new Exception($"Unable to generate hovertip from type {t}");
    }
    public static implicit operator TooltipSource(CardKeyword keyword) => new((card)=>HoverTipFactory.FromKeyword(keyword));
    public static implicit operator TooltipSource(StaticHoverTip staticTip) => new(card => HoverTipFactory.Static(staticTip));
}
