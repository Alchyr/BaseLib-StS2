using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BaseLib.Hooks;

/// <summary>
///     Which edge of the health bar a forecast segment grows from.
/// </summary>
public enum HealthBarForecastDirection
{
    /// <summary>
    ///     Grows inward from the current HP edge (e.g. poison-style).
    /// </summary>
    FromRight = 0,

    /// <summary>
    ///     Grows outward from the empty side (e.g. doom-style).
    /// </summary>
    FromLeft = 1
}

/// <summary>
///     How <see cref="HealthBarForecastDirection.FromLeft" /> segments share the empty-edge origin.
/// </summary>
public enum HealthBarForecastLeftOriginLayout
{
    /// <summary>
    ///     Segments connect end-to-end from the empty edge.
    /// </summary>
    Chained = 0,

    /// <summary>
    ///     Each segment spans from the empty edge by its own <c>Amount</c>, capped to remaining HP.
    /// </summary>
    OverlapFromOrigin = 1
}

/// <summary>
///     One forecast overlay segment for a creature health bar.
/// </summary>
/// <param name="Amount">HP amount represented by this segment.</param>
/// <param name="Color">
///     Lethal HP label theming; also used as the forecast nine-patch <see cref="CanvasItem.SelfModulate" /> when
///     <see cref="OverlaySelfModulate" /> is null.
/// </param>
/// <param name="Direction">Which edge the segment grows from.</param>
/// <param name="Order">
///     Lower values are rendered earlier in the chain.
///     For <see cref="HealthBarForecastDirection.FromRight" />, earlier segments stay closer to the current HP edge; for
///     <see cref="HealthBarForecastDirection.FromLeft" />, earlier segments stay closer to the empty edge.
/// </param>
/// <param name="OverlayMaterial">
///     Optional Godot material (e.g. shader like vanilla doom). When null, only <see cref="Color" /> tint applies.
/// </param>
/// <param name="OverlaySelfModulate">
///     Optional <see cref="CanvasItem.SelfModulate" /> for the forecast nine-patch. When null, <see cref="Color" /> is
///     used
///     for both overlay tint and lethal HP label; when set, <see cref="Color" /> is still used for lethal label theming.
/// </param>
/// <param name="LeftOriginLayout">
///     For <see cref="HealthBarForecastDirection.FromLeft" /> only:
///     <see cref="HealthBarForecastLeftOriginLayout.Chained" /> or
///     <see cref="HealthBarForecastLeftOriginLayout.OverlapFromOrigin" />.
/// </param>
/// <param name="LeftExclusiveZGroup">
///     For <see cref="HealthBarForecastLeftOriginLayout.OverlapFromOrigin" />: larger values draw above smaller values.
/// </param>
/// <param name="AffectsHpLabel">
///     Whether this segment can recolor the HP label when it reaches lethal threshold.
/// </param>
public readonly record struct HealthBarForecastSegment(
    int Amount,
    Color Color,
    HealthBarForecastDirection Direction,
    int Order,
    Material? OverlayMaterial,
    Color? OverlaySelfModulate = null,
    HealthBarForecastLeftOriginLayout LeftOriginLayout = HealthBarForecastLeftOriginLayout.Chained,
    int LeftExclusiveZGroup = 0,
    bool AffectsHpLabel = true)
{
    /// <summary>
    ///     Initializes a segment without overlay material or separate overlay modulate.
    /// </summary>
    public HealthBarForecastSegment(int amount, Color color, HealthBarForecastDirection direction, int order = 0)
        : this(amount, color, direction, order, null, null)
    {
    }

    /// <summary>
    ///     Initializes a segment with an optional <see cref="OverlayMaterial" /> and default overlay modulate.
    /// </summary>
    // ReSharper disable once RedundantOverload.Global
    public HealthBarForecastSegment(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial)
        : this(amount, color, direction, order, overlayMaterial, null)
    {
    }

    /// <summary>
    ///     Initializes a segment with an optional overlay modulate and default left-origin layout.
    /// </summary>
    public HealthBarForecastSegment(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate)
        : this(amount, color, direction, order, overlayMaterial, overlaySelfModulate,
            HealthBarForecastLeftOriginLayout.Chained)
    {
    }

    /// <summary>
    ///     Initializes a segment with a left-origin layout and default exclusive z group.
    /// </summary>
    public HealthBarForecastSegment(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate,
        HealthBarForecastLeftOriginLayout leftOriginLayout)
        : this(amount, color, direction, order, overlayMaterial, overlaySelfModulate, leftOriginLayout, 0)
    {
    }

    /// <summary>
    ///     Initializes a segment with explicit left-origin layout and exclusive z group.
    /// </summary>
    public HealthBarForecastSegment(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate,
        HealthBarForecastLeftOriginLayout leftOriginLayout,
        int leftExclusiveZGroup)
        : this(amount, color, direction, order, overlayMaterial, overlaySelfModulate, leftOriginLayout,
            leftExclusiveZGroup, true)
    {
    }
}

