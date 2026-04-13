using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomTargetedMessage : ICustomTargetedMessage
{
    public abstract RunLocation Location { get; set; }

    public abstract void Dispose(RunLocationTargetedMessageBuffer messageBuffer);
    public abstract void Initialize(RunLocationTargetedMessageBuffer messageBuffer);
}
