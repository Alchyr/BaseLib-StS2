using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Helpers;

namespace BaseLib.Config.UI;

public partial class ModConfigControl : Control
{
    private string _title;
    private Control _content;
    private Control _parentScreen;
    private NButton? _backButton;

    public ModConfigControl(string title, Control content, Control parentScreen)
    {
        this._title = title;
        this._content = content;
        this._parentScreen = parentScreen;
    }

    public override void _Ready()
    {
        this.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        
        // Setup background
        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        
        // Setup title
        var titleLabel = new MegaLabel();
        titleLabel.Text = _title;
        titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.Position += new Vector2(0, 50);
        AddChild(titleLabel);
        
        // Setup Back Button
        try {
            _backButton = SceneHelper.Instantiate<NButton>("ui/button");
        } catch {
            _backButton = new NButton();
        }
        
        AddChild(_backButton);
        _backButton.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _backButton.Position += new Vector2(50, -100);
        
        var backLabel = _backButton.GetNodeOrNull<MegaLabel>("Visuals/Label");
        if (backLabel == null) {
            backLabel = new MegaLabel();
            backLabel.Name = "Label";
            _backButton.AddChild(backLabel);
        }
        backLabel.Text = "Back";
        
        _backButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => {
            OnBack();
        }));

        // Add content
        if (_content.GetParent() != null)
        {
            _content.GetParent().RemoveChild(_content);
        }
        AddChild(_content);
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
    }

    private void OnBack()
    {
        _parentScreen.Visible = true;
        this.QueueFree();
    }
}
