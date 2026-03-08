using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Helpers;
using System;

namespace BaseLib.Config.UI;

public static class UIElements
{
    public static MegaLabel CreateLabel(Control parent, string text, Vector2 position)
    {
        var label = new MegaLabel();
        label.Text = text;
        label.Position = position;
        parent.AddChild(label);
        return label;
    }

    public static NTickbox CreateTickbox(Control parent, string text, Vector2 position, bool isTicked, Action<bool> onToggle)
    {
        // Try to instantiate from common paths. This is a guess.
        NTickbox tickbox;
        try {
            tickbox = SceneHelper.Instantiate<NTickbox>("ui/tickbox");
        } catch {
            tickbox = new NTickbox(); // Fallback if scene not found
        }
        
        tickbox.Position = position;
        tickbox.IsTicked = isTicked;
        
        var label = new MegaLabel();
        label.Text = text;
        label.Position = new Vector2(80, 0);
        tickbox.AddChild(label);

        tickbox.Connect(NTickbox.SignalName.Toggled, Callable.From<NTickbox>(t => onToggle(t.IsTicked)));
        
        parent.AddChild(tickbox);
        return tickbox;
    }

    public static NButton CreateButton(Control parent, string text, Vector2 position, Action onClick)
    {
        // Try to instantiate from common paths.
        NButton button;
        try {
            button = SceneHelper.Instantiate<NButton>("ui/button");
        } catch {
            button = new NButton(); // Fallback
        }
        
        button.Position = position;
        var label = button.GetNodeOrNull<MegaLabel>("Visuals/Label");
        if (label == null) {
            label = new MegaLabel();
            label.Name = "Label";
            button.AddChild(label);
        }
        label.Text = text;

        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => onClick()));
        
        parent.AddChild(button);
        return button;
    }
}
