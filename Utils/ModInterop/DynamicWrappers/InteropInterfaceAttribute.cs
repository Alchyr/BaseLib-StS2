namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Declares this interface as being a mirror of another in the specified target mod.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class InteropInterfaceAttribute : Attribute
{
    /// <summary>
    /// The target mod.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The source mod.
    /// </summary>
    public string SourceModId { get; }

    /// <summary>
    /// The namespace qualified name of the type in TargetModId. If <see langword="null"/>, will search for the attached interface name. The type name must match exactly.
    /// </summary>
    public string? TargetInterfaceName { get; }

    public InteropInterfaceAttribute(string targetModId, string sourceModId, string? targetInterfaceName = null)
    {
        TargetModId = targetModId;
        SourceModId = sourceModId;
        TargetInterfaceName = targetInterfaceName;
    }
}
