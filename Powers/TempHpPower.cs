using BaseLib.Abstracts;
using BaseLib.Commands;
using BaseLib.Hooks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Powers;

/// <summary>
///     Temporary HP (port of StSLib's mechanic): a hidden per-creature pool that absorbs any HP loss going through
///     the damage pipeline. Resolution order: Block → Osty redirect → Temporary HP (early AfterOsty pass) →
///     late-pass buffs (ModifyHpLostAfterOstyLate, e.g. Buffer-style prevention) → real HP. Because absorption
///     happens after the redirect, it is the FINAL receiver's pool that absorbs (redirected damage consumes Osty's
///     Temporary HP; overkill trampling back consumes the original target's).
///     Removed automatically at end of combat and on death, like any power.
///     Grant/query through <see cref="TempHpCmd" /> or the Creature extensions; do not apply this power directly
///     unless you want to skip the gain FX.
/// </summary>
public sealed class TempHpPower : CustomPowerModel
{
    /// <inheritdoc />
    public override PowerType Type => PowerType.Buff;

    /// <inheritdoc />
    public override PowerStackType StackType => PowerStackType.Counter;

    /// Hidden from the power row; rendered on the health bar instead (see TempHpDisplayPatch).
    /// Invisibility also exempts it from Artifact, which only blocks visible debuffs.
    protected override bool IsVisibleInternal => false;

    /// Absorption is computed in the pure ModifyHpLost phase but applied in AfterDamageReceived, which runs after
    /// every HP mutation of the same Damage() call has completed. Only nonzero within a single damage resolution;
    /// never serialized (power serialization is (ModelId, Amount) only).
    private int _pendingAbsorb;

    /// <inheritdoc />
    protected override void AfterCloned()
    {
        base.AfterCloned();
        _pendingAbsorb = 0;
    }

    /// <summary>
    ///     Early AfterOsty pass: after Block and after the Osty redirect (target here is the final damage receiver),
    ///     but before all late-pass HP-loss hooks — Buffer-style preventers implementing ModifyHpLostAfterOstyLate
    ///     are guaranteed to run after Temporary HP. Relative order versus other EARLY-pass effects on the same
    ///     creature (e.g. vanilla Intangible) follows power application order. Must stay pure — the pool is only
    ///     debited later, in <see cref="AfterDamageReceived" />.
    /// </summary>
    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!CombatManager.Instance.IsInProgress)
            return amount;
        if (target != Owner || amount < 1m)
            return amount;
        if (props.HasFlag(TempHpCmd.IgnoresTempHp))
            return amount;
        // The same creature can enter this pass twice within one Damage() call (multi-target redirects, and the
        // overkill-trample-back AfterOsty call for the original target), hence the remaining-pool cap.
        var remaining = Amount - _pendingAbsorb;
        if (remaining <= 0)
            return amount;

        var toTempHp = amount;
        foreach (var power in Owner.Powers)
        {
            if (power is ITempHpLossModifier modifier)
                toTempHp = modifier.ModifyTempHpLoss(toTempHp, Owner, props, dealer, cardSource);
        }
        var relics = Owner.Player?.Relics;
        if (relics != null)
        {
            foreach (var relic in relics)
            {
                if (relic is ITempHpLossModifier modifier)
                    toTempHp = modifier.ModifyTempHpLoss(toTempHp, Owner, props, dealer, cardSource);
            }
        }

        var absorbed = (int)Math.Min(decimal.Truncate(toTempHp), remaining);
        if (absorbed <= 0)
            return amount;
        _pendingAbsorb += absorbed;
        return amount - absorbed;
    }

    /// <summary>
    ///     Applies the absorption computed during the ModifyHpLost phase of the same Damage() call.
    ///     Dispatched once per DamageResult after ALL HP mutations of the call have completed; the results loop
    ///     fires it with the result whose Receiver is this pool's owner (redirect and overkill-trample cases each
    ///     produce their own result), so filtering on Receiver == Owner commits exactly once.
    /// </summary>
    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (result.Receiver != Owner || _pendingAbsorb == 0)
            return;
        await ApplyPendingAbsorb(silent: false, playFx: true, result);
    }

    /// <summary>
    ///     A killed target skips AfterDamageReceived entirely, but death can still be prevented (Fairy in a Bottle
    ///     etc.). Flushing here — before the death-prevention decision — keeps the pool consistent either way.
    /// </summary>
    public override Task BeforeDeath(Creature creature)
    {
        if (creature == Owner && _pendingAbsorb > 0)
            return ApplyPendingAbsorb(silent: true, playFx: false, null);
        return Task.CompletedTask;
    }

    private async Task ApplyPendingAbsorb(bool silent, bool playFx, DamageResult? source)
    {
        var lost = Math.Min(_pendingAbsorb, Amount);
        _pendingAbsorb = 0;
        if (lost <= 0)
            return;
        // SetAmount instead of PowerCmd.ModifyAmount: the debit must equal the absorbed damage exactly, without
        // being re-routed through the ModifyPowerAmountGiven/Received hook bus.
        SetAmount(Amount - lost, silent);
        TempHpCmd.InvokeLost(Owner, lost, Amount, source);
        if (playFx)
            TempHpCmd.PlayAbsorbFx(Owner, lost);
        if (Amount <= 0)
            await PowerCmd.Remove(this); // hook dispatch iterates a snapshot list, so removal mid-dispatch is safe
    }

    /// <summary>Forwards gains (and foreign PowerCmd changes) to the <see cref="TempHpCmd" /> events.</summary>
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power != this)
            return Task.CompletedTask;
        // Covers PowerCmd.Apply (fresh application and stacking) and foreign PowerCmd.ModifyAmount calls.
        // Our own SetAmount debit does not re-enter this hook, so absorption is never double-reported.
        if (amount > 0m)
            TempHpCmd.InvokeGained(Owner, (int)amount, Amount);
        else if (amount < 0m)
            TempHpCmd.InvokeLost(Owner, (int)(-amount), Amount, null);
        return Task.CompletedTask;
    }
}
