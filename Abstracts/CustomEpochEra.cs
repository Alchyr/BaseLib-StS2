using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Abstracts;

public abstract class CustomEpochEra
{
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
    /// Insert to the left.
    /// </summary>
    Before,
    /// <summary>
    /// Insert to the right.
    /// </summary>
    After
}