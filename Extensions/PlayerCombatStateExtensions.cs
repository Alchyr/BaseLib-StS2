using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BaseLib.Extensions;

public static class PlayerCombatStateExtensions
{
    /// <summary>
    /// Retrieve a custom resource for a given PlayerCombatState.
    /// </summary>
    public static T GetResource<T>(this PlayerCombatState playerCombatState) where T : CustomResource, new()
    {
        return CustomResources<T>.Get(playerCombatState);
    }
}