using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace BaseLib.Patches.Content;

/// <summary>
/// Extensions to <see cref="RewardSynchronizer"/> to provide public getters to internal properties and common reward functions
/// </summary>
[HarmonyPatch(typeof(RewardSynchronizer))]
public static class RewardSynchronizerPatches
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
        public ulong SenderId;
        /// <summary>
        /// The message being sent
        /// </summary>
        public CustomTargetedMessageWrapper Message;
    }

    extension(RewardSynchronizer rewardSynchronizer)
    {
        // Not used in BaseLib as publicizer is enabled, but useful for mods without.
        /// <summary>
        /// Exposes the private INetGameService property.
        /// </summary>
        public INetGameService GameService() => rewardSynchronizer._gameService;

        /// <summary>
        /// Add a <see cref="CustomTargetedMessageWrapper"/> to the combat buffer
        /// </summary>
        public void BufferCustomRewardMessage(CustomTargetedMessageWrapper message, ulong senderId)
        {
            var bufferedMessage = new BufferedCustomRewardMessage
            {
                SenderId = senderId,
                Message = message
            };
            BufferedCustomRewardMessages[rewardSynchronizer]?.Add(bufferedMessage);
        }
    }

    /// <summary>
    /// Reference list of buffered messages<br/>
    /// </summary>
    internal static readonly SpireField<RewardSynchronizer, List<BufferedCustomRewardMessage>>
        BufferedCustomRewardMessages = new(() => []);

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    private static void OnCombatEndHandleCustomBufferedMessages(RewardSynchronizer __instance)
    {
        foreach (var bufferedMessage in BufferedCustomRewardMessages[__instance]!)
        {
            __instance._messageBuffer.CallHandlersOfType(bufferedMessage.Message.GetType(), bufferedMessage.Message, bufferedMessage.SenderId);
        }
        BufferedCustomRewardMessages[__instance]?.Clear();
    }
}
