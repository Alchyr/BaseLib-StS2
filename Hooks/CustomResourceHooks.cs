using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
///     Hook for models that adjusts the amount you gain per turn of a
///     custom resource with a per-turn value (behaving like energy).
///     <see cref="BasicCustomResource"/> with its amount per turn set to a non-negative value, or
///     a custom implementation which would have to call this hook manually.
/// </summary>
public interface IModifyMaxResource<T> where T : CustomResource
{
    int ModifyResourceAmount(Player player, T resource, int max);
}

/// <summary>
/// Interface for a model that modifies a custom resource cost during combat.
/// Equivalent to basegame <see cref="AbstractModel.TryModifyEnergyCostInCombat"/>.
/// Used by <see cref="CustomResourceCost{T}.GetWithModifiers"/> through <see cref="BaseLibHooks.ModifyResourceCostInCombat"/>
/// </summary>
public interface IModifyResourceCostInCombat<T> where T : CustomResource
{
    /// <summary>
    /// Modify the amount of a resource that a card will cost during combat.
    /// This will never be called while outside of combat.
    /// </summary>
    /// <param name="card">Card whose cost we're modifying.</param>
    /// <param name="resource">The resource whose cost is being modified.</param>
    /// <param name="originalCost">Original cost.</param>
    /// <returns>The modified cost.</returns>
    public decimal ModifyResourceCostInCombat(
        CardModel card,
        T resource,
        decimal originalCost);
}

/// <summary>
/// Interface for a model that responds to a specific custom resource being spent.
/// <seealso cref="CustomResource.Spend"/>
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IAfterSpendResource<T> where T : CustomResource
{
    Task AfterSpendResource(ICombatState combatState, T resource, AbstractModel? spender, int amount);
}