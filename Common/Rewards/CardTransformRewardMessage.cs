using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Common.Rewards;

public sealed class CardTransformRewardMessage : CustomRewardMessage
{
    private void HandleCardTransformedMessage(CardTransformRewardMessage message, ulong senderId)
    {
        if (CombatManager.Instance.IsInProgress)
        {
            // RunManager.Instance.RewardSynchronizer;
        }
    }
    public required bool Upgrade;
    public override void Dispose(RunLocationTargetedMessageBuffer messageBuffer)
    {
    }

    public override void Initialize(RunLocationTargetedMessageBuffer messageBuffer)
    {
    }
    public override void Deserialize(PacketReader reader)
    {
    }


    public override void Serialize(PacketWriter writer)
    {
    }
}
