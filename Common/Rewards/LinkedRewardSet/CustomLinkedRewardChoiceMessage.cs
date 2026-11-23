using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Common.Rewards.LinkedRewardSet;

/// <summary>
/// Broadcasts which reward of a <see cref="CustomLinkedRewardSet"/> a player picked, so every other client can replay
/// that pick on its own copy of the same reward set.
/// <br/><br/>
/// Both ends must agree on two ids. The outer one is <see cref="CustomMessageWrapper.WrapperMessageId"/>, a free
/// byte slot picked from the game's registered <c>INetMessage</c> table by <c>AdjustCustomMessageKeys</c>; it depends
/// on how many message types the game and installed mods register, not on <see cref="ICustomMessage"/> order. The
/// inner id is <c>(FullName ?? Name).ComputeBasicHash()</c>, an MD5-FNV hash written as a 32 bit int, so it is bound
/// to this type's full name and unaffected by registration or implementation order (a rare 32 bit collision is
/// resolved by deterministic probing over the name-sorted types). Only a change to the wire format or to one of
/// those id mappings breaks interoperability; adding or reordering custom messages does not.
/// <br/><br/>
/// Wire format is 48 bit and unchanged since 3.4.5: <see cref="setId"/> takes 32 bit, and each index takes 8 bit
/// (32 + 8 + 8). That is the width the base game uses for the same kind of index (<c>RewardSelectedMessage</c> sends
/// its <c>rewardIndex</c> as 8 bit): an index addresses one reward inside a single reward set, which holds a handful
/// of entries, and <see cref="TryApplyChoice"/> bounds-checks both indices against the actual lists.
/// </summary>
public class CustomLinkedRewardChoiceMessage : ICustomMessage
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

    public void HandleMessage(ulong senderId) => TryApplyChoice(senderId, true);

    /// <summary>
    /// Replays this pick on the local copy of the reward set whose id is <see cref="setId"/>.
    /// The set is addressed by id instead of assuming it is on top of the stack: every client builds its own stack
    /// (<see cref="RewardsSetSynchronizer.BeginRewardsSet"/>) and the sets are not necessarily created at the same time.
    /// </summary>
    /// <param name="senderId">Player that made the pick.</param>
    /// <param name="bufferIfSetNotStarted">
    /// When true and the set has not been created here yet, the pick is buffered and replayed by
    /// <see cref="CustomLinkedRewardChoiceBuffer"/> once <see cref="RewardsSetSynchronizer.BeginRewardsSet"/> starts it.
    /// The replay path passes false so a buffered pick is never buffered twice.
    /// </param>
    /// <returns>Whether the pick was replayed on one of this client's reward sets.</returns>
    internal bool TryApplyChoice(ulong senderId, bool bufferIfSetNotStarted)
    {
        if (RunManager.Instance.State is null)
        {
            BaseLibMain.Logger.Error($"CustomLinkedRewardChoiceMessage for set {setId} from player {senderId} arrived without a run");
            return false;
        }

        var synchronizer = RunManager.Instance.RewardsSetSynchronizer;
        var player = RunManager.Instance.State.GetPlayer(senderId);
        if (player is null)
        {
            BaseLibMain.Logger.Warn($"CustomLinkedRewardChoiceMessage for set {setId} from unknown player {senderId}");
            return false;
        }

        var rewardStateForPlayer = synchronizer.GetRewardStateForPlayer(player);
        var rewardsSetState = rewardStateForPlayer.rewardsStack.LastOrDefault(state => state.set.Id == setId);
        if (rewardsSetState is null)
        {
            // Same condition the base game uses: nextId <= setId means this client has not started that set yet.
            if (bufferIfSetNotStarted && rewardStateForPlayer.nextId <= setId)
            {
                BaseLibMain.Logger.Debug($"Buffering CustomLinkedRewardChoiceMessage for set {setId} from player {senderId}: local next set id is {rewardStateForPlayer.nextId}");
                CustomLinkedRewardChoiceBuffer.Buffer(synchronizer, this, senderId);
            }
            else
            {
                BaseLibMain.Logger.Warn($"CustomLinkedRewardChoiceMessage for set {setId} from player {senderId} cannot be applied: " +
                        $"set is not on the stack (depth {rewardStateForPlayer.rewardsStack.Count}), local next set id is {rewardStateForPlayer.nextId}, " +
                        $"set completed locally: {rewardStateForPlayer.completedRewards.ContainsKey(setId)}, " +
                        $"container index {containerIndex}, nested index {nestedIndex}");
            }
            return false;
        }

        var set = rewardsSetState.set;
        if (containerIndex < 0 || containerIndex >= set.Rewards.Count)
        {
            BaseLibMain.Logger.Warn($"CustomLinkedRewardChoiceMessage from player {senderId} for set {setId}: container index {containerIndex} " +
                    $"is out of range for {set.Rewards.Count} reward(s)");
            return false;
        }

        var topLevelReward = set.Rewards[containerIndex];
        if (topLevelReward is not CustomLinkedRewardSet container)
        {
            BaseLibMain.Logger.Warn($"CustomLinkedRewardChoiceMessage from player {senderId} for set {setId}: reward {containerIndex} " +
                    $"is {topLevelReward.GetType().Name}, not a {nameof(CustomLinkedRewardSet)}");
            return false;
        }

        // Snapshot the list once: the getter builds a new one on every access.
        var nestedRewards = container.Rewards;
        if (nestedIndex < 0 || nestedIndex >= nestedRewards.Count)
        {
            BaseLibMain.Logger.Warn($"CustomLinkedRewardChoiceMessage from player {senderId} for set {setId}: nested index {nestedIndex} " +
                    $"is out of range for {nestedRewards.Count} linked reward(s) in container {containerIndex}");
            return false;
        }

        container.SetPendingSelection(nestedRewards[nestedIndex]);
        TaskHelper.RunSafely(synchronizer.SelectRewardForPlayer(rewardsSetState, container));
        return true;
    }
}

