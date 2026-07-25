using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigButton : NSettingsButton
{
    private Action? _onPressedAction;
    private new TextureRect _image;
    public static readonly string DefaultColor = "#3b7a83";

    public NConfigButton()
    {
        CustomMinimumSize = new Vector2(324, 64);
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        _image = new TextureRect
        {
            Name = "Image",
            CustomMinimumSize = new Vector2(64, 64),
            Texture = PreloadManager.Cache.GetAsset<Texture2D>("res://BaseLib/images/config/configbutton.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        _image.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_image);

        var label = new MegaLabel
        {
            Name = "Label",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoSizeEnabled = false
        };

        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<FontVariation>("res://themes/kreon_bold_glyph_space_two.tres"));
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.74f));
        label.AddThemeConstantOverride("outline_size", 12);
        label.AddThemeColorOverride("font_outline_color", new Color(0.29f, 0.14f, 0.14f));

        AddChild(label);

        var reticleScene = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/selection_reticle"));
        var reticle = reticleScene.Instantiate<NSelectionReticle>();
        reticle.Name = "SelectionReticle";
        reticle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        AddChild(reticle);
    }

    /// <summary>
    /// Sets the button's color using Godot's SelfModulate property.<br/>
    /// The overall color will be slightly darker than the color specified, since it's a modulation of the existing
    /// colors, that aren't fully white.
    /// </summary>
    /// <param name="color">The color to use for SelfModulate.</param>
    public void SetColor(Color color)
    {
        _image.SelfModulate = color;
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public void Initialize(string buttonText, Action onPressed)
    {
        SetColor(Color.FromHtml(DefaultColor));
        _onPressedAction = onPressed;

        var label = GetNodeOrNull<Label>("Label");
        if (label != null)
        {
            label.Text = buttonText;
        }

        _onPressedAction = onPressed;
        Connect(NClickableControl.SignalName.Released, Callable.From<NConfigButton>(OnReleased));
    }

    private void OnReleased(NConfigButton button)
    {
        _onPressedAction?.Invoke();
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        Disconnect(NClickableControl.SignalName.Released, Callable.From<NConfigButton>(OnReleased));
    }
}