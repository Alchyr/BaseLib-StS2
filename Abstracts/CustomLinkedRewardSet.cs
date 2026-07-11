using System.Diagnostics.CodeAnalysis;
using BaseLib.Patches.Content;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

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

    protected override Task<bool> OnSelect()
    {
        return Task.FromResult(result: true);
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


/// <summary>
/// Reward gain rules for the Linked Reward
/// </summary>
public enum LinkedRewardType
{
    /// <summary>
    /// Do not use
    /// </summary>
    None,
    /// <summary>
    /// You may only choose 1 option
    /// </summary>
    Exclusive,
    /// <summary>
    /// You either choose All or No options
    /// </summary>
    Bundled
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