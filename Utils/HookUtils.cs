using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

/// <summary>
///     Provides utility methods for dispatching and aggregating combat hook events
///     across all <see cref="ICombatState" /> hook listeners.
///     <para />
///     Hook interfaces should be implemented on <see cref="AbstractModel" /> subclasses
///     to be picked up by the listeners.
/// </summary>
public static class HookUtils
{
    /// <summary>
    ///     Dispatches an action to all hook listeners of type <typeparamref name="THook" />.
    ///     No-op when <paramref name="combatState" /> is <see langword="null" /> (outside combat).
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="action">The async action to invoke on each matching listener.</param>
    public static async Task Dispatch<THook>(ICombatState? combatState, Func<THook, Task> action)
        where THook : class
    {
        if (combatState == null) return;
        foreach (var model in combatState.IterateHookListeners().OfType<THook>())
            await action(model);
    }

    /// <summary>
    ///     Dispatches an action to all hook listeners of type <typeparamref name="THook" />,
    ///     pushing and popping each listener onto the provided <see cref="PlayerChoiceContext" />.
    ///     Silently skips listeners that are not <see cref="AbstractModel" /> instances.
    ///     <para>
    ///         No-op when <paramref name="combatState" /> is <see langword="null" />. Unlike
    ///         <see cref="DispatchWithContext{THook}" />, does not raise
    ///         <see cref="AbstractModel.InvokeExecutionFinished" /> — callers or listeners own that.
    ///     </para>
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="ctx">The player choice context to push/pop each model onto.</param>
    /// <param name="action">The async action to invoke on each matching listener.</param>
    public static async Task Dispatch<THook>(ICombatState? combatState, PlayerChoiceContext ctx,
        Func<THook, Task> action)
        where THook : class
    {
        if (combatState == null) return;
        foreach (var model in combatState.IterateHookListeners().OfType<THook>())
        {
            if (model is not AbstractModel abstractModel) continue;
            ctx.PushModel(abstractModel);
            await action(model);
            ctx.PopModel(abstractModel);
        }
    }

