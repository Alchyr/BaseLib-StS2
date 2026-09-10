using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace BaseLib.BaseLibScenes;

public partial class NAdditionalResourceDisplay : Control
{
    private static readonly StringName V = new("v");
    private static readonly StringName S = new("s");
    
    private static readonly StringName ShadowOffsetX = "shadow_offset_x";
    private static readonly StringName ShadowOffsetY = "shadow_offset_y";

    private const string DefaultFontPath = "res://themes/kreon_bold_glyph_space_two.tres";

    protected CustomResource _resource;
    protected Player? _player;

    /// <summary>
    /// Event that occurs when the amount to be displayed is updated, receiving the old and new amount.
    /// Intended to be used for visuals within the resource display.
    /// </summary>
    public event Action<int, int>? DisplayAmountChanged;
    
    /// <summary>
    /// Event that occurs when the current value displayed on the label is updated, receiving the new amount.
    /// Intended to be used for visuals within the resource display (for example, setting the label color).
    /// </summary>
    public event Action<int>? AfterLabelAmountChanged;

    protected Control _visuals;
    protected MegaRichTextLabel _label;
    
    protected bool _isListeningToCombatState;

    private int _currentCount; //The current actual quantity of the resource, the most recent value from UpdateResourceCount
    private float _lerpResourceCount;
    private float _lerpCountVelocity;
    private int _displayedResourceCount;
    
    protected HoverTip? _hoverTip;

    /// <summary>
    /// Creates a basic additional resource display using a single texture for its image and vfx,
    /// and some standard visual behaviors.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    public static NAdditionalResourceDisplay Create<T>(Player player, CustomResource resource, Texture2D singleTexture) where T : CustomResource, new()
    {
        var resourceDisplay = new NAdditionalResourceDisplay(player, resource);

        TextureRect texRect = new();
        texRect.Texture = singleTexture;
        texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        texRect.SetMouseFilter(MouseFilterEnum.Ignore);
        texRect.Material = ShaderUtils.GenerateHsv(1, 1, 1);
        
        resourceDisplay._visuals.AddChild(texRect);

        ParticleSystemBuilder particles = new ParticleSystemBuilder()
            .AddParticle(() => singleTexture)
            .GrowFade();

        var tweenHolder = new Holder<Tween>();
        resourceDisplay.DisplayAmountChanged += (oldAmount, newAmount) =>
        {
            var hsv = resourceDisplay._visuals.Material as ShaderMaterial;
            if (newAmount < oldAmount)
            {
                tweenHolder.Val?.Kill(); 
                hsv?.SetShaderParameter(V, 1f);
            }
            else if (newAmount > oldAmount)
            {
                tweenHolder.Val?.Kill();
                tweenHolder.Val = resourceDisplay.CreateTween();
                tweenHolder.Val.TweenMethod(Callable.From((float value) => hsv?.SetShaderParameter(V, value)),
                    2f, 1f, 0.2f);

                var child = particles.Build();
                resourceDisplay.AddChildSafely(child);
                resourceDisplay.MoveChildSafely(child, 0);
                child.Position = resourceDisplay.Size / 2f;
            }
        };

        resourceDisplay.AfterLabelAmountChanged += (newAmount) =>
        {
            if (resourceDisplay._visuals.Material is not ShaderMaterial hsv) return;
            if (newAmount == 0)
            {
                hsv.SetShaderParameter(S, 0.5f);
                hsv.SetShaderParameter(V, 0.85f);
            }
            else
            {
                hsv.SetShaderParameter(S, 1f);
                hsv.SetShaderParameter(V, 1f);
            }
        };
        
        return resourceDisplay;
    }


