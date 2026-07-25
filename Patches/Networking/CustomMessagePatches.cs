using System.Reflection;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Networking;

[HarmonyPatch(typeof(RunManager))]
internal static class RunManagerPatches
{
    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    private static void InitializeCustomMessageHandlers(RunManager __instance)
    {
        CustomMessageWrapper.Register(__instance.NetService);
        CustomTargetedMessageWrapper.Register(__instance.RunLocationTargetedBuffer);
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    private static void DisposeCustomMessageHandlers(RunManager __instance)
    {
        CustomMessageWrapper.Unregister(__instance.NetService);
        CustomTargetedMessageWrapper.Unregister(__instance.RunLocationTargetedBuffer);
    }
}

[HarmonyPatch]
static class AdjustCustomMessageKeys
{
    // v0.107.1+ (re)builds the network message-type cache in MessageTypes.Initialize().
    // Older builds did it in the type's static constructor instead. Resolve whichever this
    // game version actually has so the wrapper-id fixup still runs after the cache is built;
    // if neither exists, skip the patch cleanly instead of throwing "Undefined target method".
    private static MethodBase? _target;

    static bool Prepare()
    {
        _target ??= AccessTools.DeclaredMethod(typeof(MessageTypes), nameof(MessageTypes.Initialize))
                    ?? (MethodBase?)typeof(MessageTypes).TypeInitializer;

        if (_target is null)
            BaseLibMain.Logger.Warn(
                "MessageTypes.Initialize (and its static constructor) could not be found; custom " +
                "network message wrappers will not be registered for this game version.");

        return _target is not null;
    }

    static MethodBase TargetMethod() => _target!;

    [HarmonyPostfix]
    static void Fuckery()
    {
        BaseLibMain.Logger.Info("Adjusting keys of custom message wrappers.");
        var cache = MessageTypes._cache;

        cache!._idToType.Remove(typeof(CustomMessageWrapper));
        cache._idToType.Remove(typeof(CustomTargetedMessageWrapper));
        
        for (int index = 0; index < cache._idToType.Count; ++index)
        {
            var type = cache._idToType[index];
            cache._typeToId[type] = index;
        }

        byte b = 128;
        for (; b < 255; ++b)
        {
            if (!cache.TryGetTypeFromId(b, out _))
                break;
        }
        CustomMessageWrapper.WrapperMessageId = b;
        cache._typeToId[typeof(CustomMessageWrapper)] = b;
        ++b;
        for (; b < 255; ++b)
        {
            if (!cache.TryGetTypeFromId(b, out _))
                break;
        }
        CustomTargetedMessageWrapper.WrapperMessageId = b;
        cache._typeToId[typeof(CustomTargetedMessageWrapper)] = b;
        BaseLibMain.Logger.Info($"Using IDs {CustomMessageWrapper.WrapperMessageId} and {CustomTargetedMessageWrapper.WrapperMessageId} for custom message wrappers.");
    }
}

[HarmonyPatch(typeof(MessageTypes), nameof(MessageTypes.TryGetMessageType))]
static class GetCustomTypes
{
    [HarmonyPrefix]
    static bool CustomWrapperTypes(int id, ref Type? type, ref bool __result)
    {
        if (id == CustomMessageWrapper.WrapperMessageId)
        {
            type = typeof(CustomMessageWrapper);
            __result = true;
            return false;
        }
        else if (id == CustomTargetedMessageWrapper.WrapperMessageId)
        {
            type = typeof(CustomTargetedMessageWrapper);
            __result = true;
            return false;
        }
        
        return true;
    }
}