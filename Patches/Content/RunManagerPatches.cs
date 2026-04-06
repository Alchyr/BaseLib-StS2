using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RunManager))]
public static class RunManagerPatches
{
    internal static List<Type> customMessageTypes = new Type[] [..ReflectionHelper.GetSubtypesInMods<CustomMessage>()];
    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    public static void RegisterCustomMessageHandlers(RunManager __instance)
    {
        var runMessageBuffer = __instance.RunLocationTargetedBuffer;
        foreach (var type in customMessageTypes)
        {

        }
    }
}
