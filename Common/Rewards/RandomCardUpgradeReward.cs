using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// Reward class for upgrading a random card from the deck.
/// Could technically be done at the end of combat, but maybe giving the player a choice on whether to upgrade is nice
/// </summary>
public sealed class RandomCardUpgradeReward(Player player) : CustomReward(player)
{
    private static string RewardIcon => ImageHelper.GetModImagePath("ui/reward_screen/reward_icon_card_upgrade_random.png");
    /// <inheritdoc/>
    protected override string IconPath => RewardIcon;
    // public static IEnumerable<string> AssetPaths => new List<string>([RewardIcon]);

    /// <inheritdoc/>
    public override LocString Description => new("gameplay_ui", "BASELIB-COMBAT_REWARD_RANDOM_CARD_UPGRADE");

    /// <summary>
    /// CustomEnum RewardType for this reward
    /// </summary>
    [CustomEnum] public static RewardType RandomCardUpgrade;
    /// <inheritdoc/>
    protected override RewardType RewardType => RandomCardUpgrade;

    /// <inheritdoc/>
    public override int RewardsSetIndex => 8;
    /// <inheritdoc/>
    public override bool IsPopulated => true;
    /// <inheritdoc/>
    public override void Populate() { }
    /// <inheritdoc/>
    public override void MarkContentAsSeen() { }

    /// <summary>
    /// No numbers in this one, so it can just create the object and not do anything to it
    /// </summary>
    public static RandomCardUpgradeReward CreateFromSerializable(SerializableReward save, Player player)
    {
        return new RandomCardUpgradeReward(player);
    }

    /// <inheritdoc/>
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;

    /// <inheritdoc/>
    protected override async Task<bool> OnSelect()
    {
        var cardsToUpgrade = PileType.Deck.GetPile(Player).Cards
            .Where(c => c.IsUpgradable).ToList()
            .StableShuffle(Player.RunState.Rng.Niche)
            .Take(1).ToList();
        if (cardsToUpgrade == null) { return false; }

        foreach (var card in cardsToUpgrade)
        {
            CardCmd.Upgrade(card);
        }

        return cardsToUpgrade.Count > 0;
    }
}