/// <summary>
/// Buffers <see cref="CustomLinkedRewardChoiceMessage"/>s whose reward set does not exist on this client yet, then
/// replays them when <see cref="RewardsSetSynchronizer.BeginRewardsSet"/> creates that set.
/// The base game buffers its own reward messages exactly this way (<c>PlayerRewardState.bufferedMessages</c>);
/// custom messages have to bring their own.
/// <br/><br/>
/// Storage is a <see cref="SpireField{TKey,TVal}"/> (ConditionalWeakTable), so entries live exactly as long as the
/// synchronizer they belong to, and they are dropped on <see cref="RewardsSetSynchronizer.Dispose"/>.
/// </summary>
[HarmonyPatch(typeof(RewardsSetSynchronizer))]
public static class CustomLinkedRewardChoiceBuffer
{
    // UNCONFIRMED: 256 is a defensive bound picked here, not a value from DEVELOP.md.
    /// <summary>
    /// Upper bound on buffered picks. A reward set holds a handful of rewards, so reaching this means this client
    /// never starts the sets the peer is choosing from; drop instead of growing without limit.
    /// </summary>
    private const int MaxBufferedChoices = 256;

    private readonly record struct PendingChoice(ulong SenderId, CustomLinkedRewardChoiceMessage Message);

    private static readonly SpireField<RewardsSetSynchronizer, List<PendingChoice>> PendingChoices = new(() => []);

    internal static void Buffer(RewardsSetSynchronizer synchronizer, CustomLinkedRewardChoiceMessage message, ulong senderId)
    {
        var pending = PendingChoices[synchronizer]!;
        if (pending.Count >= MaxBufferedChoices)
        {
            BaseLibMain.Logger.Error($"Dropping CustomLinkedRewardChoiceMessage for set {message.setId} from player {senderId}: " +
                    $"{pending.Count} pick(s) are already waiting for reward sets this client never started");
            return;
        }
        pending.Add(new PendingChoice(senderId, message));
    }

    // Postfix: set.Id is assigned and the set is pushed inside BeginRewardsSet, so the replay can find it on the stack.
    // Set ids are per player (each player's counter starts at 0), so the owner has to match as well.
    [HarmonyPatch(nameof(RewardsSetSynchronizer.BeginRewardsSet))]
    [HarmonyPostfix]
    private static void OnRewardsSetStarted(RewardsSetSynchronizer __instance, RewardsSet set)
    {
        var pending = PendingChoices[__instance]!;
        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].Message.setId != set.Id || pending[i].SenderId != set.Player.NetId) continue;

            var choice = pending[i];
            pending.RemoveAt(i);
            i--;
            BaseLibMain.Logger.Debug($"Replaying buffered CustomLinkedRewardChoiceMessage for set {set.Id} of player {choice.SenderId}");
            choice.Message.TryApplyChoice(choice.SenderId, false);
        }
    }

    [HarmonyPatch(nameof(RewardsSetSynchronizer.Dispose))]
    [HarmonyPostfix]
    private static void OnSynchronizerDisposed(RewardsSetSynchronizer __instance)
    {
        var pending = PendingChoices[__instance]!;
        if (pending.Count == 0) return;

        BaseLibMain.Logger.Warn($"Dropping {pending.Count} buffered linked reward choice(s) at the end of the run: " +
                $"their reward sets were never started locally (set ids: {string.Join(", ", pending.Select(choice => choice.Message.setId).Distinct())})");
        pending.Clear();
    }
}
