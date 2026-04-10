using BaseLib.Abstracts;
using BaseLib.Common.Rewards;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

/// <summary>
/// Extensions to <see cref="RewardSynchronizer"/> to provide public getters to internal properties and common reward functions
/// </summary>
[HarmonyPatch(typeof(RewardSynchronizer))]
public static class RewardSynchronizerExtensions
{
    /// <summary>
    /// Struct to save a custom reward message until combat ends
    /// Prefer creating with <see cref="BufferCustomRewardMessage"/>
    /// </summary>
    public struct BufferedCustomRewardMessage
    {
        /// <summary>
        /// the id of the player who sent the message
        /// </summary>
        public ulong senderId;
        /// <summary>
        /// The message being sent
        /// </summary>
        public CustomRewardMessage message;
    }

    /// <summary>
    /// Reference list of buffered messages<br/>
    /// Hopefully there is only ever one instance of <see cref="RewardSynchronizer"/> at a time on each client?
    /// </summary>
    internal static List<BufferedCustomRewardMessage> _bufferedCustomRewardMessages = [];

    extension(RewardSynchronizer rewardSynchronizer)
    {

        internal List<BufferedCustomRewardMessage> BufferedCustomRewardMessages { get { return _bufferedCustomRewardMessages; } }

        /// <summary>
        /// Add a <see cref="CustomRewardMessage"/> to the combat buffer
        /// </summary>
        public void BufferCustomRewardMessage(CustomRewardMessage message, ulong senderId)
        {
            var bufferedMessage = new BufferedCustomRewardMessage
            {
                senderId = senderId,
                message = message
            };
            rewardSynchronizer.BufferedCustomRewardMessages.Add(bufferedMessage);
        }

        /// <summary>
        /// Exposes the private LocalPlayer property from <seealso cref="RewardSynchronizer"/>
        /// </summary>
        public Player? LocalPlayerRef => rewardSynchronizer._playerCollection.GetPlayer(rewardSynchronizer._localPlayerId);
        /// <summary>
        /// Exposes the private IPlayerCollection property
        /// </summary>
        public IPlayerCollection? PlayerCollection => rewardSynchronizer._playerCollection;
        /// <summary>
        /// Exposes the private RunLocationTargetedMessageBuffer property
        /// </summary>
        public RunLocationTargetedMessageBuffer? MessageBuffer => rewardSynchronizer._messageBuffer;
        /// <summary>
        /// Exposes the private INetGameService property
        /// </summary>
        public INetGameService? GameService => rewardSynchronizer._gameService;


        /// <summary>
        /// Method to handle transforming a card as a combat reward
        /// </summary>
        public async Task<bool> DoLocalCardTransform(int amount = 1, bool upgrade = false)
        {
            CardTransformRewardMessage message = new CardTransformRewardMessage
            {
                Location = rewardSynchronizer.MessageBuffer!.CurrentLocation,
                wasSkipped = false,
                Upgrade = upgrade,
                Amount = amount
            };
            BaseLibMain.Logger.Debug($"Transforming card for local player {rewardSynchronizer.LocalPlayerRef}");

            rewardSynchronizer.GameService?.SendMessage(message);
            return await rewardSynchronizer.DoCardTransform(rewardSynchronizer.LocalPlayerRef!, amount, upgrade);
        }

        /// <summary>
        /// Transform a card for a specific player as a combat reward
        /// </summary>
        public async Task<bool> DoCardTransform(Player player, int amount = 1, bool upgrade = false)
        {
            CardSelectorPrefs prefs = new CardSelectorPrefs(new LocString("gameplay_ui", "COMBAT_REWARD_CARD_TRANSFORM"), amount)
            {
                Cancelable = true,
                RequireManualConfirmation = true
            };

            List<CardModel> cards = (await CardSelectCmd.FromDeckForTransformation(player, prefs)).ToList();

            BaseLibMain.Logger.Debug($"Current combat state for transform rewards is: IsEnding={CombatManager.Instance.IsEnding}");
            foreach (CardModel card in cards)
            {
                CardModel newCard = CardFactory.CreateRandomCardForTransform(card, isInCombat: false, player.RunState.Rng.Niche);

                if (upgrade || card.IsUpgraded) // need a more robust handler for multi-upgrade at some point
                {
                    CardCmd.Upgrade(newCard);
                }

                await CardCmd.Transform(card, newCard);
                BaseLibMain.Logger.Debug($"Player {player.NetId} transformed {card.Id} in their deck into {newCard.Id}" + (upgrade ? " and upgraded it." : "."));
            }

            return cards.Count > 0;
        }
    }

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    private static void HandleCustomBufferedMessages(RewardSynchronizer __instance)
    {
        foreach (BufferedCustomRewardMessage bufferedMessage in __instance.BufferedCustomRewardMessages)
        {
            __instance.MessageBuffer?.CallHandlersOfType(bufferedMessage.message.GetType(), bufferedMessage.message, bufferedMessage.senderId);
        }
        __instance.BufferedCustomRewardMessages.Clear();
    }
}
