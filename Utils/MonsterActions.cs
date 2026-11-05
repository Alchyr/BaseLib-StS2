using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

public static class MonsterActions
{
    /// <summary>
    /// Sets up an attack command for a monster.
    /// </summary>
    public static AttackCommand Attack(MonsterModel monster, int baseDmg, int hitCount = 1)
    {
        var result = new AttackCommand(baseDmg)
            .FromMonster(monster);
        if (hitCount != 1)
            result.WithHitCount(hitCount);
        return result;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the calling monster.
    /// </summary>
    public static async Task<T?> ApplySelf<T>(MonsterModel monster, decimal amount, PlayerChoiceContext? context = null, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(context ?? new ThrowingPlayerChoiceContext(), monster.Creature, amount, monster.Creature, null, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the provided targets.
    /// </summary>
    public static async Task<IReadOnlyList<T>> Apply<T>(MonsterModel monster, decimal amount, IEnumerable<Creature> targets, PlayerChoiceContext? context = null, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(context ?? new ThrowingPlayerChoiceContext(), targets, amount, monster.Creature, null, silent);
    }
}