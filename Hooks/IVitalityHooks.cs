using BaseLib.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
/// Defines a hook listener that runs automatically before and after a vitality cmd has fully resolved.
/// <para>
/// Hook interfaces should be implemented on <see cref="AbstractModel"/> subclasses in the active 
/// combat state to be picked up by the central dispatch pipelines.
/// </para>
/// </summary>
public interface IVitalityHooks
{
    /// <summary>
    /// Invoked before <see cref="VitalityCmd.GainVitality"/> has fully completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State Timing:</b> When this method runs, vitality has not yet been added nor have any listeners
    /// altered its values. 
    /// </para>
    /// </remarks>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">The vitality amount so far, including earlier listeners' changes.</param>
    /// <param name="cardSource">Passes the cardSource if there is one.</param>
    /// <returns>A <see cref="Task"/> tracking the asynchronous execution of this follow-up hook logic.</returns>
    Task BeforeVitalityGained (Creature creature, decimal amount, CardModel? cardSource) => Task.CompletedTask;
    
    /// <summary>
    /// Invoked after <see cref="VitalityCmd.GainVitality"/> has fully completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method will run regardless of the final value of the vitality given, even if it's zero.
    /// Listeners are expected.
    /// </para>
    /// <para>
    /// <b>State Timing:</b> When this method runs, vitality has been added, and
    /// <see cref="IModifyVitalityAmount.AfterModifyingVitalityAmount"/> hooks have already ran. 
    /// </para>
    /// </remarks>
    /// <param name="creature">The creature gaining vitality.</param>
    /// <param name="amount">
    /// The vitality amount so far, including earlier listeners' changes.
    /// This value is clamped to a minimum of zero.
    /// </param>
    /// <param name="cardSource">Passes the cardSource if there is one.</param>
    /// <returns>A <see cref="Task"/> tracking the asynchronous execution of this follow-up hook logic.</returns>
    Task AfterVitalityGained(Creature creature, decimal amount, CardModel? cardSource) => Task.CompletedTask;
}