using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// Reward class for upgrading one or more cards with a selection screen
/// </summary>
public sealed class CardUpgradeReward(Player player) : CustomReward(player)
{
    private static string RewardIcon => ImageHelper.GetModImagePath("ui/reward_screen/reward_icon_card_upgrade.png");
    /// <inheritdoc/>
    protected override string IconPath => RewardIcon;

    // MAYBE: This isn't used atm in mods, but there's potential for hooking up a preload hook like
    //      is seen in MegaCrit.Sts2.Core.Assets.AssetSets, though maybe it doesn't matter that much
    //      in this case specifically
    //public static IEnumerable<string> AssetPaths = [RewardIcon];

    /// <inheritdoc/>
    public override LocString Description
    {
        get
        {
            LocString locString = new("gameplay_ui", "BASELIB-COMBAT_REWARD_CARD_UPGRADE");
            locString.Add("cards", Amount);
            return locString;
        }
    }

    /// <summary>
    /// CustomEnum RewardType for this reward
    /// </summary>
    [CustomEnum] public static RewardType CardUpgrade;
    /// <inheritdoc/>
    protected override RewardType RewardType => CardUpgrade;

    /// <inheritdoc/>
    public override int RewardsSetIndex => 8;
    /// <inheritdoc/>
    public override bool IsPopulated => true;

    /// <summary>
    /// The maximum number of cards to choose to upgrade
    /// </summary>
    public required int Amount;

    /// <inheritdoc/>
    public override SerializableReward ToSerializable()
    {
        return new SerializableReward()
        {
            RewardType = CardUpgrade,
            GoldAmount = Amount // Hijacking the base values for now
        };
    }

    /// <summary>
    /// Static constructor to create the reward from the save file
    /// </summary>
    public static CardUpgradeReward CreateFromSerializable(SerializableReward save, Player player)
    {
        return new CardUpgradeReward(player)
        {
            Amount = save.GoldAmount // Hijacking the vanilla saved values
        };
    }

    /// <inheritdoc/>
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;

    /// <inheritdoc/>
    public override void Populate() { }
    /// <inheritdoc/>
    public override void MarkContentAsSeen() { }

    /// <inheritdoc/>
    protected override async Task<bool> OnSelect()
    {
        BaseLibMain.Logger.Debug($"Player {Player} Obtained targeted card upgrade from reward");
        return await RunManager.Instance.RewardSynchronizer.DoCardUpgrade(Player, Amount);
    }
}

static class CardUpgradeRewardExtensions
{
    extension(RewardSynchronizer rewardSynchronizer)
    {
        // We can get away with not sending a message in this case, as upgrading cards is already synced
        public async Task<bool> DoCardUpgrade(Player player, int amount = 1)
        {
            CardSelectorPrefs prefs = new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1, amount)
            {
                Cancelable = true,
                RequireManualConfirmation = true
            };

            List<CardModel> cards = (await CardSelectCmd.FromDeckForUpgrade(player, prefs)).ToList();

            // grid because horizontal is behind the reward overlay
            CardCmd.Upgrade(cards, CardPreviewStyle.GridLayout);

            return cards.Count > 0;
        }
    }
}
