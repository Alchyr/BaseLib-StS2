namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// A wrapper object for seamless inter-mod communication via interfaces.
/// </summary>
public interface IDynamicWrapper
{
    /// <summary>
    /// The mod that this wrapper belongs to.
    /// </summary>
    public string WrapperModId { get; }

    /// <summary>
    /// The mod that the instance object belongs to.
    /// </summary>
    public string InstanceModId { get; }

    /// <summary>
    /// The underlying object that this wrapper encapsulates.
    /// </summary>
    public object Instance { get; }
}