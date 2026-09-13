using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Indicates that this object is intended to be wrapped for consumption by a specific mod.
/// </summary>
/// <remarks>
/// Recommended to implement this on an <see cref="AbstractModel"/> that is being used as a hook listener for custom hooks.
/// If the hook listener filters models using OfTypeDynamic&lt;ICustomHook&gt;(), this will be picked up.
/// </remarks>
public interface IWrappable
{
    /// <summary>
    /// The mod that this object can be wrapped for.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The registered mirror interface that this object implements. Providing this to <see cref="DynamicWrapper.Wrap{T}(string, object, Type?)"/>
    /// can help with finding a wrapper, even if the concrete type itself is not registered.
    /// </summary>
    public Type InterfaceType { get; }
}