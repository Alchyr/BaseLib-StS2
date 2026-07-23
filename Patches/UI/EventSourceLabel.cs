using BaseLib.Config;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace BaseLib.Patches.UI;

/// <summary>
/// Events have no hover tip, so for modded events the source mod is shown as a small corner label
/// on the event screen instead.
/// </summary>
[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.SetEvent))]
public static class EventSourceLabel
{
    private const string LabelName = "BaseLibModSourceLabel";

    [HarmonyPostfix]
    static void AddSourceLabel(NEventLayout __instance, EventModel eventModel)
    {
        // Ancient events show their name as a banner; handled by AncientSourceLabel instead.
        if (eventModel is AncientEventModel) return;

        var existing = __instance.GetNodeOrNull<MegaLabel>(LabelName);

        var name = BaseLibConfig.ShowEventModSource ? WhatMod.FindModName(eventModel.GetType()) : null;
        if (name == null)
        {
            existing?.QueueFree();
            return;
        }

        if (existing != null)
        {
            existing.SetTextAutoSize(name);
            return;
        }

        var label = new MegaLabel
        {
            Name = LabelName,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MinFontSize = 24,
            MaxFontSize = 24,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = Colors.White with { A = 0.7f },
        };
        var font = ResourceLoader.Load<FontVariation>("res://themes/kreon_regular_shared.tres");
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.AddThemeColorOverride("font_outline_color", Colors.Black with { A = 0.55f });
        label.AddThemeConstantOverride("outline_size", 6);
        label.SetTextAutoSize(name);
        __instance.AddChild(label);

        // Pin to the bottom-left of the screen with equal margins, using global coordinates so the
        // nested event-room containers' geometry doesn't matter. Reposition when sizes settle.
        const float margin = 56f;
        void Place()
        {
            if (!GodotObject.IsInstanceValid(label)) return;
            var viewport = label.GetViewportRect().Size;
            label.GlobalPosition = new Vector2(margin, viewport.Y - margin - label.Size.Y);
        }
        Place();
        label.Resized += Place;
        __instance.Resized += Place;
    }
}
