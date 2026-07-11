using System.Diagnostics.CodeAnalysis;
using BaseLib.Abstracts;
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

/// <summary>
/// We do not use BaseGame rewards because they are slightly buggy, and we are providing additional features which is too much work to patch in.
/// </summary>
public class CustomLinkedRewardSet : CustomReward
{
    [CustomEnum] public static RewardType CustomLinkedRewardType;
    private const string SaveId = "baselib_customlinkedrewardset_children";
    
    private LocString HoverTipTitle => new("static_hover_tips", LinkedRewardType == LinkedRewardType.Bundled 
                ? "BASELIB-BUNDLED_REWARDS.title" : "BASELIB-EXCLUSIVE_REWARDS.title");
    private LocString HoverTipDesc => new("static_hover_tips", LinkedRewardType == LinkedRewardType.Bundled
                ? "BASELIB-BUNDLED_REWARDS.description" : "BASELIB-EXCLUSIVE_REWARDS.description");
    
    public HoverTip HoverTip => new(HoverTipTitle, HoverTipDesc);
    
    private static readonly SpireField<Reward, CustomLinkedRewardSet?> ParentCustomLinkedRewardSet = new(() => null);

    public static bool TryGetCustomLinkedRewardSet(Reward reward, [NotNullWhen(true)] out CustomLinkedRewardSet? customLinkedRewardSet)
    {
        customLinkedRewardSet = ParentCustomLinkedRewardSet.Get(reward);
        return customLinkedRewardSet != null;
    }
    
    
    private List<Reward> _rewards;
    public IReadOnlyList<Reward> Rewards => _rewards.ToList();
    
    protected override RewardType RewardType  => CustomLinkedRewardType;
    public override int RewardsSetIndex  => Rewards.Max(r => r.RewardsSetIndex);
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;
    public override LocString Description => new("gameplay_ui", "COMBAT_REWARD_LINKED");
    public override bool IsPopulated  => _rewards.All(r => r.IsPopulated);

    private LinkedRewardType _linkedRewardType;
    public LinkedRewardType LinkedRewardType => _linkedRewardType;
    
    private bool _selectionStarted = false;
    
    private Reward? _pendingExclusiveSelection;
    public void SetPendingExclusiveSelection(Reward chosen) => _pendingExclusiveSelection = chosen;

    
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
    
    private void RestoreRewards(List<Reward> rewards, LinkedRewardType linkedRewardType)
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

    
    protected override async Task<bool> OnSelect()
    {
        if (_selectionStarted) return true;
        _selectionStarted = true;
        
        if (LinkedRewardType == LinkedRewardType.Exclusive)
        {
            var chosen = _pendingExclusiveSelection;
            _pendingExclusiveSelection = null;
            if (chosen == null)
            {
                BaseLibMain.Logger.Error("No Reward selected. Should not happen!");
                return false;
            }

            if (!chosen.SuccessfullySelected)
                await chosen.SelectUnsynchronized();

            foreach (var reward in _rewards.ToList())
            {
                if (reward != chosen && !reward.SuccessfullySelected)
                    reward.OnSkipped();
            }
        }
        if(LinkedRewardType == LinkedRewardType.Bundled)
        {
            foreach (var reward in _rewards.ToList())
            {
                if (reward.SuccessfullySelected) continue;
                await reward.SelectUnsynchronized(); // we do only want local machine!
            }
        }
        
        return true;
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

    public void RemoveReward(Reward reward)
    {
        _rewards.Remove(reward);
    }
    
  

    
    // Builds an empty placeholder. 'ExtendedSaveHandlers' existing postfix on
    // Reward.FromSerializable calls our registered setter right after this returns,
    // which is what actually fills in _rewards via RestoreRewards.
    public static CustomReward CreateFromSerializable(SerializableReward save, Player player)
        => new CustomLinkedRewardSet([], player);


    public override void Initialize()
    {
        base.Initialize();
        ExtendedSaveTypes.RegisterObjectSaveType<SerializableCustomLinkedRewardData>(
                    ExtendedSaveTypes.PropertyFunc<SerializableCustomLinkedRewardData, List<SerializableReward>>(
                                nameof(SerializableCustomLinkedRewardData.Rewards)),
                    ExtendedSaveTypes.PropertyFunc<SerializableCustomLinkedRewardData, int>(
                                nameof(SerializableCustomLinkedRewardData.LinkedRewardTypeValue))
        );
        ExtendedSaveHandlers<Reward, SerializableReward>.RegisterSave<SerializableCustomLinkedRewardData>(
                    SaveId,
                    reward => reward is CustomLinkedRewardSet clrs
                                ? new SerializableCustomLinkedRewardData
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

public class SerializableCustomLinkedRewardData : IPacketSerializable
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
public static class CustomLinkedRewardSetPatches
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
public static class CustomLinkedRewardSetMultiplayerPatches
{
    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
    static class RedirectBundledNestedRewardSelection
    {
        // index must be top level reward, so we reroute to linked container
        [HarmonyPrefix]
        static bool Prefix(RewardsSetSynchronizer __instance, ref Task<bool> __result, ref Reward reward)
        {
            if (!CustomLinkedRewardSet.TryGetCustomLinkedRewardSet(reward, out var parent)) return true;
                
            if(parent.LinkedRewardType == LinkedRewardType.Bundled)
            {
                reward = parent;
                return true;
            }
            // We can not use the existing Synchronisation method for Exclusive Linked rewards.
            // The game only sends the index of the reward, no information about anything else.
            // Use custom message to bypass base games RewardSetSynchronizer completely in this case.
            if (parent.LinkedRewardType == LinkedRewardType.Exclusive)
            {
                var rewardStateForPlayer = __instance.GetRewardStateForPlayer(__instance.LocalPlayer);
                var rewardsSetState = rewardStateForPlayer.rewardsStack.Last();
                var containerIndex = rewardsSetState.set.Rewards.IndexOf(parent);
                var nestedIndex = parent.Rewards.ToList().IndexOf(reward);

                parent.SetPendingExclusiveSelection(reward);
                __result = __instance.SelectRewardForPlayer(rewardsSetState, parent); // apply locally immediately

                CustomMessageWrapper.Send(new ExclusiveLinkedRewardChoiceMessage
                {
                            setId = rewardsSetState.set.Id,
                            containerIndex = containerIndex,
                            nestedIndex = nestedIndex
                });
                return false;
            }
            return true;
        }
    }
}