/// <summary>
///     Runtime context passed to <see cref="IHealthBarForecastSource" /> when resolving forecast segments.
/// </summary>
/// <param name="Creature">Creature whose health bar is being rendered.</param>
public readonly record struct HealthBarForecastContext(Creature Creature)
{
    /// <summary>
    ///     Current combat state when the creature is in combat.
    /// </summary>
    public CombatStateWrapper? CombatState => BetaMainCompatibility.Creature_.WrappedCombatState(Creature);

    /// <summary>
    ///     Side whose turn is active when available.
    /// </summary>
    public CombatSide? CurrentSide => CombatState?.CurrentSide;
}

/// <summary>
///     Helpers for turn-relative <see cref="HealthBarForecastSegment.Order" /> values shared by multiple sources.
/// </summary>
public static class HealthBarForecastOrder
{
    /// <summary>
    ///     Returns an order key for effects that resolve at the start of <paramref name="triggerSide" />'s turn.
    /// </summary>
    /// <param name="creature">Creature used to read the active combat side.</param>
    /// <param name="triggerSide">Side whose turn start is being modeled.</param>
    /// <returns>Higher value when it is currently that side's turn (segments stack after others).</returns>
    public static int ForSideTurnStart(Creature creature, CombatSide triggerSide)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return BetaMainCompatibility.Creature_.WrappedCombatState(creature)?.CurrentSide == triggerSide ? 1 : 0;
    }

    /// <summary>
    ///     Returns an order key for effects that resolve at the end of <paramref name="triggerSide" />'s turn.
    /// </summary>
    /// <param name="creature">Creature used to read the active combat side.</param>
    /// <param name="triggerSide">Side whose turn end is being modeled.</param>
    /// <returns>Higher value when it is not currently that side's turn.</returns>
    public static int ForSideTurnEnd(Creature creature, CombatSide triggerSide)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return BetaMainCompatibility.Creature_.WrappedCombatState(creature)?.CurrentSide == triggerSide ? 0 : 1;
    }
}

/// <summary>
///     Produces one or more <see cref="HealthBarForecastSegment" /> values for a creature's health bar overlay.
/// </summary>
/// <remarks>
///     Power models can implement this on the power type and are discovered from <see cref="Creature.Powers" />.
///     Additional sources can be registered with
///     <see cref="HealthBarForecastRegistry.Register(string, string, IHealthBarForecastSource)" />
///     or <see cref="HealthBarForecastRegistry.RegisterForeign" /> for cross-assembly duck-typed segments.
/// </remarks>
public interface IHealthBarForecastSource
{
    /// <summary>
    ///     Returns segments to render for <paramref name="context" />; skip or yield empty when none apply.
    /// </summary>
    /// <param name="context">Creature and combat context for the bar being drawn.</param>
    IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context);
}
