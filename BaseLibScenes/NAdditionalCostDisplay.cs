using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.BaseLibScenes;

public partial class NAdditionalCostDisplay : Control
{
    private const string DefaultFontPath = "res://themes/kreon_bold_shared.tres";
    
    private static readonly StringName ShadowOffsetX = "shadow_offset_x"; //todo - clean up reuse
    private static readonly StringName ShadowOffsetY = "shadow_offset_y";

    protected Color _mainColor;
    
    protected TextureRect _icon;
    protected TextureRect _unplayableIcon;
    protected MegaLabel _label;
    
    //Resource cannot be cached as unlike combat UI, card UI lifetime is longer than the lifetime of a resource instance.
    public NAdditionalCostDisplay(string id, string imagePath, Color mainOutlineColor)
    {
        Name = $"AdditionalCostDisplay{id}";
        Size = new Vector2(56, 56);
        PivotOffset = Size * 0.5f;
        MouseFilter = MouseFilterEnum.Ignore;

        _mainColor = mainOutlineColor;

        _icon = new()
        {
            Texture = ResourceLoader.Load<Texture2D>(imagePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        AddChild(_icon);

        _unplayableIcon = new()
        {
            Name = $"Unplayable{id}Icon",
            Texture = PreloadManager.Cache.GetTexture2D(
                "res://images/atlases/ui_atlas.sprites/card/card_unplayable_icon.tres"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Scale = new Vector2(0.75f, 0.75f),
            Visible = false
        };
        _unplayableIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _icon.AddChild(_unplayableIcon);
        
        _unplayableIcon.PivotOffset = Size * 0.5f;
        
        _label = new()
        {
            Name = "CostLabel",
            MinFontSize = 16,
            MaxFontSize = 22,
            Text = "X",
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoSizeEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        
        var font = PreloadManager.Cache.GetAsset<Font>(DefaultFontPath);
        _label.AddThemeFontOverride(ThemeConstants.Label.Font, font);
        _label.AddThemeFontSizeOverride(ThemeConstants.Label.FontSize, 22);
        _label.AddThemeConstantOverride(ShadowOffsetX, 2);
        _label.AddThemeConstantOverride(ShadowOffsetY, 2);
        _label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 12);
        _label.AddThemeConstantOverride("shadow_outline_size", 12);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontColor, StsColors.cream);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, _mainColor);
        _label.AddThemeColorOverride(ThemeConstants.Label.FontShadowColor, new Color(0, 0, 0, 0.18f));

        _icon.AddChild(_label);

        Visible = false;
    }

    public void UpdateCostVisual(NCard card, ICustomResourceCost cost, PileType pileType)
    {
        var model = card.Model;
        if (model == null)
        {
            Visible = false;
            return;
        }
        
        if (card.Visibility != ModelVisibility.Visible)
        {
            _label.SetTextAutoSize(string.Empty);
            Visible = false;
            _label.AddThemeColorOverride(ThemeConstants.Label.FontColor, StsColors.cream);
            _label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, _mainColor);
        }
        else
        {
            Visible = true;
            var color1 = StsColors.cream;
            var color2 = _mainColor;
            if (cost.CostsX)
            {
                _label.SetTextAutoSize("X");
            }
            else
            {
                _label.SetTextAutoSize(cost.GetWithModifiers(CostModifiers.All).ToString());
            }
            
            if (!cost.CostsX && cost.WasJustUpgraded)
            {
                color1 = StsColors.green;
                color2 = StsColors.energyGreenOutline;
            }
            else if (pileType == PileType.Hand)
            {
                var costColor = cost.GetCostColor(model, model.CombatState);
                color1 = NCard.GetCostTextColorInHand(costColor, card._pretendCardCanBePlayed, color1);
                color2 = NCard.GetCostOutlineColorInHand(costColor, card._pretendCardCanBePlayed, color2);
            }
            _label.AddThemeColorOverride(ThemeConstants.Label.FontColor, color1);
            _label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, color2);

            if (pileType == PileType.Hand && !model.CanPlay(out var reason, out _))
                _unplayableIcon.Visible = !reason.HasResourceCostReason();
            else
                _unplayableIcon.Visible = false;
        }
    }
}