using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Common.Rewards.LinkedRewardSet;

public class ExclusiveLinkedRewardChoiceMessage : ICustomMessage
{
    public int setId;
    public int containerIndex;
    public int nestedIndex;

    public bool ShouldBroadcast => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(setId);
        writer.WriteInt(containerIndex, 8);
        writer.WriteInt(nestedIndex, 8);
    }

    public void Deserialize(PacketReader reader)
    {
        setId = reader.ReadInt();
        containerIndex = reader.ReadInt(8);
        nestedIndex = reader.ReadInt(8);
    }

    public void HandleMessage(ulong senderId)
    {
        if (RunManager.Instance.State is null)
        {
            BaseLibMain.Logger.Error("RunManager.State is null in a context where it shouldn't be");
            return;
        }
        var synchronizer = RunManager.Instance.RewardsSetSynchronizer;
        var player = RunManager.Instance.State.GetPlayer(senderId);
        if (player is null) return;
        var rewardStateForPlayer = synchronizer.GetRewardStateForPlayer(player);
        var rewardsSetState = rewardStateForPlayer.rewardsStack.Last(); // same assumption the base game itself makes
        var set = rewardsSetState.set;

        if (containerIndex < 0 || containerIndex >= set.Rewards.Count) return;
        if (set.Rewards[containerIndex] is not CustomLinkedRewardSet container) return;
        if (nestedIndex < 0 || nestedIndex >= container.Rewards.Count) return;

        container.SetPendingExclusiveSelection(container.Rewards[nestedIndex]);
        TaskHelper.RunSafely(synchronizer.SelectRewardForPlayer(rewardsSetState, container));
    }
}