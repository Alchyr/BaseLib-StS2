using System.Reflection;
using BaseLib.Extensions;
using BaseLib.Patches.Localization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

internal interface IBetaCompatTempPower
{
    void IgnoreNextInstance();
}

/// <summary>
/// A generic version of the base games Temporary Strength and Dexterity Power with small functionality improvements
/// </summary>
public abstract class CustomTemporaryPowerModel : CustomPowerModel, ITemporaryPower, IBetaCompatTempPower, IAddDumbVariablesToPowerDescription
{
     private const string LocTurnEndBoolVar = "UntilEndOfOtherSideTurn";

     /// <inheritdoc />
     public void AddDumbVariablesToPowerDescription(LocString description)
     {
         description.Add("TemporaryPowerTitle", this.InternallyAppliedPower.Title);
     }

    protected abstract Func<PlayerChoiceContext, Creature, decimal, Creature?, CardModel?, bool, Task> ApplyPowerFunc { get; }

    /// <inheritdoc />
    public abstract PowerModel InternallyAppliedPower { get; }

    /// <inheritdoc />
    public abstract AbstractModel OriginModel { get; }
    protected virtual bool UntilEndOfOtherSideTurn => false;
    protected virtual int LastForXExtraTurns => 0;

    /// <inheritdoc />
    public override PowerType Type => InvertInternalPowerAmount ? 
        InternallyAppliedPower.Type switch
        {
            PowerType.Buff => PowerType.Debuff,
            PowerType.Debuff => PowerType.Buff,
            _ => PowerType.None,
        }
        : InternallyAppliedPower.Type;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => true;
    
    //public override bool IsInstanced => LastForXExtraTurns != 0; //changed to PowerInstanceType

    //This will not work on main branch; swap to a patch of base method :(
    //Property of main branch has default value, so missing override won't be an issue.
    [HarmonyPatch]
    class OldTemporaryPowerInstancedPatch
    {
        static MethodInfo? _targetMethod = AccessTools.PropertyGetter(typeof(PowerModel), "IsInstanced");
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (_targetMethod != null) yield return _targetMethod;
        }

        static bool Prepare()
        {
            return _targetMethod != null;
        }
        
        [HarmonyPrefix]
        static bool MaybeInstanced(PowerModel __instance, ref bool? __result)
        {
            if (__instance is not CustomTemporaryPowerModel tempPower) return true;

            __result = tempPower.LastForXExtraTurns != 0;
            return false;
        }
    }
    [HarmonyPatch]
    class NewTemporaryPowerInstancedPatch
    {
        private static readonly MethodInfo? GetInstanceType = AccessTools.PropertyGetter(typeof(PowerModel), "InstanceType");
        private static readonly Type? InstanceTypeEnum = "MegaCrit.Sts2.Core.Entities.Powers.PowerInstanceType".TryGetType();
        static IEnumerable<MethodBase> TargetMethods()
        {
            if (GetInstanceType != null) yield return GetInstanceType;
        }

        static bool Prepare()
        {
            return GetInstanceType != null;
        }
        
        [HarmonyPrefix]
        static bool MaybeInstanced(PowerModel __instance, ref object? __result)
        {
            if (__instance is not CustomTemporaryPowerModel tempPower) return true;

            if (InstanceTypeEnum == null)
                throw new InvalidOperationException("Could not get PowerInstanceType enum type");

            if (tempPower.LastForXExtraTurns == 0) return true;

            __result = InstanceTypeEnum.GetEnumValues().GetValue(1);
            return false;
        }
    }
    /*public override PowerInstanceType InstanceType =>
        LastForXExtraTurns != 0 ? PowerInstanceType.Instanced : PowerInstanceType.None;*/
    
    protected virtual bool InvertInternalPowerAmount => false;
    
    // The whole IgnoreNextInstance thing ONLY exists because of the Misery card
    // Check Misery.DoHackyThingsForSpecificPowers() for usage
    // Removed in beta.
    private bool _shouldIgnoreNextInstance;

    /// <inheritdoc />
    public void IgnoreNextInstance() => _shouldIgnoreNextInstance = true;
    
    // Only used for localization purposes
    /// <inheritdoc />
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(0), new BoolVar(LocTurnEndBoolVar, false)];

    /// <inheritdoc />
    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        var powerSource = this;
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
        {
            // This could lead to infinite recursion if someone makes a mistake and publishes it. So just say no to any attempt.
            BaseLibMain.Logger.Warn($"Don't put TemporaryPowerModels into a TemporaryPowerModel. Attempted to apply power '{InternallyAppliedPower.GetType().Name}' in power '{this.GetType().Name}'. Power will not be applied!");
            return;
        }
        if (_shouldIgnoreNextInstance)
        {
            _shouldIgnoreNextInstance = false;
        }
        else
        {
            DynamicVars.Repeat.BaseValue = LastForXExtraTurns;
            DynamicVars[LocTurnEndBoolVar].BaseValue = Convert.ToDecimal(UntilEndOfOtherSideTurn);
            await ApplyPowerFunc(new ThrowingPlayerChoiceContext(), target, InvertInternalPowerAmount ? -amount : amount, applier, cardSource, true);
        }
    }


    /// <inheritdoc />
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        var powerSource = this;
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
            return;
        if (amount == powerSource.Amount || power != powerSource)
            return;
        if (powerSource._shouldIgnoreNextInstance)
            powerSource._shouldIgnoreNextInstance = false;
        else
            await ApplyPowerFunc(context, powerSource.Owner, InvertInternalPowerAmount ? -amount : amount, applier, cardSource, true);
    }

    /// <inheritdoc />
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner) == UntilEndOfOtherSideTurn)
            return;
        
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
        {
            await PowerCmd.Remove(this);
            return;
        }

        if (DynamicVars.Repeat.BaseValue > 0)
        {
            DynamicVars.Repeat.UpgradeValueBy(-1);
            return;
        }
        
        Flash();
        await ApplyPowerFunc(choiceContext, Owner, InvertInternalPowerAmount ? Amount : -Amount, Owner, null, true);
        await PowerCmd.Remove(this);
    }
}