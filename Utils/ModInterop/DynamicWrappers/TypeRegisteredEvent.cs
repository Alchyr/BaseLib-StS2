namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// EventHandler for the <see cref="DynamicWrapper.TypeRegistered"/> event.
/// </summary>
/// <param name="e">An object providing data related to the event.</param>
public delegate void TypeRegisteredEventHandler(TypeRegisteredEventArgs e);

/// <summary>
/// Provides data related to the <see cref="DynamicWrapper.TypeRegistered"/> event.
/// </summary>
public class TypeRegisteredEventArgs : EventArgs
{
    internal TypeRegisteredEventArgs(string targetModId, Type targetInterface, string sourceModId, Type? sourceType, Type sourceInterface)
    {
        TargetModId = targetModId;
        TargetInterface = targetInterface;
        SourceModId = sourceModId;
        SourceType = sourceType;
        SourceInterface = sourceInterface;
    }

    /// <summary>
    /// The mod that the type was registered against.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The interface from TargetModId that is a mirror of RegisteredInterface.
    /// </summary>
    public Type TargetInterface { get; }

    /// <summary>
    /// The mod who's type was registered.
    /// </summary>
    public string SourceModId { get; }

    /// <summary>
    /// The type that was registered, or <see langword="null"/> if just the interface alone was registered.
    /// </summary>
    public Type? SourceType { get; }

    /// <summary>
    /// The interface that was registered, that is a mirror of TargetInterface.
    /// </summary>
    public Type SourceInterface { get; }
}