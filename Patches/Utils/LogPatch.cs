using System.Text;
using System.Text.RegularExpressions;
using BaseLib.BaseLibScenes;
using BaseLib.Config;
using BaseLib.ConsoleCommands;
using Godot;
using Godot.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Patches.Utils;

public partial class LogListener : Godot.Logger
{
    [GeneratedRegex(@"^(?:\[(?<level>VERYDEBUG|LOAD|DEBUG|INFO|WARN|ERROR)\]|(?<level>VERYDEBUG|LOAD|DEBUG|INFO|WARN(?:ING)?|ERROR)\b:?)\s*(?<msg>.*)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LogPrefixRegex { get; }

    public override void _LogMessage(string message, bool error)
    {
        // The input to this is messy.
        // Most messages have a log level prepended already, but some don't, including some errors. Additionally,
        // some warnings begin with "WARNING:" but no actual tag like [WARN].
        var match = LogPrefixRegex.Match(message);

        string level;
        string cleanMsg;

        // Always tag as error if error == true, otherwise follow the tag if any, or fall back to INFO.
        if (error)
        {
            level = "ERROR";
            cleanMsg = match.Success ? match.Groups["msg"].Value : message;
        }
        else if (match.Success)
        {
            level = match.Groups["level"].Value.ToUpperInvariant();
            if (level == "WARNING") level = "WARN";
            cleanMsg = match.Groups["msg"].Value;
        }
        else
        {
            level = "INFO";
            cleanMsg = message;
        }

        var formatted = $"[{level}] {cleanMsg}";
        NLogWindow.AddLog(formatted);

        // This runs on whatever (possibly background) thread emitted the log. Only marshal
        // to the main thread when the auto-open feature is actually enabled, so the common
        // case never allocates a Callable or touches Godot off-thread on every error line.
        if (level == "ERROR" && BaseLibConfig.OpenLogWindowOnError)
            Callable.From(NLogWindow.OpenOnErr).CallDeferred();
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
        Array<ScriptBacktrace> scriptBacktraces)
    {
        var errorName = ((ErrorType)errorType).ToString();
        StringBuilder msg = new StringBuilder().Append($"[ERROR] Error occurred [{errorName}]: {rationale}\n{code}\n{file}:{line} @ {function}()\n");
        // Defensive: this callback can fire on any thread; never let a hiccup formatting the
        // backtrace throw out of the logger (which would itself be logged as another error).
        try
        {
            foreach (var backtrace in scriptBacktraces)
            {
                if (backtrace.IsEmpty()) continue;
                msg.Append($"{backtrace.Format()}");
            }
        }
        catch
        {
            // ignored
        }

        NLogWindow.AddLog(msg.ToString());

        if (BaseLibConfig.OpenLogWindowOnError)
            Callable.From(NLogWindow.OpenOnErr).CallDeferred();
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
class NMainMenuReadyOpenLogWindowPatch
{
    private static bool _hasOpenedOnStartup;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_hasOpenedOnStartup || !BaseLibConfig.OpenLogWindowOnStartup) return;

        _hasOpenedOnStartup = true;
        if (!NLogWindow.IsOpen)
            OpenLogWindow.OpenWindow(stealFocus: false);
    }
}
