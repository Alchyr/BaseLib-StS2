using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

/// <summary>
/// Patch for getting custom tooltips for keywords from HoverTipFactory.
/// </summary>
class CustomTooltips
{
    [HarmonyPatch(typeof(HoverTipFactory), nameof(HoverTipFactory.FromKeyword))]
    static class DynamicKeywordTips
    {
        [HarmonyPrefix]
        public static bool CustomKeyword(CardKeyword keyword, ref IHoverTip __result)
        {
            if (CustomKeywords.KeywordIDs.TryGetValue((int) keyword, out var info))
            {
                //HoverTip with model attached or add dictionary manually
                if (info.RichKeyword)
                {
                    LocString description = keyword.GetDescription();
                    description.Add("energyPrefix", GetEnergyPrefix(info));
                    __result = new HoverTip(keyword.GetTitle(), description);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolve the keyword's energy icon from the pool it declared, the way the game resolves it for cards and
        /// powers. An empty prefix makes EnergyIconsFormatter fall back to the character being played, which does not
        /// exist outside of a run, so the tooltip would show the colorless icon in the card library.
        /// </summary>
        private static string GetEnergyPrefix(CustomKeywords.KeywordInfo info)
        {
            if (info.PoolType == null) return "";

            AbstractModel? pool = ModelDb.GetByIdOrNull<AbstractModel>(ModelDb.GetId(info.PoolType));
            return pool is IPoolModel ? EnergyIconHelper.GetPrefix(pool) : "";
        }
    }
}