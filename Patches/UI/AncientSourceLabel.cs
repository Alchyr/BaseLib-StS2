using BaseLib.Config;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace BaseLib.Patches.UI;

/// <summary>
/// Ancient events show their name as a banner instead of a hover tip. For modded ancients the source
/// mod is added as a small line under the banner's epithet, but only after the dramatic reveal has
/// settled, so it never shows during the centered intro.
/// </summary>
[HarmonyPatch(typeof(NAncientNameBanner), "AnimateVfx")]
public static class AncientSourceLabel
{
    private const string LabelName = "BaseLibModSourceLabel";

    [HarmonyPostfix]
    static void Postfix(NAncientNameBanner __instance, ref Task __result)
        => __result = AddAfterSettled(__result, __instance);

    private static async Task AddAfterSettled(Task original, NAncientNameBanner banner)
    {
        await original;

        if (!GodotObject.IsInstanceValid(banner)) return;
        if (!BaseLibConfig.ShowAncientModSource) return;

        var ancient = Traverse.Create(banner).Field("_ancient").GetValue<AncientEventModel>();
        if (ancient == null) return;

        var name = WhatMod.FindModName(ancient.GetType());
        if (name == null) return;

        var epithet = banner.GetNodeOrNull<MegaLabel>("%Epithet");
        if (epithet == null || epithet.GetNodeOrNull(LabelName) != null) return;

        // Child of the now-settled epithet, so it follows its final position and 0.5 fade-in.
        var label = new MegaLabel
        {
            Name = LabelName,
            AnchorRight = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            OffsetTop = 26, OffsetBottom = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            MinFontSize = 11,
            MaxFontSize = 14,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var font = epithet.GetThemeFont("font");
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.AddThemeColorOverride("font_outline_color", Colors.Transparent);
        label.SetTextAutoSize($"{name}");
        epithet.AddChild(label);
    }
}
