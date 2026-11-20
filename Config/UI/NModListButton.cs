using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

public partial class NModListButton : NButton
{
    private Label _label;
    private Panel _backgroundPanel;
    private StyleBoxFlat _styleBox;
    private Tween? _stateTween;

    private bool _isButtonDown;
    private bool IsSelectedMod { get; set; }
    public string ModName { get; private set; }

    private readonly Color _textNormal = new(0.7f, 0.7f, 0.7f);
    private readonly Color _textHover = Colors.White;
    private readonly Color _textActive = StsColors.gold;

    private readonly Color _bgNormal = new(0f, 0f, 0f, 0.2f);
    private readonly Color _bgHover = new(0.15f, 0.15f, 0.15f, 0.5f);
    private readonly Color _bgPressed = new(0.2f, 0.2f, 0.2f, 0.7f);

    // NButton has _hotkeyIcon, but it's fairly tightly coupled to having one hotkey per button; we need multiple
    // buttons that share a hotkey, one at a time, so we create our own instance.
    private NHotkeyIcon _customHotkeyIcon;
    private readonly StringName _targetHotkey = MegaInput.cancel;
    private bool _isHotkeyIconVisible = false;

    public NModListButton(string modName)
    {
        ModName = modName;
        Name = $"ModButton_{modName}";
        CustomMinimumSize = new Vector2(0f, 66f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        FocusMode = FocusModeEnum.All;

        _styleBox = new StyleBoxFlat {
            BgColor = _bgNormal,
            CornerRadiusTopLeft = 8, CornerRadiusBottomLeft = 8,
            CornerRadiusTopRight = 8, CornerRadiusBottomRight = 8,
            BorderColor = StsColors.gold,
            BorderWidthLeft = 0
        };

        _backgroundPanel = new Panel {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _backgroundPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        _backgroundPanel.AddThemeStyleboxOverride("panel", _styleBox);
        AddChild(_backgroundPanel);

        _label = new MegaLabel
        {
            Text = modName,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            AutoSizeEnabled = true,
            MinFontSize = 14,
            MaxFontSize = 24
        };

        _label.AddThemeFontOverride("font",
            PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_glyph_space_one.tres"));
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.AddThemeColorOverride("font_color", _textNormal);
        _label.AddThemeConstantOverride("shadow_outline_size", 2);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));

        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        _label.OffsetLeft = 24f;
        _label.OffsetRight = -16f;

        AddChild(_label);

        const float iconSize = 48;
        var iconScene = PreloadManager.Cache.GetAsset<PackedScene>(SceneHelper.GetScenePath("ui/hotkey_icon"));

        _customHotkeyIcon = iconScene.Instantiate<NHotkeyIcon>();
        _customHotkeyIcon.CustomMinimumSize = Vector2.One * iconSize;
        _customHotkeyIcon.Size = Vector2.One * iconSize;
        _customHotkeyIcon.Visible = false;
        _customHotkeyIcon.SetAnchorsPreset(LayoutPreset.CenterRight);
        _customHotkeyIcon.Position = new Vector2(-iconSize, -iconSize / 2);

        AddChild(_customHotkeyIcon);
    }

    public override void _Ready()
    {
        ConnectSignals();

        if (NControllerManager.Instance != null)
        {
            NControllerManager.Instance.Connect(NControllerManager.SignalName.MouseDetected,
                Callable.From(RefreshIconVisibility));
            NControllerManager.Instance.Connect(NControllerManager.SignalName.ControllerDetected,
                Callable.From(RefreshIconVisibility));
            NControllerManager.Instance.Connect(NControllerManager.SignalName.ControllerTypeChanged,
                Callable.From(RefreshIconVisibility));
        }

        RefreshIconVisibility();
        UpdateVisualState(instant: true);
    }

    public void SetHotkeyIconVisible(bool enabled)
    {
        _isHotkeyIconVisible = enabled;
        RefreshIconVisibility();
    }

    private void RefreshIconVisibility()
    {
        var isDirectionNavActive = NControllerManager.Instance?.IsUsingDirectionalNavigation ?? false;
        _customHotkeyIcon.Visible = _isHotkeyIconVisible && isDirectionNavActive;

        if (_customHotkeyIcon.Visible)
            _customHotkeyIcon.UpdateInput(_targetHotkey);
    }

    public void SetActiveState(bool isActive)
    {
        IsSelectedMod = isActive;
        UpdateVisualState();
    }

    protected override void OnFocus() { base.OnFocus(); UpdateVisualState(); }
    protected override void OnUnfocus() { base.OnUnfocus(); UpdateVisualState(); }

    protected override void OnPress() { base.OnPress(); _isButtonDown = true; UpdateVisualState(); }
    protected override void OnRelease() { base.OnRelease(); _isButtonDown = false; UpdateVisualState(); }

    private void UpdateVisualState(bool instant = false)
    {
        Color targetBg;
        Color targetText;
        int targetBorderWidth;

        if (_isButtonDown)
        {
            targetBg = _bgPressed;
            targetText = _textHover;
            targetBorderWidth = 0;
        }
        else if (IsFocused)
        {
            targetBg = _bgHover;
            targetText = _textHover;
            targetBorderWidth = IsSelectedMod ? 4 : 0;
        }
        else if (IsSelectedMod)
        {
            targetBg = _bgHover;
            targetText = _textActive;
            targetBorderWidth = 4;
        }
        else
        {
            targetBg = _bgNormal;
            targetText = _textNormal;
            targetBorderWidth = 0;
        }

        if (instant)
        {
            _styleBox.BgColor = targetBg;
            _styleBox.BorderWidthLeft = targetBorderWidth;
            _label.AddThemeColorOverride("font_color", targetText);

            return;
        }

        _stateTween?.Kill();
        _stateTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        _stateTween.TweenProperty(_styleBox, "bg_color", targetBg, 0.1f);
        _stateTween.TweenProperty(_label, "theme_override_colors/font_color", targetText, 0.15f);
        _stateTween.TweenProperty(_styleBox, "border_width_left", targetBorderWidth, 0.2f);
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (NControllerManager.Instance == null) return;
        NControllerManager.Instance.Disconnect(NControllerManager.SignalName.MouseDetected,
            Callable.From(RefreshIconVisibility));
        NControllerManager.Instance.Disconnect(NControllerManager.SignalName.ControllerDetected,
            Callable.From(RefreshIconVisibility));
        NControllerManager.Instance.Disconnect(NControllerManager.SignalName.ControllerTypeChanged,
            Callable.From(RefreshIconVisibility));
    }
}