    /// <summary>
    /// Creates an additional resource display with no included custom visuals, only a label,
    /// and no special visual behavior.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    public NAdditionalResourceDisplay(Player player, CustomResource resource)
    {
        _player = player;
        _resource = resource;
        Name = $"AdditionalResourceDisplay{resource.Id}";
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        Size = new Vector2(88, 88);
        SetMouseFilter(MouseFilterEnum.Pass);

        _visuals = new Control()
        {
            Name = "Visuals"
        };
        _visuals.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _visuals.SetMouseFilter(MouseFilterEnum.Ignore);

        _label = new()
        {
            Name = "CountLabel",
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = _resource.GetTextForAmount(_resource.Amount),
            AutoSizeEnabled = false,
            ScrollActive = false,
            BbcodeEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        
        var font = PreloadManager.Cache.GetAsset<Font>(DefaultFontPath);
        _label.AddThemeFontOverride(ThemeConstants.Label.Font, font);
        _label.AddThemeFontOverride(ThemeConstants.RichTextLabel.NormalFont, font);
        _label.AddThemeFontSizeOverrideAll(36);
        _label.AddThemeConstantOverride(ShadowOffsetX, 5);
        _label.AddThemeConstantOverride(ShadowOffsetY, 5);
        _label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 14);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontColor, StsColors.cream);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, _resource.MainColor);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontShadowColor, new Color(0, 0, 0, 0.12f));
        
        AddChild(_visuals);
        AddChild(_label);

        Visible = false;
        ConnectResourceChangedSignal();
        RefreshVisibility();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _hoverTip = _resource.MakeTip();
        Connect(Control.SignalName.MouseEntered, Callable.From(OnHovered));
        Connect(Control.SignalName.MouseExited, Callable.From(OnUnhovered));
    }

    /// <inheritdoc />
    public override void _EnterTree()
    {
        base._EnterTree();
        ConnectResourceChangedSignal();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        base._ExitTree();
        if (_player == null || !_isListeningToCombatState)
            return;

        var playerCombatState = _player.PlayerCombatState;
        if (playerCombatState == null)
            return;

        _resource.AmountChanged -= OnResourceAmountChanged;
        _isListeningToCombatState = false;
    }

    private void ConnectResourceChangedSignal()
    {
        if (_player == null || _isListeningToCombatState)
            return;

        var playerCombatState = _player.PlayerCombatState;
        if (playerCombatState == null)
            return;

        _resource.AmountChanged += OnResourceAmountChanged;
        _isListeningToCombatState = true;
    }

    private void OnHovered()
    {
        if (_hoverTip == null) return;
        
        NHoverTipSet.CreateAndShow(this, _hoverTip)?.SetGlobalPosition(GlobalPosition + new Vector2(-34f, -300f));
    }

    private void OnUnhovered() => NHoverTipSet.Remove(this);

    private void OnResourceAmountChanged(int oldAmount, int newAmount)
    {
        UpdateResourceCount(oldAmount, newAmount);
        RefreshVisibility();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_player == null)
            return;
        
        _lerpResourceCount = MathHelper.SmoothDamp(_lerpResourceCount, _currentCount, ref _lerpCountVelocity,
            0.1f, (float)delta);
        SetResourceCountText(Mathf.RoundToInt(_lerpResourceCount));
    }

    private void UpdateResourceCount(int oldAmount, int newAmount)
    {
        _currentCount = newAmount;
        DisplayAmountChanged?.Invoke(oldAmount, newAmount);
        if (newAmount < oldAmount)
        {
            _lerpResourceCount = newAmount;
            SetResourceCountText(newAmount);
        }
    }

    private void SetResourceCountText(int amt)
    {
        if (_displayedResourceCount == amt)
            return;
        _displayedResourceCount = amt;
        _label.AddThemeColorOverride(ThemeConstants.Label.FontColor, amt == 0 ? StsColors.red : StsColors.cream);
        _label.Text = _resource.GetTextForAmount(amt);
        AfterLabelAmountChanged?.Invoke(amt);
    }

    public void RefreshVisibility()
    {
        if (_player == null)
        {
            Visible = false;
        }
        else if (!Visible)
        {
            Visible = Visible || _resource.ShouldShowDisplay();
            if (Visible) BaseLibMain.Logger.Info("Making additional resource display visible");
        }
    }
}