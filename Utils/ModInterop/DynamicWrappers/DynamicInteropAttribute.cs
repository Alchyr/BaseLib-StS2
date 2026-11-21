namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Registers this interface as a mirror of another in the specified target mod, allowing it to be used for dynamic interop. Registration is only performed once all other such attributes have been discovered.
/// </summary>
/// <remarks>
/// This is an alternative to manually registering your interfaces.
/// </remarks>
/// <param name="targetModId">The target mod.</param>
/// <param name="sourceModId">The source mod.</param>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class DynamicInteropAttribute(string targetModId, string sourceModId) : Attribute
{
    /// <summary>
    /// The target mod.
    /// </summary>
    public string TargetModId { get; } = targetModId;

    /// <summary>
    /// The source mod.
    /// </summary>
    public string SourceModId { get; } = sourceModId;

    /// <summary>
    /// The namespace qualified name of the type in TargetModId. Required if the target has a different name to your own. If <see langword="null"/>, will search for the attached interface name.
    /// </summary>
    public string? TargetInterfaceFullName { get; set; } = null;
}