using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;

namespace BaseLib.Abstracts;

/// Message interface 
public interface ICustomTargetedMessage : ICustomMessage, IRunLocationTargetedMessage
{
    /// <inheritdoc/>
    abstract void Initialize(RunLocationTargetedMessageBuffer messageBuffer);

    /// <inheritdoc/>
    abstract void Dispose(RunLocationTargetedMessageBuffer messageBuffer);
}
