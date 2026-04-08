using Baselib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

public delegate T SerializableCustomReward<T>(SerializableReward save, Player player) where T : CustomReward;

public abstract class CustomReward(Player player) : Reward(player)
{
    /// <summary>
    /// Set the reward index after vanilla rewards by default
    /// </summary>
    public override int RewardsSetIndex => 9;

    public abstract SerializableCustomReward<CustomReward> SerializeMethod { get; }

    public virtual void Initialize()
    {
        if (SerializeMethod?.Method.IsStatic == true)
        {
            BaseLibMain.Logger.Info($"Registering CustomReward serializer for {GetType()}");
            CustomRewardPatches.RegisterCustomReward(RewardType, SerializeMethod);
        }
        else if (SerializeMethod != null)
        {
            throw new FieldAccessException($"Custom Reward {this.GetType()} has assigned a non-static method to SerializeMethod property");
        }
        else
        {
            throw new NotImplementedException($"Custom Reward {this.GetType()} has not implemented an Initialize() method to register a serializer for itself");
        }
    }

    // TODO: per-mod id prefixing for localisation
}

