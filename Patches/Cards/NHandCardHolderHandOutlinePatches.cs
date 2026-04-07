using BaseLib.Hooks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace BaseLib.Patches.Cards;

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.UpdateCard))]
internal static class NHandCardHolderUpdateCardHandOutlinePatch
{
    [HarmonyPostfix]
    public static void Postfix(NHandCardHolder __instance)
    {
        if (!ModCardHandOutlinePatchHelper.TryGetRule(__instance, out var model, out var rule))
            return;

        ModCardHandOutlinePatchHelper.ApplyHighlight(__instance, model, rule);
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.Flash))]
internal static class NHandCardHolderFlashHandOutlinePatch
{
    [HarmonyPostfix]
    public static void Postfix(NHandCardHolder __instance)
    {
        if (!ModCardHandOutlinePatchHelper.TryGetRule(__instance, out _, out var rule))
            return;

        ModCardHandOutlinePatchHelper.ApplyFlash(__instance, rule);
    }
}