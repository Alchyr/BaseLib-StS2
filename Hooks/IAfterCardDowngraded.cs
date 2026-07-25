using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Hooks;

/// <summary>
/// Interface for models that should know when a card is downgraded.
/// </summary>
public interface IAfterCardDowngraded
{
    /// <summary>
    /// Called after a card is downgraded.
    /// Should not trigger gameplay effects; will also occur on card inspection screen.
    /// If having gameplay effect is required, check that the card exists in a combat state or the player's deck,
    /// depending on what you're doing.
    /// <code>card == this</code> Will return false for the card displayed by
    /// card inspection.
    /// </summary>
    void AfterCardDowngraded(CardModel card);

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.DowngradeInternal))]
    private static class DowngradeHook
    {
        [HarmonyPostfix]
        private static void Patch(CardModel __instance)
        {
            var combatState = __instance.CombatState;
            var runState = __instance.Owner?.RunState ?? (combatState == null ? NullRunState.Instance : combatState.RunState);
            foreach (var item in runState.IterateHookListeners(combatState))
            {
                (item as IAfterCardDowngraded)?.AfterCardDowngraded(__instance);
            }
        }
    }
}