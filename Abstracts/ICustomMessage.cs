using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BaseLib.Abstracts;

public interface ICustomMessage : INetMessage
{
    /// <summary>
    /// Register your message type here.
    /// Needs to be a function that takes <c>(<see cref="ICustomMessage"/> message, <see langword="ulong"/> senderId)</c>
    /// See <seealso cref="RewardSynchronizerExtensions.HandleCardTransformedMessage"/> for an example. You probably want to use an <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods">Extension Method</see>
    /// </summary>
    public abstract void Initialize(INetGameService netService);

    /// <summary>
    /// Unregister your message type here<br/>
    /// Reference the same function you registered in <see cref="Initialize(RunLocationTargetedMessageBuffer)"/>
    /// </summary>
    public abstract void Dispose(INetGameService netService);
}

