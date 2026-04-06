using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RewardSynchronizer))]
public static class RewardSynchronizerExtensions
{
    /// <summary>
    /// Struct to save a custom reward message until combat ends
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
    /// Reference list of buffered messages
    /// </summary>
    public static List<BufferedCustomRewardMessage> BufferedCustomRewardMessages = [];

    /// <summary>
    /// Exposes the private LocalPlayer property from <seealso cref="RewardSynchronizer"/>
    /// </summary>
    public static Player? LocalPlayerRef(this RewardSynchronizer rewardSynchronizer) => rewardSynchronizer._playerCollection.GetPlayer(rewardSynchronizer._localPlayerId);

    internal static readonly List<Type> _rewardMessageCache = [..ReflectionHelper.GetSubtypesInMods<CustomRewardMessage>()];

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    internal static void OnCombatEndedHandleCustomBufferedMessages()
    {
        foreach (BufferedCustomRewardMessage bufferedMessage in BufferedCustomRewardMessages)
        {
            bufferedMessage.message.MessageHandler(bufferedMessage.message, bufferedMessage.senderId);
        }
        BufferedCustomRewardMessages.Clear();
    }
}
