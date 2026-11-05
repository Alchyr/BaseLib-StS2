using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class ModelExtensions
{
    public static string LocKey(this AbstractModel model, string subKey)
    {
        return $"{model.Id.Entry}.{subKey}";
    }

    public static DynamicVarSet? GetDynamicVarSet(this AbstractModel model)
    {
        return model switch
        {
            CardModel card => card.DynamicVars,
            RelicModel relic => relic.DynamicVars,
            PowerModel power => power.DynamicVars,
            PotionModel potion => potion.DynamicVars,
            EnchantmentModel enchantment => enchantment.DynamicVars,
            EventModel ev => ev.DynamicVars,
            _ => null
        };
    }

    public static DynamicVar GetDynamicVar(this AbstractModel model, string varKey)
    {
        return model.GetDynamicVarSet()?[varKey] ?? throw new Exception($"{model.GetType().Name} does not have dynamic vars (or is unsupported by GetDynamicVar)");
    }
}