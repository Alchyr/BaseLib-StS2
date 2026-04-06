using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomMessage : INetMessage, IRunLocationTargetedMessage
{
    public abstract void MessageHandler<T>(T message, ulong senderID) where T : CustomMessage;
    public abstract bool ShouldBroadcast { get; }
    public abstract NetTransferMode Mode { get; }
    public abstract LogLevel LogLevel { get; }
    public abstract RunLocation Location { get; }

    public abstract void Deserialize(PacketReader reader);
    public abstract void Serialize(PacketWriter writer);
}
