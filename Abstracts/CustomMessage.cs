using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace BaseLib.Abstracts;

/// <summary>
/// The type to inherit from to add a custom message.
/// Not actually necessary, just provides some helpful abstract methods as reminders/hints for setting up a message
/// </summary>
public abstract class CustomMessage : INetMessage, ICustomMessage
{
    public abstract void Serialize(PacketWriter writer);
    public abstract void Deserialize(PacketReader reader);

    public abstract void Initialize(INetGameService netService);
    public abstract void Dispose(INetGameService netService);

    /// <summary>
    /// Whether to broadcast the message
    /// </summary>
    public abstract bool ShouldBroadcast { get; }
    /// <summary>
    /// The way to transfer the message
    /// </summary>
    public abstract NetTransferMode Mode { get; }
    /// <summary>
    /// What log level to output to (referenced when calling the vanilla handler(s) for messages)
    /// </summary>
    public abstract LogLevel LogLevel { get; }
}

