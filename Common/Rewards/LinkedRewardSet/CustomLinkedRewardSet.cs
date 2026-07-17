using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BaseLib.Abstracts;
using BaseLib.BaseLibScenes;
using BaseLib.Patches.Content;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards.LinkedRewardSet;

// We do not use BaseGame rewards because they are slightly buggy, and we are providing additional features which is too much work to patch in.
// May get obsolete once base game fully implements their version of it
/// <summary>
/// Custom RewardSet for linked rewards. Supports two ways of resolving: <br/>
/// 1. May choose one or none (Default. Equal to Sts1 behaviour) <br/>
/// 2. Must choose all or none <br/> <br/>
/// Do not nest LinkedRewards
/// </summary>
public class CustomLinkedRewardSet : CustomReward
{
    [CustomEnum] public static RewardType CustomLinkedRewardType;
    private const string SaveId = "baselib_customlinkedrewardset_children";
    
    
    private static readonly SpireField<Reward, CustomLinkedRewardSet?> ParentCustomLinkedRewardSet = new(() => null);
    public static bool TryGetCustomLinkedRewardSet(Reward reward, [NotNullWhen(true)] out CustomLinkedRewardSet? customLinkedRewardSet)
    {
        customLinkedRewardSet = ParentCustomLinkedRewardSet.Get(reward);
        return customLinkedRewardSet != null;
    }
    
    
    private LocString HoverTipTitle => new("static_hover_tips", LinkedRewardType == LinkedRewardType.Bundled 
                ? "BASELIB-BUNDLED_REWARDS.title" : "BASELIB-EXCLUSIVE_REWARDS.title");
    private LocString HoverTipDesc => new("static_hover_tips", LinkedRewardType == LinkedRewardType.Bundled
                ? "BASELIB-BUNDLED_REWARDS.description" : "BASELIB-EXCLUSIVE_REWARDS.description");
    public HoverTip HoverTip => new(HoverTipTitle, HoverTipDesc);
    public override LocString Description => new("gameplay_ui", "COMBAT_REWARD_LINKED");
    
    
    private List<Reward> _rewards;
    private LinkedRewardType _linkedRewardType;
    private bool _selectionStarted = false;
    private Reward? _pendingSelection;
    
    public IReadOnlyList<Reward> Rewards => _rewards.ToList();
    protected override RewardType RewardType  => CustomLinkedRewardType;
    public LinkedRewardType LinkedRewardType => _linkedRewardType;
    public override int RewardsSetIndex  => Rewards.Max(r => r.RewardsSetIndex);
    public override bool IsPopulated  => _rewards.All(r => r.IsPopulated);


    
    /// <summary>
    /// Exists only for BaseLib Save logic registration. <br/>
    /// Do not use.
    /// </summary>
    private CustomLinkedRewardSet() : base(null!)
    {
        _rewards = [];
    }
    public CustomLinkedRewardSet(List<Reward> rewards, Player player, LinkedRewardType linkedRewardType = LinkedRewardType.Exclusive) : base(player)
    {
        _rewards = rewards;
        _linkedRewardType = linkedRewardType;
        foreach (var reward in _rewards)
            ParentCustomLinkedRewardSet.Set(reward, this);
    }
    
    public override void Populate()
    {
        foreach (var reward in _rewards)
            reward.Populate();
    }
       
    public override void MarkContentAsSeen()
    {
        foreach (var reward in _rewards)
            reward.MarkContentAsSeen();
    }
    
    public override void OnSkipped()
    {
        foreach (var reward in _rewards)
            reward.OnSkipped();
    }
    
    protected override async Task<bool> OnSelect()
    {
        if (_selectionStarted) return SuccessfullySelected;
        _selectionStarted = true;
        return LinkedRewardType switch
        {
                    LinkedRewardType.Exclusive => await OnSelectExclusive(),
                    LinkedRewardType.Bundled => await OnSelectBundled(),
                    _ => true
        };
    }

    private async Task<bool> OnSelectExclusive()
    {
        var chosen = _pendingSelection;
        _pendingSelection = null;
        if (chosen == null)
        {
            BaseLibMain.Logger.Error("No Reward selected. Should not happen!");
            _selectionStarted = false;
            return false;
        }

        if (!(chosen.SuccessfullySelected || await chosen.SelectUnsynchronized()))
        {
            _selectionStarted = false;
            return false;
        }
        foreach (var reward in _rewards.ToList().Where(reward => reward != chosen && !reward.SuccessfullySelected))
            reward.OnSkipped();
        return true;
    }
    private async Task<bool> OnSelectBundled()
    {
        var ordered = _rewards.ToList();
        var clicked = _pendingSelection;
        _pendingSelection = null;
        if (clicked != null && ordered.Remove(clicked)) // move the selected reward to the front, so it is offered first
            ordered.Insert(0, clicked);
        foreach (var reward in ordered)
        {
            if (reward.SuccessfullySelected) continue;
            if (await reward.SelectUnsynchronized()) continue;
            if (!_rewards.Any(r => r.SuccessfullySelected))
            {
                // Nothing claimed yet: the player was only peeking, back out and keep the set available.
                _selectionStarted = false;
                return false;
            }

            // The bundle is already committed, so a skip is a final "I don't want any of these" (for this reward only).
            // The remaining rewards are still offered before the set resolves.
            reward.OnSkipped();
        }
        return true;
    }
    
    /// <summary>
    /// <see cref="NCustomLinkedRewardSet"/> is not being notified of this.
    /// Using it during the reward screen can lead to undefined behaviour.
    /// </summary>
    /// <param name="reward"></param>
    public void RemoveReward(Reward reward) => _rewards.Remove(reward);
    
    
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;
    
