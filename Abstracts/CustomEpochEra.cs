using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Abstracts;



public abstract class CustomEpochEra
{
    /// <summary>
    /// Points to a new [CustomEnum] EpochEra.
    /// </summary>
    public abstract EpochEra CustomEra { get; }
    
    /// <summary>
    /// The base game Era used as a frame of reference for where to insert the custom era.
    /// </summary>
    public abstract EpochEra ReferenceEra { get; }
    /// <summary>
    /// The direction in which the custom era will be inserted.
    /// </summary>
    public abstract RelativeEraDirection Direction { get; }
    /// <summary>
    /// Used for ordering multiple custom eras at the same <see cref="ReferenceEra">Reference Era</see> and <seealso cref="Direction"/>.
    /// Larger numbers are further away from the reference era.
    /// </summary>
    public virtual int DirectionDepth => 0;
    
    /// <summary>
    /// The icon displayed on the timeline line at the bottom of the era.
    /// </summary>
    public abstract string EraIconPath { get; }
    public Texture2D EraIconTexture => PreloadManager.Cache.GetTexture2D(EraIconPath);
    /// <summary>
    /// Stops the game from auto-sizing the era icon.
    /// </summary>
    public virtual bool UseOriginalImageSize => false;
    /// <summary>
    /// Stops the game from applying the default tint to the era icon.
    /// </summary>
    public virtual bool DisableTinting => false;


     [HarmonyPatch]
    private static class Patches
    {
        private static readonly FieldInfo? NEraColumnIconField = AccessTools.Field(typeof(NEraColumn), "_icon");
        private static readonly FieldInfo? NEraColumnNameField = AccessTools.Field(typeof(NEraColumn), "_name");
        private static readonly FieldInfo? NEraColumnYearField = AccessTools.Field(typeof(NEraColumn), "_year");
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NEraColumn), nameof(NEraColumn.Init))]
        private static void ReplaceIconForCustomEraColumn(NEraColumn __instance, EpochSlotData epochSlot)
        {
            BaseLibMain.Logger.Info("ReplaceIconForCustomEraColumn");
            // We assume in a custom Era can only be CustomEpochs
            if (epochSlot.Model is not CustomEpochModel customEpochModel) return;
            if (customEpochModel.CustomEra is null) return;
            if (NEraColumnIconField?.GetValue(__instance) is not TextureRect textureRect) return;
            if (NEraColumnNameField?.GetValue(__instance) is not MegaLabel name) return;
            if (NEraColumnYearField?.GetValue(__instance) is not MegaLabel year) return;
            
            if (textureRect.Texture is null)
                textureRect.Visible = true; // set to false at some point if the original method cant get a proper texture
            textureRect.Texture = customEpochModel.CustomEra.EraIconTexture;
            BaseLibMain.Logger.Info($"{textureRect.Texture.ResourcePath}");
            if (customEpochModel.CustomEra.UseOriginalImageSize)
            {
                var originalSize = customEpochModel.CustomEra.EraIconTexture.GetSize();
                textureRect.Size = originalSize;
                textureRect.Position -= (originalSize / 2) - new Vector2(24, 24);
                // large textures would begin overlapping the bottom epochs
                textureRect.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            if (customEpochModel.CustomEra.DisableTinting)
            {
                textureRect.Modulate = new Color("ffffff");
            }
            var slugifiedName = StringHelper.Slugify(customEpochModel.CustomEra.GetType().Name);
            name.SetTextAutoSize(new LocString("eras", slugifiedName + ".name").GetFormattedText());
            year.SetTextAutoSize(new LocString("eras", slugifiedName + ".year").GetFormattedText());
        }
    }
}

/// <summary>
/// The direction in which a <see cref="CustomEpochEra">Custom Era</see> will be inserted, in reference to its <see cref="CustomEpochEra.ReferenceEra">Reference Era</see>
/// </summary>
public enum RelativeEraDirection
{
    /// <summary>
    /// Shouldn't be used.
    /// </summary>
    None,
    /// <summary>
    /// Insert to the left of reference Era.
    /// </summary>
    Before,
    /// <summary>
    /// Insert to the right of reference Era.
    /// </summary>
    After
}