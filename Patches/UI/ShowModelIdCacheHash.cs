using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NDebugInfoLabelManager), nameof(NDebugInfoLabelManager.UpdateText))]
class ShowModelIdCacheHash
{
    [HarmonyPostfix]
    static void AdjustModdedLabel(NDebugInfoLabelManager __instance)
    {
        var text = __instance._moddedWarning.Text;
        __instance._moddedWarning.SetTextAutoSize($"{text}\nHASH [{ModelIdSerializationCache.Hash}]");
    }
}