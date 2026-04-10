using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using BaseLib.Patches.Content;

namespace BaseLib.Abstracts;

/// <summary>
/// The type to inherit from to add a custom message
/// </summary>
public abstract class CustomMessage : INetMessage
{
    /// <summary>
    /// Register your message type here
    /// Needs to be a function that takes <c>(<see cref="CustomMessage"/> message, <see langword="ulong"/> senderId)</c>
    /// See <seealso cref="RewardSynchronizerExtensions.HandleCardTransformedMessage"/> for an example. You probably want to use an <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods">Extension Method</see>
    /// </summary>
    public abstract void Initialize(RunLocationTargetedMessageBuffer messageBuffer);

    /// <summary>
    /// Unregister your message type here<br/>
    /// Reference the same function you registered in <see cref="Initialize(RunLocationTargetedMessageBuffer)"/>
    /// </summary>
    public abstract void Dispose(RunLocationTargetedMessageBuffer messageBuffer);

    public abstract bool ShouldBroadcast { get; }
    public abstract NetTransferMode Mode { get; }
    public abstract LogLevel LogLevel { get; }

    /// <summary>
    /// Read out the necessary data from the saved info, in the order it was written
    /// </summary>
    public abstract void Deserialize(PacketReader reader);
    /// <summary>
    /// Save necessary data, to be read out when reconstructing the message
    /// </summary>
    public abstract void Serialize(PacketWriter writer);
}
