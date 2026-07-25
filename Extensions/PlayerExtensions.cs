using System.Diagnostics.CodeAnalysis;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class PlayerExtensions
{
    /// <summary>
    /// Shortcut to player.Creature.HasPower.
    /// </summary>
    public static bool HasPower<T>(this Player player) where T : PowerModel
    {
        return player.Creature.HasPower<T>();
    }

    /// <summary>
    /// Retrieve a specific relic from a player, if they have it.
    /// Alternative to <see cref="Player.GetRelic"/>.
    /// </summary>
    public static bool TryGetRelic<T>(this Player player, [NotNullWhen(true)] out T? relic) where T : RelicModel
    {
        relic = player.GetRelic<T>();
        return relic != null;
    }
    
    /// <summary>
    /// Retrieve a custom resource from a given player's PlayerCombatState, null if combat state is null.
    /// </summary>
    public static T? GetResource<T>(this Player player) where T : CustomResource, new()
    {
        CustomResources<T>.TryGet(player.PlayerCombatState, out var result);
        return result;
    }
}