    /// <summary>
    ///     Dispatches an action to all hook listeners of type <typeparamref name="THook" /> in
    ///     <paramref name="player" />'s combat state, creating a
    ///     <see cref="HookPlayerChoiceContext" /> for each listener and awaiting its completion
    ///     or pause. Silently skips listeners that are not <see cref="AbstractModel" /> instances.
    ///     <para>
    ///         No-op when the player has no combat state (outside combat). Each context is
    ///         attributed to <paramref name="player" />'s <c>NetId</c> — the acting player, not
    ///         necessarily the local client. <see cref="AbstractModel.InvokeExecutionFinished" />
    ///         is raised for each listener after its action completes; listeners should not raise
    ///         it themselves.
    ///     </para>
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="player">
    ///     The player whose combat state supplies the listeners and whose identity the created
    ///     contexts run under.
    /// </param>
    /// <param name="action">
    ///     The async action to invoke on each matching listener, receiving that listener's
    ///     <see cref="PlayerChoiceContext" />.
    /// </param>
    public static async Task DispatchWithContext<THook>(Player player,
        Func<THook, PlayerChoiceContext, Task> action)
        where THook : class
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null) return;
        var netId = player.NetId;
        foreach (var model in combatState.IterateHookListeners().OfType<THook>())
        {
            if (model is not AbstractModel abstractModel) continue;
            var hookCtx = new HookPlayerChoiceContext(abstractModel, netId, combatState, GameActionType.Combat);
            var task = action(model, hookCtx);
            await hookCtx.AssignTaskAndWaitForPauseOrCompletion(task);
            abstractModel.InvokeExecutionFinished();
        }
    }

    /// <summary>
    ///     Aggregates a value across all hook listeners of type <typeparamref name="THook" />,
    ///     passing each listener and the current accumulated value to the provided function.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <typeparam name="TResult">The type of the accumulated result.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="initial">The initial value for the aggregation.</param>
    /// <param name="action">A function that takes a listener and the current value and returns the new value.</param>
    /// <returns>The final aggregated value after all listeners have been processed.</returns>
    public static TResult Aggregate<THook, TResult>(ICombatState combatState, TResult initial,
        Func<THook, TResult, TResult> action)
        where THook : class
    {
        return combatState.IterateHookListeners().OfType<THook>()
            .Aggregate(initial, (current, model) => action(model, current));
    }

    /// <summary>
    ///     Returns <see langword="true" /> if all hook listeners of type <typeparamref name="THook" />
    ///     satisfy the given predicate.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="predicate">The condition to test each listener against.</param>
    public static bool All<THook>(ICombatState combatState, Func<THook, bool> predicate)
        where THook : class
    {
        return combatState.IterateHookListeners().OfType<THook>().All(predicate);
    }

    /// <summary>
    ///     Returns <see langword="true" /> if all hook listeners of type <typeparamref name="THook" />
    ///     satisfy the given predicate, additionally collecting the listeners that failed it.
    ///     Vacuously <see langword="true" /> when no listeners of the type exist.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="predicate">The condition to test each listener against.</param>
    /// <param name="nonMatches">
    ///     The listeners that did <b>not</b> satisfy the predicate; empty when the result is
    ///     <see langword="true" />.
    /// </param>
    public static bool All<THook>(ICombatState combatState, Func<THook, bool> predicate,
        out IEnumerable<THook> nonMatches)
        where THook : class
    {
        var list = combatState.IterateHookListeners().OfType<THook>().Where(m => !predicate(m)).ToList();
        nonMatches = list;
        return list.Count == 0;
    }

    /// <summary>
    ///     Returns <see langword="true" /> if any hook listener of type <typeparamref name="THook" />
    ///     satisfies the given predicate.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="predicate">The condition to test each listener against.</param>
    public static bool Any<THook>(ICombatState combatState, Func<THook, bool> predicate)
        where THook : class
    {
        return combatState.IterateHookListeners().OfType<THook>().Any(predicate);
    }

    /// <summary>
    ///     Returns <see langword="true" /> if any hook listener of type <typeparamref name="THook" />
    ///     satisfies the given predicate, additionally collecting all listeners that matched.
    ///     Unlike LINQ <c>Any</c>, this does not short-circuit — the predicate runs for every listener.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="predicate">The condition to test each listener against.</param>
    /// <param name="matches">
    ///     All listeners that satisfied the predicate; empty when the result is
    ///     <see langword="false" />.
    /// </param>
    public static bool Any<THook>(ICombatState combatState, Func<THook, bool> predicate,
        out IEnumerable<THook> matches)
        where THook : class
    {
        var list = combatState.IterateHookListeners().OfType<THook>().Where(predicate).ToList();
        matches = list;
        return list.Count > 0;
    }

    /// <summary>
    ///     Passes a value through all hook listeners of type <typeparamref name="THook" />,
    ///     tracking which listeners changed it.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <typeparam name="TValue">The type of the value being modified. Must implement <see cref="IEquatable{T}" />.</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="originalAmount">The initial value before any modifications.</param>
    /// <param name="amountModifier">A function that takes a listener and the current value and returns the modified value.</param>
    /// <param name="modifiers">
    ///     Outputs the listeners whose call changed the value they received (per-step
    ///     <typeparamref name="TValue" /> equality). Listeners returning their input unchanged are
    ///     excluded; listeners whose changes later cancel out are <b>included</b>, so this set can
    ///     be non-empty even when the returned value equals <paramref name="originalAmount" />.
    /// </param>
    /// <returns>
    ///     The final modified value. When <paramref name="combatState" /> is
    ///     <see langword="null" />, returns <paramref name="originalAmount" /> with an empty
    ///     <paramref name="modifiers" /> set.
    /// </returns>
    public static TValue Modify<THook, TValue>(
        ICombatState? combatState,
        TValue originalAmount,
        Func<THook, TValue, TValue> amountModifier,
        out IEnumerable<THook> modifiers)
        where THook : class
        where TValue : IEquatable<TValue>
    {
        if (combatState == null)
        {
            modifiers = [];
            return originalAmount;
        }
        var amount = originalAmount;
        var abstractModelList = new List<THook>();
        foreach (var model in combatState.IterateHookListeners().OfType<THook>())
        {
            var previous = amount;
            amount = amountModifier.Invoke(model, amount);
            if (!previous.Equals(amount))
                abstractModelList.Add(model);
        }

        modifiers = abstractModelList;
        return amount;
    }

    /// <summary>
    ///     Invokes a follow-up action on the listeners that previously modified a value via
    ///     <see cref="Modify{THook,TValue}" />, iterating in current hook-listener order (not the
    ///     order of <paramref name="modifiers" />). Listeners no longer present in the combat
    ///     state's iteration are silently skipped.
    ///     <see cref="AbstractModel.InvokeExecutionFinished" /> is raised after each action for
    ///     listeners that are <see cref="AbstractModel" /> instances; implementations should not
    ///     raise it themselves.
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <param name="cs">The current combat state to iterate listeners from.</param>
    /// <param name="modifiers">
    ///     The set of listeners that modified the value, as returned by
    ///     <see cref="Modify{THook,TValue}" />.
    /// </param>
    /// <param name="action">The async action to invoke on each modifier.</param>
    public static async Task AfterModifying<THook>(ICombatState cs, IEnumerable<THook> modifiers,
        Func<THook, Task> action)
        where THook : class
    {
        var modifierSet = new HashSet<THook>(modifiers);
        foreach (var iterateHookListener in cs.IterateHookListeners().OfType<THook>())
        {
            if (!modifierSet.Contains(iterateHookListener)) continue;
            await action(iterateHookListener);
            if (iterateHookListener is AbstractModel model)
                model.InvokeExecutionFinished();
        }
    }

    /// <summary>
    ///     Presents a mutable <paramref name="value" /> to all hook listeners of type
    ///     <typeparamref name="THook" /> for in-place modification. Every listener is invoked
    ///     exactly once (the enumeration is fully materialized); each returns
    ///     <see langword="true" /> to declare that it modified the value.
    ///     <para>
    ///         Unlike <see cref="Modify{THook,TValue}" />, modification tracking is
    ///         <b>self-reported</b> — nothing verifies the value actually changed, so listeners
    ///         must return honestly or the <see cref="AfterModifying{THook}" /> follow-up will
    ///         target the wrong set. The same <paramref name="value" /> instance is returned;
    ///         it is passed back for call-site fluency, not copied.
    ///     </para>
    /// </summary>
    /// <typeparam name="THook">The hook interface to filter listeners by.</typeparam>
    /// <typeparam name="TValue">The mutable type being modified in place (typically a class or collection).</typeparam>
    /// <param name="combatState">The current combat state to iterate listeners from.</param>
    /// <param name="value">The instance listeners may mutate.</param>
    /// <param name="amountModifier">
    ///     Invoked per listener with the shared instance; returns whether this listener modified it.
    /// </param>
    /// <param name="modifiers">The listeners that reported modifying the value.</param>
    /// <returns>The same <paramref name="value" /> instance, after all listeners ran.</returns>
    public static TValue ModifyMutable<THook, TValue>(
        ICombatState combatState,
        TValue value,
        Func<THook, TValue, bool> amountModifier,
        out IEnumerable<THook> modifiers)
        where THook : class
    {
        var list = combatState.IterateHookListeners().OfType<THook>()
            .Where(model => amountModifier.Invoke(model, value)).ToList();
        modifiers = list;
        return value;
    }
}