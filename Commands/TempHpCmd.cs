using BaseLib.Patches.Content;
using BaseLib.Powers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;

namespace BaseLib.Commands;

/// <summary>
///     Public API for the Temporary HP mechanic (port of StSLib's Temporary HP).
///     Temporary HP absorbs any HP loss that goes through the damage pipeline (attacks, Poison, self-damage, ...).
///     Order: Block → Osty redirect → Temporary HP → late-pass buffs (ModifyHpLostAfterOstyLate, e.g.
///     Buffer-style prevention) → real HP. Disappears at the end of combat.
///     See <see cref="TempHpPower" /> for the underlying hidden power and <see cref="Hooks.ITempHpLossModifier" />
///     for the per-hit modification hook.
/// </summary>
public static class TempHpCmd
{
    /// <summary>
    ///     Reserved damage-props bit: damage carrying this flag bypasses Temporary HP entirely
    ///     (the StSLib "ignoresTempHP" equivalent). Vanilla only uses bits 1-4; BaseLib reserves bit 28.
    /// </summary>
    public const ValueProp IgnoresTempHp = (ValueProp)(1 << 28);

    /// <summary>Hover tip shown when a creature has Temporary HP. Usable on cards via WithTip(TempHpCmd.TempHp).</summary>
    [CustomEnum] public static StaticHoverTip TempHp;

    /// <summary>Fired after a creature gains Temporary HP: (creature, amount gained, new total).</summary>
    public static event Action<Creature, int, int>? Gained;

    /// <summary>
    ///     Fired after a creature loses Temporary HP: (creature, amount lost, new total, damage that caused it).
    ///     The DamageResult is null for non-damage losses (RemoveAll, foreign PowerCmd decrements, death flush).
    /// </summary>
    public static event Action<Creature, int, int, DamageResult?>? Lost;

    /// <summary>
    ///     Grant Temporary HP to a creature. Stacks with existing Temporary HP.
    ///     Multiplayer-safe when called from synchronized contexts (card OnPlay, hooks, networked console commands),
    ///     exactly like any power application.
    /// </summary>
    /// <returns>The creature's new Temporary HP total.</returns>
    public static async Task<int> Add(PlayerChoiceContext choiceContext, Creature target, int amount,
        Creature? applier = null, CardModel? cardSource = null, bool showVfx = true)
    {
        if (amount <= 0)
            return Get(target);
        var power = await PowerCmd.Apply<TempHpPower>(choiceContext, target, amount, applier, cardSource, silent: true);
        if (power != null && showVfx)
            PlayGainFx(target);
        return power?.Amount ?? Get(target);
    }

    /// <summary>Remove all Temporary HP from a creature (the StSLib RemoveAllTemporaryHPAction equivalent).</summary>
    public static async Task RemoveAll(Creature target)
    {
        var power = target.GetPower<TempHpPower>();
        if (power == null)
            return;
        var had = power.Amount;
        await PowerCmd.Remove(power);
        if (had > 0)
            InvokeLost(target, had, 0, null);
    }

    /// <summary>The creature's current Temporary HP (0 if none).</summary>
    public static int Get(Creature target)
    {
        return target.GetPower<TempHpPower>()?.Amount ?? 0;
    }

    internal static void InvokeGained(Creature creature, int gained, int total)
    {
        Gained?.Invoke(creature, gained, total);
    }

    internal static void InvokeLost(Creature creature, int lost, int total, DamageResult? source)
    {
        Lost?.Invoke(creature, lost, total, source);
    }

    private static void PlayGainFx(Creature target)
    {
        if (!CombatManager.Instance.IsInProgress)
            return;
        SfxCmd.Play("event:/sfx/heal");
        VfxCmd.PlayOnCreatureCenter(target, "vfx/vfx_cross_heal");
    }

    /// <summary>
    ///     Gold floating number for absorbed damage — the only feedback on a fully absorbed hit, since vanilla
    ///     shows no damage number and no hurt anim when the final HP loss is 0.
    /// </summary>
    internal static void PlayAbsorbFx(Creature target, int absorbed)
    {
        if (absorbed <= 0 || !CombatManager.Instance.IsInProgress)
            return;
        var vfx = NDamageNumVfx.Create(target, absorbed);
        if (vfx == null)
            return;
        vfx.Modulate = StsColors.gold; // the _Ready tween animates modulate gold -> cream, mimicking vanilla's red -> cream
        var label = vfx.GetNodeOrNull<MegaLabel>("Label");
        label?.AddThemeColorOverride("font_color", StsColors.gold);
        label?.AddThemeColorOverride("font_outline_color", StsColors.rewardLabelGoldOutline);
        var container = target.GetVfxContainer();
        if (container != null)
            container.AddChildSafely(vfx);
        else
            NRun.Instance?.GlobalUi.AddChildSafely(vfx);
    }
}
