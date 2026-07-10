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
    // Do not rename, move, touch, anything to this field. We use it deterministically through the hash algorithm.
    [CustomEnum] public static RewardType CustomLinkedRewardType;
    private const string SaveId = "baselib_customlinkedrewardset_children";
    
    private static LocString HoverTipTitle => new LocString("static_hover_tips", "LINKED_REWARDS.title");
    private static LocString HoverTipDesc => new LocString("static_hover_tips", "LINKED_REWARDS.description");

    private static readonly SpireField<Reward, CustomLinkedRewardSet?> ParentCustomLinkedRewardSet = new(() => null);

    public static bool TryGetCustomLinkedRewardSet(Reward reward, [NotNullWhen(true)] out CustomLinkedRewardSet? customLinkedRewardSet)
    {
        customLinkedRewardSet = ParentCustomLinkedRewardSet.Get(reward);
        return customLinkedRewardSet != null;
    }
    
    
    private List<Reward> _rewards;
    public IReadOnlyList<Reward> Rewards => _rewards.ToList();
    
    protected override RewardType RewardType  => CustomLinkedRewardType;
    public override int RewardsSetIndex  => Rewards.Max((Reward r) => r.RewardsSetIndex);
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;
    public override LocString Description => new LocString("gameplay_ui", "COMBAT_REWARD_LINKED");
    public override bool IsPopulated  => _rewards.All((Reward r) => r.IsPopulated);

    private LinkedRewardType _linkedRewardType;
    public LinkedRewardType LinkedRewardType => _linkedRewardType;
    
    private CustomLinkedRewardSet() : base(null!)
    {
        _rewards = [];
    }
    public CustomLinkedRewardSet(List<Reward> rewards, Player player, LinkedRewardType linkedRewardType = LinkedRewardType.Exclusive) : base(player)
    {
        _rewards = rewards;
        _linkedRewardType = linkedRewardType;
        foreach (var reward in _rewards)
            //reward.ParentRewardSet = this; // OLD
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
    
    
    // Called by the sideload setter below once the nested rewards have been
    // reconstructed from save data.
    internal void RestoreRewards(List<Reward> rewards, LinkedRewardType linkedRewardType)
    {
        _rewards = rewards;
        _linkedRewardType = linkedRewardType;
        foreach (var reward in _rewards)
            ParentCustomLinkedRewardSet.Set(reward, this);
    }

    // --- CustomReward hookup ---

    // Builds an empty placeholder; ExtendedSaveHandlers' existing postfix on
    // Reward.FromSerializable calls our registered setter right after this returns,
    // which is what actually fills in _rewards via RestoreRewards.
    public static CustomReward CreateFromSerializable(SerializableReward save, Player player)
        => new CustomLinkedRewardSet([], player);


    public override void Initialize()
    {
        base.Initialize(); // registers DeserializeMethod against CustomLinkedRewardType
        ExtendedSaveTypes.RegisterObjectSaveType<SerializableLinkedRewardData>(
                    ExtendedSaveTypes.PropertyFunc<SerializableLinkedRewardData, List<SerializableReward>>(
                                nameof(SerializableLinkedRewardData.Rewards)),
                    ExtendedSaveTypes.PropertyFunc<SerializableLinkedRewardData, int>(
                                nameof(SerializableLinkedRewardData.LinkedRewardTypeValue))
        );
        ExtendedSaveHandlers<Reward, SerializableReward>.RegisterSave<SerializableLinkedRewardData>(
                    SaveId,
                    getter: reward => reward is CustomLinkedRewardSet clrs
                                ? new SerializableLinkedRewardData
                                {
                                            Rewards = clrs.Rewards.Select(r => r.ToSerializable()).ToList(),
                                            LinkedRewardTypeValue  = (int)clrs.LinkedRewardType
                                }
                                : null,
                    setter: (reward, value) =>
                    {
                        if (reward is not CustomLinkedRewardSet clrs || value == null) return;
                        var restored = value.Rewards.Select(sr => Reward.FromSerializable(sr, reward.Player)).ToList();
                        clrs.RestoreRewards(restored, (LinkedRewardType)value.LinkedRewardTypeValue );
                    });
    }
}



public enum LinkedRewardType
{
    None,
    Exclusive,
    Bundled
}

// Small wrapper so a List<SerializableReward> (+ LinkedRewardType) can ride along
// as a sideloaded value via ExtendedSaveHandlers. Mirrors the pattern the base game
// uses for CombatRoom.ExtraRewards.
public class SerializableLinkedRewardData : IPacketSerializable
{
    public List<SerializableReward> Rewards { get; set; } = [];
    public int LinkedRewardTypeValue { get; set; } // stored as int, not the enum directly


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
    // TODO: Check what would happen. Eventually remove or replace with throwing an error
    [HarmonyPatch(typeof(NRewardButton), "SetReward")]
    [HarmonyPrefix]
    private static void WarnOfUndefinedBehaviour(Reward reward)
    {
        if(reward is CustomLinkedRewardSet)
            BaseLibMain.Logger.Warn($"The Reward inside an NRewardButton has been set to an object of \"CustomLinkedRewardSet\". This is undefined behaviour (base game would throw an error here). Proceed at your own risk.");
    }

    [HarmonyPatch(typeof(Reward), "HoverTips", MethodType.Getter)]
    [HarmonyPostfix]
    private static void AddHoverTip(Reward __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!CustomLinkedRewardSet.TryGetCustomLinkedRewardSet(__instance, out var customLinkedRewardSet)) return;
        __result = [.. __result, .. customLinkedRewardSet.HoverTips];
    }
}