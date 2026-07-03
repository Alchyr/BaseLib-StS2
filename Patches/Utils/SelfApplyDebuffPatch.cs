using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Utils;

/// <summary>
/// If player applies a debuff to self or ally before turn ends (not from curse cards like doubt),
/// the debuff should tick it's duration at enemy's turn ends
/// </summary>
[HarmonyPatch]
public static class SelfApplyDebuffPatch
{
    static MethodBase TargetMethod()
    {
        var targetMethod =
            AccessTools.DeclaredMethod(typeof(PowerCmd), "Apply", 
                [typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)]);
        if (targetMethod == null)
            targetMethod =
                AccessTools.DeclaredMethod(typeof(PowerCmd), "Apply",
                    [typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)]);

        return targetMethod;
    }
    
    [HarmonyPostfix]
    static void Postfix(ref Task __result, PowerModel power, Creature target, Creature? applier, CardModel? cardSource)
    {
        // Replace original Task with wrapped Task
        __result = WrappedApplyTask(__result, power, target, applier, cardSource);
    }

    static async Task WrappedApplyTask(Task originalTask, PowerModel power, Creature target, Creature? applier, CardModel? cardSource)
    {
        await originalTask;

        // At this point, all the logic in the original function has been executed, including the part that sets SkipNextDurationTick to true.
        if (target.CombatState?.CurrentSide == CombatSide.Player 
            && target.Side == CombatSide.Player && power is { Type: PowerType.Debuff, Applier.Side: CombatSide.Player }
            && (power is ICustomModel || power.Applier?.Monster is ICustomModel || power.Applier?.Player?.Character is ICustomModel || cardSource is ICustomModel))
        {
            // Ensure player-applied debuffs on self/allies tick at current (enemy's) turn end
            power.SkipNextDurationTick = false;
        }
    }
}