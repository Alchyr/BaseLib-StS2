using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RunManager))]
internal static class RunManagerPatches
{
    private static readonly List<Type> customMessageTypes = [..ReflectionHelper.GetSubtypesInMods<CustomMessage>()];

    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    private static void InitializeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            if (messageType.CreateInstance() is not CustomMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Initialize");
                continue;
            }
            dummyMessage.Initialize(__instance.RunLocationTargetedBuffer);
        }
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    private static void DisposeCustomMessageHandlers(RunManager __instance)
    {
        foreach (var messageType in customMessageTypes)
        {
            if (messageType.CreateInstance() is not CustomMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {messageType.GetType()} from {messageType.Assembly} failed during Dispose");
                continue;
            }
            dummyMessage.Dispose(__instance.RunLocationTargetedBuffer);
        }
    }
}
