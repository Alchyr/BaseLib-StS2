using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Indicates that this object is intended to be wrapped for consumption by a specific mod. A type that implements this interface does not need to be registered (though it's SourceInterface still needs to be).
/// </summary>
/// <remarks>
/// Recommended to implement this on an <see cref="AbstractModel"/> that is being used as a hook listener for custom hooks.
/// If the hook listener filters models using OfTypeDynamic&lt;ICustomHook&gt;(), then this will be included.
/// </remarks>
public interface IWrappable
{
    /// <summary>
    /// The mod that this object can be wrapped for.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The registered mirror interface that this object implements.
    /// </summary>
    public Type SourceInterface { get; }
}