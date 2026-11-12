using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

/// <summary>
///     Provides utility methods for dispatching and aggregating combat hook events
///     across all <see cref="ICombatState" /> hook listeners.
///     <para />
///     Hook interfaces should be implemented on <see cref="AbstractModel" /> subclasses
///     to be picked up by the listeners.
///     <para />
///     Every dispatch and value-folding loop invokes each listener <b>in isolation</b>: a listener
///     that throws is logged through <c>BaseLibMain.Logger.Error</c> (naming the listener's full
///     type and the mod that defined it) and its exception is swallowed, so the remaining
///     listeners still run and one mod's bug cannot abort a hook or the run.
///     <see cref="OperationCanceledException" /> is the one exception that is <b>not</b> swallowed:
///     it signals the game tearing the action down rather than a buggy listener, so it keeps
///     propagating after the choice-context stack is restored.
///     <para />
///     The boolean gates <c>All</c> and <c>Any</c> deliberately do <b>not</b> isolate: a throwing
///     listener has no neutral truth value to contribute to an all/any answer, so their predicates
///     still propagate to the caller.
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
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
            await InvokeSafely(model, action);
    }

    /// <summary>
    ///     Dispatches an action to all combat hook listeners of type <typeparamref name="THook" />,
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
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
        {
            if (model is not AbstractModel abstractModel) continue;
            ctx.PushModel(abstractModel);
            try
            {
                await InvokeSafely(model, action);
            }
            finally
            {
                // The pop must run even when the listener throws (including the cancellation that
                // InvokeSafely rethrows), otherwise a failed listener stays on top of the stack and
                // every later player choice is attributed to it.
                ctx.PopModel(abstractModel);
            }
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
    ///         is raised for each listener after its action completes, and also after one that
    ///         threw (the throw is logged and swallowed, see <see cref="HookUtils" />); listeners
    ///         should not raise it themselves.
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
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
        {
            if (model is not AbstractModel abstractModel) continue;
            var hookCtx = new HookPlayerChoiceContext(abstractModel, netId, combatState, GameActionType.Combat);
            // AssignTaskAndWaitForPauseOrCompletion both stores the task on the context and waits for
            // it to complete or pause; its internal TaskHelper.WhenAny rethrows a faulted listener,
            // which InvokeSafely catches.
            await InvokeSafely(model, m => hookCtx.AssignTaskAndWaitForPauseOrCompletion(action(m, hookCtx)));
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
    /// <returns>
    ///     The final aggregated value after all listeners have been processed. A listener that
    ///     throws is logged and skipped: the accumulated value keeps what the previous listeners
    ///     produced.
    /// </returns>
    public static TResult Aggregate<THook, TResult>(ICombatState combatState, TResult initial,
        Func<THook, TResult, TResult> action)
        where THook : class
    {
        var result = initial;
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
        {
            try
            {
                result = action(model, result);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A listener that throws contributes nothing: the accumulator keeps the value it
                // had before this listener ran.
                LogListenerFailure(model, e);
            }
        }

        return result;
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
        return Hook.IterateCombatHookListeners(combatState).OfType<THook>().All(predicate);
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
        var list = Hook.IterateCombatHookListeners(combatState).OfType<THook>().Where(m => !predicate(m)).ToList();
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
        return Hook.IterateCombatHookListeners(combatState).OfType<THook>().Any(predicate);
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
        var list = Hook.IterateCombatHookListeners(combatState).OfType<THook>().Where(predicate).ToList();
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
    ///     <para>
    ///         A listener that throws is logged and skipped: the accumulated value keeps what the
    ///         previous listeners produced, and the thrown listener is <b>not</b> added to
    ///         <paramref name="modifiers" />.
    ///     </para>
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
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
        {
            var previous = amount;
            try
            {
                amount = amountModifier.Invoke(model, amount);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A listener that throws leaves the accumulated value untouched and is not recorded
                // as a modifier, so AfterModifying will not call it back.
                LogListenerFailure(model, e);
                continue;
            }
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
    ///     listeners that are <see cref="AbstractModel" /> instances, including actions that threw
    ///     (the throw is logged and swallowed, see <see cref="HookUtils" />); implementations should
    ///     not raise it themselves.
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
        foreach (var iterateHookListener in Hook.IterateCombatHookListeners(cs).OfType<THook>())
        {
            if (!modifierSet.Contains(iterateHookListener)) continue;
            await InvokeSafely(iterateHookListener, action);
            if (iterateHookListener is AbstractModel model)
                model.InvokeExecutionFinished();
        }
    }

    /// <summary>
    ///     Presents a mutable <paramref name="value" /> to all hook listeners of type
    ///     <typeparamref name="THook" /> for in-place modification. Every listener is invoked
    ///     exactly once, in order, with no short-circuiting; each returns
    ///     <see langword="true" /> to declare that it modified the value.
    ///     <para>
    ///         Unlike <see cref="Modify{THook,TValue}" />, modification tracking is
    ///         <b>self-reported</b> — nothing verifies the value actually changed, so listeners
    ///         must return honestly or the <see cref="AfterModifying{THook}" /> follow-up will
    ///         target the wrong set. The same <paramref name="value" /> instance is returned;
    ///         it is passed back for call-site fluency, not copied.
    ///     </para>
    ///     <para>
    ///         A listener that throws is logged and treated as "did not modify": it is not added to
    ///         <paramref name="modifiers" />. Any mutation it made to the shared instance before
    ///         throwing cannot be rolled back.
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
        var list = new List<THook>();
        foreach (var model in Hook.IterateCombatHookListeners(combatState).OfType<THook>())
        {
            try
            {
                if (amountModifier.Invoke(model, value))
                    list.Add(model);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A listener that throws is treated as "did not modify": it is not registered, so
                // AfterModifying will not call it back. Any partial mutation it already made to the
                // shared instance stays, which is why the log has to name it.
                LogListenerFailure(model, e);
            }
        }

        modifiers = list;
        return value;
    }

    /// <summary>
    ///     Invokes <paramref name="action" /> for a single listener, isolating it from the rest of
    ///     the dispatch: a listener that throws is logged and skipped instead of aborting the hook.
    ///     <para>
    ///         The exception is deliberately <b>not</b> rethrown: one mod's bug must not stop the
    ///         remaining listeners, abort the dispatch, or crash the run. The trade-off is that a
    ///         broken listener silently does less than it intends; the error log (naming the
    ///         listener and its mod) is the only signal, so it must stay loud and specific.
    ///     </para>
    ///     <para>
    ///         <see cref="OperationCanceledException" /> is the single exception that is rethrown:
    ///         it means the game is tearing the action down, not that a listener is buggy, and
    ///         continuing to run listeners on a cancelled combat would act on dead state.
    ///     </para>
    /// </summary>
    private static async Task InvokeSafely<THook>(THook listener, Func<THook, Task> action)
        where THook : class
    {
        try
        {
            await action(listener);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogListenerFailure(listener, e);
        }
    }

    /// <summary>
    ///     Logs a listener exception swallowed by <see cref="InvokeSafely{THook}" />, naming the hook
    ///     interface, the listener's full type name and the mod that defined it, and stating
    ///     explicitly that the exception was swallowed on purpose.
    /// </summary>
    private static void LogListenerFailure<THook>(THook listener, Exception e)
        where THook : class
    {
        var type = listener.GetType();
        BaseLibMain.Logger.Error(
            $"[HookUtils] Swallowed an exception thrown by {HookName<THook>()} listener " +
            $"{type.FullName ?? type.Name} from {DescribeOwner(type)}. " +
            "The remaining listeners still run and the exception is not rethrown, so one mod's bug " +
            $"cannot abort the hook or the run.\n{e}");
    }

    /// <summary>
    ///     Human-readable owner of a listener type for error logs: the defining mod's id when it is
    ///     mod content, otherwise the defining assembly.
    /// </summary>
    private static string DescribeOwner(Type type)
    {
        var modId = WhatMod.FindMod(type)?.manifest?.id;
        return string.IsNullOrWhiteSpace(modId)
            ? $"assembly '{type.Assembly.GetName().Name ?? "<unknown>"}'"
            : $"mod '{modId}'";
    }

    /// <summary>
    ///     The hook interface's short name for error logs; strips the generic arity suffix so
    ///     <c>IAfterSpendResource&lt;T&gt;</c> logs as <c>IAfterSpendResource</c>.
    /// </summary>
    private static string HookName<THook>()
        where THook : class
    {
        var name = typeof(THook).Name;
        var aritySeparator = name.IndexOf('`');
        return aritySeparator < 0 ? name : name[..aritySeparator];
    }
}