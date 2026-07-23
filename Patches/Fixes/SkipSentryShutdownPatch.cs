using BaseLib.Extensions;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;

namespace BaseLib.Patches.Fixes;

[HarmonyPatch(typeof(SentryService), nameof(SentryService.Shutdown))]
static class SkipSentryShutdownPatch
{
    private static readonly SemanticVersion MinVersion = new(0, 107, 0);
    
    [HarmonyTranspiler]
    static List<CodeInstruction> ReplaceShutdown(IEnumerable<CodeInstruction> code)
    {
        if (BetaMainCompatibility.Version.LessThan(MinVersion))
        {
            BaseLibMain.Logger.Info($"Skipping SentryService shutdown patch; version [{BetaMainCompatibility.Version}] " +
                                    $"less than minimum version [{MinVersion}].");
        }
        
        var patcher = new InstructionPatcher(code);
        var matched = patcher.TryMatch(
            new CallMatcher(typeof(GodotObject)
                .Method(nameof(GodotObject.Call), [typeof(StringName), typeof(Variant[])])));

        if (matched == null)
        {
            BaseLibMain.Logger.Info("Skipping SentryService shutdown patch; no match found.");
            return patcher;
        }
        
        return matched.ReplaceLastMatch([CodeInstruction.Call(typeof(SkipSentryShutdownPatch), nameof(SkipShutdown))]);
    }

    private static Variant SkipShutdown(GodotObject objInstance, StringName methodName, params Variant[] ignoreArgs)
    {
        BaseLibMain.Logger.Info("Skipping SentryService shutdown.");
        return default;
    }
}