using BaseLib.BaseLibScenes;
using BaseLib.Commands;
using BaseLib.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Patches.Utils;

public partial class LogListener : Godot.Logger
{
    public override void _LogMessage(string message, bool error)
    {
        NLogWindow.AddLog(message);
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
        OpenLogWindow.OpenWindow(stealFocus: false);
    }
}
