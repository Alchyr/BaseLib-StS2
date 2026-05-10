using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// Message for transforming a card from a new reward type
/// </summary>
public sealed class CardTransformRewardMessage : ICustomTargetedMessage
{
    /// <inheritdoc/>
    public void HandleMessage(ulong senderId)
    {
        var rs = RunManager.Instance.RewardSynchronizer;

        Player? player = rs._playerCollection.GetPlayer(senderId);
        if (player == rs.LocalPlayer)
        {
            throw new InvalidOperationException("CardTransformRewardMessage should not be sent to the player transforming the card");
        }
        TaskHelper.RunSafely(rs.DoCardTransform(player, Amount, Upgrade));
    }

    /// <summary>
    /// Whether to upgrade the card as well as transforming
    /// </summary>
    public required bool Upgrade;
    /// <summary>
    /// The amount of cards to select from
    /// </summary>
    public required int Amount;

    public bool wasSkipped { get; set; }

    /// <inheritdoc/>
    public LogLevel LogLevel => LogLevel.Debug;

    /// <inheritdoc/>
    public RunLocation Location { get; set; }
    /// <inheritdoc/>
    public bool IsRewardMessage => true;
    /// <inheritdoc/>
    public bool ShouldBroadcast => true;

    /// <inheritdoc/>
    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        Amount = reader.ReadInt();
        Upgrade = reader.ReadBool();
    }

    /// <inheritdoc/>
    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteInt(Amount);
        writer.WriteBool(Upgrade);
    }
}
