using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public abstract class CustomRewardMessage : CustomMessage
{
    public required RewardType rewardType;
    public required RunLocation location;
    // public required bool wasSkipped;

    public override bool ShouldBroadcast => true;
    public override NetTransferMode Mode => NetTransferMode.Reliable;
    public override LogLevel LogLevel => LogLevel.VeryDebug;
    public override RunLocation Location => location;
}
