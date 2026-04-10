using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomRewardMessage : CustomMessage, IRunLocationTargetedMessage
{
    public required RewardType rewardType;
    public required bool wasSkipped;

    public override bool ShouldBroadcast => true;
    public override NetTransferMode Mode => NetTransferMode.Reliable;

    public RunLocation Location { get; set; }
}
