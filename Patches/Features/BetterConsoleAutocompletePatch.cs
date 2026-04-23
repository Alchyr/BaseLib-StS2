using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace BaseLib.Patches.Features;

/// <summary>
/// Improves dev console autocomplete to use <see cref="string.Contains(string, StringComparison)"/>
/// instead of <see cref="string.StartsWith(string, StringComparison)"/> when no custom predicate is set,
/// allowing partial matches anywhere in the candidate name (e.g. "STRI" matches "MYMOD-STRIKE_MYCHARACTER").
/// Results are sorted so direct prefix matches appear first, followed by tag-suffix matches, then all others.
/// </summary>
[HarmonyPatch(typeof(AbstractConsoleCmd), nameof(AbstractConsoleCmd.CompleteArgument))]
public static class BetterConsoleAutocompletePatch
{
    /// <summary>Replaces the default StartsWith predicate with Contains.</summary>
    [HarmonyPrefix]
    public static void UseContainsMatching(ref Func<string, string, bool>? matchPredicate)
    {
        if (matchPredicate != null) return;
        matchPredicate = (candidate, partial) =>
            candidate.Contains(partial, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts candidates so direct prefix matches come first, mod-tag-suffix matches second,
    /// and all other contains-matches last.
    /// </summary>
    [HarmonyPostfix]
    public static void SortByRelevance(ref CompletionResult __result, string partialArg)
    {
        
        if (string.IsNullOrWhiteSpace(partialArg)) return;
        __result.Candidates = __result.Candidates
            .OrderBy(e =>
            {
                if (e.StartsWith(partialArg, StringComparison.OrdinalIgnoreCase)) return 0;
                var afterTag = e.Contains('-') ? e.Split('-', 2)[1] : e;
                return afterTag.StartsWith(partialArg, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            })
            .ThenBy(e => e)
            .ToList();
    }
}

/// <summary>
/// Suppresses the <see cref="GD.PushError"/> call in <see cref="NDevConsole.UpdateGhostText"/>
/// that fires when <c>CommonPrefix</c> does not start with the current input text.
/// This is no longer an error with contains-based autocomplete.
/// </summary>
[HarmonyPatch(typeof(NDevConsole), nameof(NDevConsole.UpdateGhostText))]
public static class UpdateGhostTextPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> RemovePushError(IEnumerable<CodeInstruction> instructions)
    {
        var toString = AccessTools.Method(typeof(DefaultInterpolatedStringHandler), nameof(DefaultInterpolatedStringHandler.ToStringAndClear));

        return (List<CodeInstruction>)new InstructionPatcher(instructions)
            .Match(new InstructionMatcher().call(toString))
            .Replace(new CodeInstruction(OpCodes.Nop))
            .Step()
            .Replace(new CodeInstruction(OpCodes.Nop)); 
    }
}