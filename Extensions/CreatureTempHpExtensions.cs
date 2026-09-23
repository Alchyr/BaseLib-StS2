using BaseLib.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

/// <summary>Creature extensions for the Temporary HP mechanic. See <see cref="TempHpCmd" />.</summary>
public static class CreatureTempHpExtensions
{
    /// <summary>The creature's current Temporary HP (0 if none).</summary>
    public static int GetTempHp(this Creature creature)
    {
        return TempHpCmd.Get(creature);
    }

    /// <summary>Whether the creature currently has any Temporary HP.</summary>
    public static bool HasTempHp(this Creature creature)
    {
        return TempHpCmd.Get(creature) > 0;
    }

    /// <summary>Grant Temporary HP. See <see cref="TempHpCmd.Add" />.</summary>
    public static Task<int> AddTempHp(this Creature creature, PlayerChoiceContext choiceContext, int amount,
        Creature? applier = null, CardModel? cardSource = null)
    {
        return TempHpCmd.Add(choiceContext, creature, amount, applier, cardSource);
    }
}
