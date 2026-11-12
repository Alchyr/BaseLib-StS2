using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Model that will passively receive hooks at all times.
/// </summary>
public abstract class CustomSingletonModel : AbstractModel, ICustomModel
{
    public enum HookType
    {
        None,
        Combat,
        Run
    }
    
    /// <summary>
    /// This property seems effectively unused; it is set anyways in case of future changes.
    /// </summary>
    public override bool ShouldReceiveCombatHooks { get; }


    /// <summary>
    /// Create a singleton that receives either combat hooks or run hooks.
    /// While there is probably no point in creating a singleton that receives no hooks, it can be done.
    /// </summary>
    public CustomSingletonModel(HookType hookType)
    {
        switch (hookType)
        {
            case HookType.None:
                break;
            case HookType.Combat:
                ShouldReceiveCombatHooks = true;
                ModHelper.SubscribeForCombatStateHooks(Id.Entry, CombatSubModels);
                break;
            case HookType.Run:
                ModHelper.SubscribeForRunStateHooks(Id.Entry, RunSubModels);
                break;
        }
    }
    
    [Obsolete("Use the constructor receiving a HookType instead. A singleton receiving both types of hooks will receive some hooks twice, so this constructor is being replaced.")]
    public CustomSingletonModel(bool receiveCombatHooks, bool receiveRunHooks) : this(receiveCombatHooks ? HookType.Combat : receiveRunHooks ? HookType.Run : HookType.None)
    {

    }

    private IEnumerable<AbstractModel> RunSubModels(RunState runState)
    {
        return [this];
    }
    private IEnumerable<AbstractModel> CombatSubModels(CombatState combatState)
    {
        return [this];
    }
}