    /// <summary>
    /// Exclusive: the reward the player chose.
    /// Bundled: the reward whose button was clicked (player should see that one first to not confuse them)
    /// </summary>
    public void SetPendingSelection(Reward chosen) => _pendingSelection = chosen;
    
    public static CustomReward CreateFromSerializable(SerializableReward save, Player player)
        => new CustomLinkedRewardSet([], player);

    private void RestoreRewards(List<Reward> rewards, LinkedRewardType linkedRewardType)
    {
        _rewards = rewards;
        _linkedRewardType = linkedRewardType;
        foreach (var reward in _rewards)
            ParentCustomLinkedRewardSet.Set(reward, this);
    }

    public override void Initialize()
    {
        base.Initialize();
        ExtendedSaveTypes.RegisterObjectSaveType<SerializableCustomLinkedRewardSet>(
                    ExtendedSaveTypes.PropertyFunc<SerializableCustomLinkedRewardSet, List<SerializableReward>>(
                                nameof(SerializableCustomLinkedRewardSet.Rewards)),
                    ExtendedSaveTypes.PropertyFunc<SerializableCustomLinkedRewardSet, int>(
                                nameof(SerializableCustomLinkedRewardSet.LinkedRewardTypeValue))
        );
        ExtendedSaveHandlers<Reward, SerializableReward>.RegisterSave<SerializableCustomLinkedRewardSet>(
                    SaveId,
                    reward => reward is CustomLinkedRewardSet clrs
                                ? new SerializableCustomLinkedRewardSet
                                {
                                            Rewards = clrs.Rewards.Select(r => r.ToSerializable()).ToList(),
                                            LinkedRewardTypeValue  = (int)clrs.LinkedRewardType
                                }
                                : null,
                    (reward, value) =>
                    {
                        if (reward is not CustomLinkedRewardSet clrs || value == null) return;
                        var restored = value.Rewards.Select(sr => Reward.FromSerializable(sr, reward.Player)).ToList();
                        clrs.RestoreRewards(restored, (LinkedRewardType)value.LinkedRewardTypeValue );
                    });
    }
}

public class SerializableCustomLinkedRewardSet : IPacketSerializable
{
    public List<SerializableReward> Rewards { get; set; } = [];
    public int LinkedRewardTypeValue { get; set; }


    public void Serialize(PacketWriter writer)
    {
        writer.WriteList(Rewards);
        writer.WriteInt(LinkedRewardTypeValue);
    }

    public void Deserialize(PacketReader reader)
    {
        Rewards = reader.ReadList<SerializableReward>();
        LinkedRewardTypeValue = reader.ReadInt();
    }
}

[HarmonyPatch]
public static class CustomLinkedRewardSet_Patches
{
    [HarmonyPatch(typeof(NRewardButton), "SetReward")]
    [HarmonyPrefix]
    private static void ThrowIfMisuseOfCustomRewardSet(Reward reward)
    {
        if(reward is CustomLinkedRewardSet)
            throw new ArgumentException("You aren't allowed to apply a CustomLinkedRewardSet to an NRewardButton");
    }

    [HarmonyPatch(typeof(Reward), "HoverTips", MethodType.Getter)]
    [HarmonyPostfix]
    private static void AddHoverTip(Reward __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!CustomLinkedRewardSet.TryGetCustomLinkedRewardSet(__instance, out var customLinkedRewardSet)) return;
        var list = __result.ToList();
        list.Add(customLinkedRewardSet.HoverTip);
        __result = list;
    }
}


[HarmonyPatch]
public static class CustomLinkedRewardSet_MultiplayerPatches
{
    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
    private static class RedirectNestedRewardSelection
    {
        // index must be top level reward, so we reroute to linked container
        [HarmonyPrefix]
        static bool Prefix(RewardsSetSynchronizer __instance, ref Task<bool> __result, ref Reward reward)
        {
            if (!CustomLinkedRewardSet.TryGetCustomLinkedRewardSet(reward, out var parent)) return true;
            if (parent.SuccessfullySelected)
            {
                // Bundled sibling buttons re-enter here via NCustomLinkedRewardSet.GetReward after the set
                // already completed.
                // Report success without reselecting.
                __result = Task.FromResult(true);
                return false;
            }
            // We can not use the existing Synchronisation method for Linked rewards.
            // The game only sends the index of the reward, no information about anything else.
            // Exclusive needs to know which nested reward was chosen
            //  Bundled  needs to know which nested reward to resolve first
            // (nested CardReward choices sync in resolution order via PlayerChoiceSynchronizer,
            // so all clients must resolve in the same order).
            // Using a custom message to bypass base games RewardsSetSynchronizer completely.
            var rewardStateForPlayer = __instance.GetRewardStateForPlayer(__instance.LocalPlayer);
            var rewardsSetState = rewardStateForPlayer.rewardsStack.Last();
            var containerIndex = rewardsSetState.set.Rewards.IndexOf(parent);
            var nestedIndex = parent.Rewards.ToList().IndexOf(reward);

            parent.SetPendingSelection(reward);
            __result = __instance.SelectRewardForPlayer(rewardsSetState, parent); // apply locally immediately

            CustomMessageWrapper.Send(new CustomLinkedRewardChoiceMessage
            {
                        setId = rewardsSetState.set.Id,
                        containerIndex = containerIndex,
                        nestedIndex = nestedIndex
            });
            return false;
        }
    }
}