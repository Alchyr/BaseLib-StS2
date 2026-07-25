using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Features;

/// <summary>
/// <see cref="CardCmd.AutoPlay"/> only randomizes a target for the vanilla <see cref="TargetType.AnyEnemy"/>
/// and <see cref="TargetType.AnyAlly"/> types, so a card with a custom single-target type that is
/// auto-played without a target ends up played with a null target and throws. This fills in the
/// missing fallback by picking a random valid target using the registered predicate.
/// <para/>
/// Fully defensive: it never blocks the original method, and any problem leaves the target untouched
/// so the vanilla path runs exactly as before.
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]
internal static class AutoPlayCustomTargetPatch
{
    [HarmonyPrefix]
    private static void PickRandomCustomTarget(CardModel card, ref Creature? target)
    {
        try
        {
            // Only step in when there is no target and the type is a registered custom single-target.
            if (target != null || card == null) return;
            if (!CustomTargetType.SingleTargeting.TryGetValue(card.TargetType, out var canTarget) || canTarget == null)
                return;

            var player = card.Owner;
            if (player?.RunState?.Rng == null) return;

            var combatState = card.CombatState;
            if (combatState == null) return;

            var candidates = combatState.Creatures
                .Where(creature => creature.IsAlive && canTarget(creature, player))
                .ToList();
            if (candidates.Count == 0) return;

            target = player.RunState.Rng.CombatTargets.NextItem(candidates);
        }
        catch (Exception e)
        {
            // Swallow everything: leaving target unchanged simply runs the original AutoPlay logic.
            BaseLibMain.Logger.Warn($"AutoPlay custom-target fallback failed; using vanilla behavior. {e}");
        }
    }
}
