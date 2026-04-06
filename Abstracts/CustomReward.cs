using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;

namespace BaseLib.Abstracts;

public abstract class CustomReward(Player player) : Reward(player)
{
    /// <summary>
    /// Set the reward index after vanilla rewards by default
    /// </summary>
    public override int RewardsSetIndex => 9;

    // TODO: per-mod id prefixing for localisation
}
