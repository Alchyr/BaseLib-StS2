namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// EventHandler for the TypeRegistered event.
/// </summary>
/// <param name="e">An object providing data related to the event.</param>
public delegate void TypeRegisteredEventHandler(TypeRegisteredEventArgs e);

/// <summary>
/// Provides data related to the <see cref="DynamicWrapper.TypeRegistered"/> event.
/// </summary>
public class TypeRegisteredEventArgs : EventArgs
{
    internal TypeRegisteredEventArgs(string targetModId, string sourceModId, Type registeredType, Type registeredInterface, Type targetInterface)
    {
        TargetModId = targetModId;
        SourceModId = sourceModId;
        RegisteredType = registeredType;
        RegisteredInterface = registeredInterface;
        TargetInterface = targetInterface;
    }

    /// <summary>
    /// The mod that the type was registered against.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The mod who's type was registered.
    /// </summary>
    public string SourceModId { get; }

    /// <summary>
    /// The type that was registered.
    /// </summary>
    public Type RegisteredType { get; }

    /// <summary>
    /// The interface of the registered type (may be the same as RegisteredType if the interface itself was registered). This is the mirror of TargetInterface.
    /// </summary>
    public Type RegisteredInterface { get; }

    /// <summary>
    /// The interface from TargetModId that is the mirror of RegisteredInterface.
    /// </summary>
    public Type TargetInterface { get; }
}