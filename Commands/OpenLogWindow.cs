using BaseLib.BaseLibScenes;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

namespace BaseLib.Commands;

public class OpenLogWindow : AbstractConsoleCmd
{
    public override string CmdName => "showlog";
    public override string Args => "";
    public override string Description => "Open log display window";
    public override bool IsNetworked => false;
    
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        OpenWindow();
        return new CmdResult(true, "Opened log window.");
    }

    public static void OpenWindow()
    {
        var instance = NGame.Instance;
        if (instance == null) return;
        
        Window window = instance.GetWindow();
        window.GuiEmbedSubwindows = false;
        
        var scene = PreloadManager.Cache.GetScene("res://BaseLib/scenes/LogWindow.tscn").Instantiate<NLogWindow>();
        scene.Size = DisplayServer.ScreenGetSize() * 2 / 3;
        window.AddChildSafely(scene);
    }
}