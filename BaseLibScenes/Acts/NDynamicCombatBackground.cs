using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;

namespace BaseLib.BaseLibScenes.Acts;

/// <summary>
/// Attached to the dynamic_background.tscn root node.<br></br>
/// Will create as many background Layers as needed.
/// The individual layer scenes still need to be created manually
/// </summary>
[GlobalClass]
public partial class NDynamicCombatBackground : NCombatBackground
{
    private void CreateLayerNodes(BackgroundAssets assets)
    {
        Control? baseLayer = GetNode<Control>("%Layer_00");
        if (baseLayer is null)
        {
            BaseLibMain.Logger.Error("Attempt to create dynamic layers failed, no base layer 'Layer_00' found!");
            return;
        }
        Node parent = baseLayer.GetParent();
        for (int i = 1; i < assets.BgLayers.Count; i++)
        {
            Control newLayer = (Control)baseLayer.Duplicate();
            newLayer.Name = $"Layer_{i:00}";
            parent.AddChildSafely(newLayer);
            parent.MoveChild(newLayer, i);
        }
    }

    [HarmonyPatch(typeof(NCombatBackground), "SetLayers")]
    class NCombatBackgroundSetLayers
    {
        [HarmonyPrefix]
        static void CreateLayers(NCombatBackground __instance, BackgroundAssets bg)
        {
            if (__instance is not NDynamicCombatBackground nDynamicCombatBackground) return;
            nDynamicCombatBackground.CreateLayerNodes(bg);
        }
    }
}
