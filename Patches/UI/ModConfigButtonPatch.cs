using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Godot;
using System;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using BaseLib.Config;
using BaseLib.Config.UI;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer._Ready))]
public static class ModConfigButtonPatch
{
    public static void Postfix(NModInfoContainer __instance)
    {
        NButton configButton;
        try {
            configButton = SceneHelper.Instantiate<NButton>("ui/button");
        } catch {
            configButton = new NButton();
        }
        
        __instance.AddChild(configButton);
        
        configButton.Name = "ConfigButton";
        // Position it at bottom right
        configButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        configButton.Position += new Vector2(-200, -80); // Adjusted position
        
        var label = configButton.GetNodeOrNull<MegaLabel>("Visuals/Label");
        if (label == null) {
            label = new MegaLabel();
            label.Name = "Label";
            configButton.AddChild(label);
        }
        label.Text = "Config";

        configButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(btn => {
            OnConfigPressed(__instance);
        }));
        
        configButton.Hide();
    }

    private static void OnConfigPressed(NModInfoContainer container)
    {
        var mod = ModConfigFillPatch.CurrentMod;
        if (mod == null) return;
        
        var configPanel = Registry.Get(mod.pckName);
        if (configPanel != null)
        {
            // Find NModdingScreen
            Node parent = container.GetParent();
            while (parent != null && !(parent is NModdingScreen))
            {
                parent = parent.GetParent();
            }

            if (parent is NModdingScreen screen)
            {
                // Instantiate our manual config control
                var configControl = new ModConfigControl(mod.manifest?.name ?? mod.pckName, configPanel, screen);
                // We add it to the screen's parent and hide the screen
                screen.GetParent().AddChild(configControl);
                screen.Visible = false;
            }
        }
    }
}

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class ModConfigFillPatch
{
    public static Mod CurrentMod;

    public static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        CurrentMod = mod;
        var configButton = __instance.GetNodeOrNull<NButton>("ConfigButton");
        if (configButton != null)
        {
            if (mod.wasLoaded && Registry.Get(mod.pckName) != null)
            {
                configButton.Show();
            }
            else
            {
                configButton.Hide();
            }
        }
    }
}
