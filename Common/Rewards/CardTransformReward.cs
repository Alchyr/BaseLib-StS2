using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// A reward class similar to the card removal one created by <see cref="ForbiddenGrimoire"/>,
/// only for transforming instead of removing cards
/// </summary>
/// <example>
/// In a relic or power's <c>AfterCombatEnd</c> override
/// <code>
/// room.AddExtraReward(Owner.Player, new CardTransformReward(Owner.Player) {Amount = Amount, Upgrade = true});
/// </code> </example>
public sealed class CardTransformReward(Player player) : CustomReward(player)
{
    /// <summary>
    /// A new <see cref="RewardType"/> defined with the <see cref="CustomEnumAttribute"/> attribute
    /// </summary>
    [CustomEnum] public static RewardType CardTransform;

    /// <summary>
    /// Reference to the <see cref="RewardType"/> <see cref="CardTransform"/> defined earlier
    /// </summary>
    protected override RewardType RewardType => CardTransform;

    /// <summary>
    /// Whether the card rewards should be upgraded or not
    /// </summary>
    public required bool Upgrade;
    /// <summary>
    /// How many cards can be selected in this reward screen
    /// </summary>
    public required int Amount;

    /// <summary>
    /// The description to show in the reward screen,
    /// switches based on whether the reward will upgrade the transformed cards
    /// </summary>
    public override LocString Description
    {
        get
        {
            LocString locString = new LocString("gameplay_ui", "BASELIB-COMBAT_REWARD_CARD_TRANSFORM");
            locString.Add("cards", Amount);
            locString.Add("Upgrade", Upgrade);
            return locString;
        }
    }
    /// <inheritdoc/>
    public override bool IsPopulated => true;
    public static string RewardIcon => ImageHelperExtensions.GetModImagePath("ui/reward_screen/reward_icon_card_transform.png");
    /// <inheritdoc/>
    protected override string IconPath => RewardIcon;


    /// <summary>
    /// Serializing the reward, saving whether to upgrade and how many cards to transform in the vanilla fields
    /// </summary>
    public override SerializableReward ToSerializable()
    {
        return new SerializableReward()
        {
            RewardType = CardTransform,
            GoldAmount = Amount,
            WasGoldStolenBack = Upgrade
        };
    }

    /// <summary>
    /// Recreates the reward from the saved <see cref="SerializableReward"/>
    /// </summary>
    /// <param name="save">The <see cref="SerializableReward"/> that was created and saved from
    /// <see cref="ToSerializable"/></param>
    /// <param name="player">The <see cref="Player"/> the reward belongs to</param>
    public static CardTransformReward CreateFromSerializable(SerializableReward save, Player player)
    {
        return new CardTransformReward(player) {
            // hijacking the gold amounts as a temp hack before worrying about extending the serialized values
            Amount = save.GoldAmount,
            Upgrade = save.WasGoldStolenBack
        };
    }

    /// <inheritdoc/>
    public override CreateRewardFromSave<CustomReward> DeserializeMethod => CreateFromSerializable;

    /// <inheritdoc/>
    public override void MarkContentAsSeen() { }
    /// <inheritdoc/>
    public override void Populate() {}

    /// <inheritdoc/>
    protected override async Task<bool> OnSelect()
    {
        BaseLibMain.Logger.Info("Obtained card transformation from reward");
        return await RunManager.Instance.RewardSynchronizer.DoUnsyncedCardTransform(Player, Amount, true);
    }
}

static class TransformRewardSynchronizerPatches
{
    extension(RewardSynchronizer rewardSynchronizer)
    {
        /// <summary>
        /// Transform a card for a specific player as a combat reward
        /// This is allowed to be called without sending a message becasue transforming already sends it's own one
        /// </summary>
        public async Task<bool> DoUnsyncedCardTransform(Player player, int amount = 1, bool upgrade = false)
        {
            var loc = upgrade ? CardSelectorPrefsExtensions.TransformAndUpgradeSelectionPrompt : CardSelectorPrefs.TransformSelectionPrompt;
            CardSelectorPrefs prefs = new CardSelectorPrefs(loc, 1, amount)
            {
                Cancelable = true,
                RequireManualConfirmation = true
            };

            List<CardModel> cards = (await CardSelectCmd.FromDeckForTransformation(player, prefs)).ToList();

            BaseLibMain.Logger.Debug($"Current combat state for transform rewards is: IsEnding={CombatManager.Instance.IsEnding}");
            foreach (CardModel card in cards)
            {
                CardModel newCard = CardFactory.CreateRandomCardForTransform(card, isInCombat: false, player.RunState.Rng.Niche);

                // MAYBE: potentially add a toggle for keeping upgrade state;
                // and a more robust handler for multi-upgrade cards/upgrading more than once?
                if (upgrade)
                {
                    CardCmd.Upgrade(newCard);
                }

                // grid because horizontal is behind the reward screen overlay
                await CardCmd.Transform(card, newCard, CardPreviewStyle.GridLayout);
                BaseLibMain.Logger.Debug($"Player {player.NetId} transformed {card.Id} in their deck into {newCard.Id}" + (upgrade ? " and upgraded it." : "."));
            }

            // Bool return decides whether the reward is "consumed" (disappears from the list)
            return cards.Count > 0;
        }
    }
}
