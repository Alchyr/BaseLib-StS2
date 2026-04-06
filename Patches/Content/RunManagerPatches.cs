using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RunManager))]
public static class RunManagerPatches
{

    internal static List<Type> customMessageTypes = [..ReflectionHelper.GetSubtypesInMods<CustomMessage>()];

    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    public static void InitializeCustomMessageHandlers(RunManager __instance)
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
    public static void UnregisterCustomRewardHandlers(RunManager __instance)
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
    // [HarmonyPatch(nameof(RunManager.InitializeShared))]
    // [HarmonyPostfix]
    public static void RegisterCustomMessageHandlers(RunManager __instance)
    {
        var runMessageBuffer = __instance.RunLocationTargetedBuffer;
        foreach (var type in customMessageTypes)
        {

            // var registerMethod = AccessTools.Method(typeof(RunLocationTargetedMessageBuffer), nameof(RunLocationTargetedMessageBuffer.RegisterMessageHandler));
            // var typedMethod = registerMethod.MakeGenericMethod(type);
            // typedMethod.Invoke(__instance, [AccessTools.FieldRef(type, nameof(CustomMessage.MessageHandler))]);
        }
    }
}
