using Godot;
using MegaCrit.Sts2.Core.Combat;

namespace BaseLib.Hooks;

/// <summary>
///     Convenience helpers for building health bar forecast segments.
/// </summary>
public static class HealthBarForecasts
{
    /// <summary>
    ///     Starts a general-purpose sequence builder for <paramref name="context" />.
    /// </summary>
    public static HealthBarForecastSequenceBuilder For(HealthBarForecastContext context)
    {
        return new HealthBarForecastSequenceBuilder(context);
    }

    /// <summary>
    ///     Starts a right-growing forecast lane with a fixed <paramref name="color" />.
    /// </summary>
    public static HealthBarForecastLaneBuilder FromRight(HealthBarForecastContext context, Color color)
    {
        return FromRight(context, color, null);
    }

    /// <summary>
    ///     Starts a right-growing forecast lane with a separate optional overlay modulate.
    /// </summary>
    public static HealthBarForecastLaneBuilder FromRight(
        HealthBarForecastContext context,
        Color color,
        Color? overlaySelfModulate)
    {
        return FromRight(context, color, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="FromRight(HealthBarForecastContext, Color, Color?)" />
    public static HealthBarForecastLaneBuilder FromRight(
        HealthBarForecastContext context,
        Color color,
        Color? overlaySelfModulate,
        bool affectsHpLabel)
    {
        return new HealthBarForecastLaneBuilder(
            For(context),
            color,
            HealthBarForecastDirection.FromRight,
            overlaySelfModulate,
            affectsHpLabel);
    }

    /// <summary>
    ///     Starts a left-growing forecast lane with a fixed <paramref name="color" />.
    /// </summary>
    public static HealthBarForecastLaneBuilder FromLeft(HealthBarForecastContext context, Color color)
    {
        return FromLeft(context, color, null);
    }

    /// <inheritdoc cref="FromRight(HealthBarForecastContext, Color, Color?)" />
    public static HealthBarForecastLaneBuilder FromLeft(
        HealthBarForecastContext context,
        Color color,
        Color? overlaySelfModulate)
    {
        return FromLeft(context, color, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="FromRight(HealthBarForecastContext, Color, Color?)" />
    public static HealthBarForecastLaneBuilder FromLeft(
        HealthBarForecastContext context,
        Color color,
        Color? overlaySelfModulate,
        bool affectsHpLabel)
    {
        return new HealthBarForecastLaneBuilder(
            For(context),
            color,
            HealthBarForecastDirection.FromLeft,
            overlaySelfModulate,
            affectsHpLabel);
    }

    /// <summary>
    ///     Returns a single segment when <paramref name="amount" /> is positive.
    /// </summary>
    public static IEnumerable<HealthBarForecastSegment> Single(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial)
    {
        return Single(amount, color, direction, order, overlayMaterial, null);
    }

    /// <summary>
    ///     Returns a single segment when <paramref name="amount" /> is positive, with optional material and overlay
    ///     modulate.
    /// </summary>
    public static IEnumerable<HealthBarForecastSegment> Single(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate)
    {
        return Single(amount, color, direction, order, overlayMaterial, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="Single(int, Color, HealthBarForecastDirection, int, Material?, Color?)" />
    public static IEnumerable<HealthBarForecastSegment> Single(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate,
        bool affectsHpLabel)
    {
        if (amount <= 0)
            return [];

        return
        [
            new HealthBarForecastSegment(
                amount,
                color,
                direction,
                order,
                overlayMaterial,
                overlaySelfModulate,
                AffectsHpLabel: affectsHpLabel)
        ];
    }

    /// <summary>
    ///     Returns a single segment when <paramref name="amount" /> is positive, without a custom material.
    /// </summary>
    public static IEnumerable<HealthBarForecastSegment> Single(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order = 0)
    {
        return Single(amount, color, direction, order, null, null);
    }
}

/// <summary>
///     Mutable builder for one forecast source's ordered segment sequence.
/// </summary>
public sealed class HealthBarForecastSequenceBuilder(HealthBarForecastContext context)
{
    private readonly List<HealthBarForecastSegment> _segments = [];

    /// <summary>
    ///     Forecast context associated with this sequence.
    /// </summary>
    public HealthBarForecastContext Context { get; } = context;

    /// <summary>
    ///     Appends a segment when <paramref name="amount" /> is positive.
    /// </summary>
    public HealthBarForecastSequenceBuilder Add(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial)
    {
        return Add(amount, color, direction, order, overlayMaterial, null);
    }

    /// <summary>
    ///     Appends a segment when <paramref name="amount" /> is positive, with explicit overlay modulate.
    /// </summary>
    public HealthBarForecastSequenceBuilder Add(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate)
    {
        return Add(amount, color, direction, order, overlayMaterial, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="Add(int, Color, HealthBarForecastDirection, int, Material?, Color?)" />
    public HealthBarForecastSequenceBuilder Add(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate,
        bool affectsHpLabel)
    {
        if (amount <= 0)
            return this;

        var segment = new HealthBarForecastSegment(
            amount,
            color,
            direction,
            order,
            overlayMaterial,
            overlaySelfModulate,
            AffectsHpLabel: affectsHpLabel);
        if (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (CanMerge(last, segment))
            {
                _segments[^1] = last with { Amount = last.Amount + segment.Amount };
                return this;
            }
        }

        _segments.Add(segment);
        return this;
    }

    /// <summary>
    ///     Appends a segment without a custom material.
    /// </summary>
    public HealthBarForecastSequenceBuilder Add(
        int amount,
        Color color,
        HealthBarForecastDirection direction,
        int order = 0)
    {
        return Add(amount, color, direction, order, null, null);
    }

    /// <summary>
    ///     Appends all positive amounts as consecutive segments.
    /// </summary>
    public HealthBarForecastSequenceBuilder AddRange(
        IEnumerable<int> amounts,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial)
    {
        return AddRange(amounts, color, direction, order, overlayMaterial, null);
    }

    /// <summary>
    ///     Appends all positive amounts as consecutive segments with explicit overlay modulate.
    /// </summary>
    public HealthBarForecastSequenceBuilder AddRange(
        IEnumerable<int> amounts,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate)
    {
        return AddRange(amounts, color, direction, order, overlayMaterial, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="AddRange(IEnumerable{int}, Color, HealthBarForecastDirection, int, Material?, Color?)" />
    public HealthBarForecastSequenceBuilder AddRange(
        IEnumerable<int> amounts,
        Color color,
        HealthBarForecastDirection direction,
        int order,
        Material? overlayMaterial,
        Color? overlaySelfModulate,
        bool affectsHpLabel)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        foreach (var amount in amounts)
            Add(amount, color, direction, order, overlayMaterial, overlaySelfModulate, affectsHpLabel);

        return this;
    }

    /// <summary>
    ///     Appends all positive amounts as consecutive segments without a custom material.
    /// </summary>
    public HealthBarForecastSequenceBuilder AddRange(
        IEnumerable<int> amounts,
        Color color,
        HealthBarForecastDirection direction,
        int order = 0)
    {
        return AddRange(amounts, color, direction, order, null, null);
    }

    /// <summary>
    ///     Appends segments that trigger at the start of <paramref name="triggerSide" />'s turn.
    /// </summary>
    public HealthBarForecastSequenceBuilder AddSideTurnStart(
        CombatSide triggerSide,
        Color color,
        HealthBarForecastDirection direction,
        params int[] amounts)
    {
        return AddRange(
            amounts,
            color,
            direction,
            HealthBarForecastOrder.ForSideTurnStart(Context.Creature, triggerSide));
    }

    /// <summary>
    ///     Appends segments that trigger at the end of <paramref name="triggerSide" />'s turn.
    /// </summary>
    public HealthBarForecastSequenceBuilder AddSideTurnEnd(
        CombatSide triggerSide,
        Color color,
        HealthBarForecastDirection direction,
        params int[] amounts)
    {
        return AddRange(
            amounts,
            color,
            direction,
            HealthBarForecastOrder.ForSideTurnEnd(Context.Creature, triggerSide));
    }

    /// <summary>
    ///     Creates a fixed-color right-growing lane on this sequence.
    /// </summary>
    public HealthBarForecastLaneBuilder FromRight(Color color)
    {
        return FromRight(color, null);
    }

    /// <inheritdoc cref="HealthBarForecasts.FromRight(HealthBarForecastContext, Color, Color?)" />
    public HealthBarForecastLaneBuilder FromRight(Color color, Color? overlaySelfModulate)
    {
        return FromRight(color, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="FromRight(Color, Color?)" />
    public HealthBarForecastLaneBuilder FromRight(Color color, Color? overlaySelfModulate, bool affectsHpLabel)
    {
        return new HealthBarForecastLaneBuilder(
            this,
            color,
            HealthBarForecastDirection.FromRight,
            overlaySelfModulate,
            affectsHpLabel);
    }

    /// <summary>
    ///     Creates a fixed-color left-growing lane on this sequence.
    /// </summary>
    public HealthBarForecastLaneBuilder FromLeft(Color color)
    {
        return FromLeft(color, null);
    }

    /// <inheritdoc cref="FromRight(Color, Color?)" />
    public HealthBarForecastLaneBuilder FromLeft(Color color, Color? overlaySelfModulate)
    {
        return FromLeft(color, overlaySelfModulate, true);
    }

    /// <inheritdoc cref="FromRight(Color, Color?)" />
    public HealthBarForecastLaneBuilder FromLeft(Color color, Color? overlaySelfModulate, bool affectsHpLabel)
    {
        return new HealthBarForecastLaneBuilder(
            this,
            color,
            HealthBarForecastDirection.FromLeft,
            overlaySelfModulate,
            affectsHpLabel);
    }

    /// <summary>
    ///     Returns the built sequence snapshot.
    /// </summary>
    public IReadOnlyList<HealthBarForecastSegment> Build()
    {
        return _segments.Count == 0 ? [] : _segments.ToArray();
    }

    private static bool CanMerge(HealthBarForecastSegment left, HealthBarForecastSegment right)
    {
        return left.Color == right.Color &&
               left.Direction == right.Direction &&
               left.Order == right.Order &&
               left.OverlaySelfModulate == right.OverlaySelfModulate &&
               left.LeftOriginLayout == right.LeftOriginLayout &&
               left.LeftExclusiveZGroup == right.LeftExclusiveZGroup &&
               left.AffectsHpLabel == right.AffectsHpLabel &&
               ReferenceEquals(left.OverlayMaterial, right.OverlayMaterial);
    }
}

/// <summary>
///     Convenience wrapper for the common case of one fixed-color forecast lane.
/// </summary>
public sealed class HealthBarForecastLaneBuilder(
    HealthBarForecastSequenceBuilder sequence,
    Color color,
    HealthBarForecastDirection direction,
    Color? overlaySelfModulate = null,
    bool affectsHpLabel = true)
{
    /// <summary>
    ///     Parent sequence builder.
    /// </summary>
    public HealthBarForecastSequenceBuilder Sequence { get; } = sequence;

    /// <summary>
    ///     Appends a segment with explicit <paramref name="order" /> and optional <paramref name="overlayMaterial" />.
    /// </summary>
    public HealthBarForecastLaneBuilder Add(int amount, int order, Material? overlayMaterial)
    {
        Sequence.Add(amount, color, direction, order, overlayMaterial, overlaySelfModulate, affectsHpLabel);
        return this;
    }

    /// <summary>
    ///     Appends a segment without a custom material.
    /// </summary>
    public HealthBarForecastLaneBuilder Add(int amount, int order = 0)
    {
        return Add(amount, order, null);
    }

    /// <summary>
    ///     Appends multiple segments with the same <paramref name="order" /> and optional <paramref name="overlayMaterial" />.
    /// </summary>
    public HealthBarForecastLaneBuilder AddRange(IEnumerable<int> amounts, int order, Material? overlayMaterial)
    {
        Sequence.AddRange(amounts, color, direction, order, overlayMaterial, overlaySelfModulate, affectsHpLabel);
        return this;
    }

    /// <summary>
    ///     Appends multiple segments without a custom material.
    /// </summary>
    public HealthBarForecastLaneBuilder AddRange(IEnumerable<int> amounts, int order = 0)
    {
        return AddRange(amounts, order, null);
    }

    /// <summary>
    ///     Appends segments that trigger at the start of <paramref name="triggerSide" />'s turn.
    /// </summary>
    public HealthBarForecastLaneBuilder AtSideTurnStart(CombatSide triggerSide, params int[] amounts)
    {
        var order = HealthBarForecastOrder.ForSideTurnStart(Sequence.Context.Creature, triggerSide);
        Sequence.AddRange(amounts, color, direction, order, null, overlaySelfModulate, affectsHpLabel);
        return this;
    }

    /// <summary>
    ///     Appends segments that trigger at the end of <paramref name="triggerSide" />'s turn.
    /// </summary>
    public HealthBarForecastLaneBuilder AtSideTurnEnd(CombatSide triggerSide, params int[] amounts)
    {
        var order = HealthBarForecastOrder.ForSideTurnEnd(Sequence.Context.Creature, triggerSide);
        Sequence.AddRange(amounts, color, direction, order, null, overlaySelfModulate, affectsHpLabel);
        return this;
    }

    /// <summary>
    ///     Starts another right-growing lane on the same parent sequence.
    /// </summary>
    public HealthBarForecastLaneBuilder ThenFromRight(Color nextColor)
    {
        return Sequence.FromRight(nextColor, null);
    }

    /// <summary>
    ///     Starts another left-growing lane on the same parent sequence.
    /// </summary>
    public HealthBarForecastLaneBuilder ThenFromLeft(Color nextColor)
    {
        return Sequence.FromLeft(nextColor, null);
    }

    /// <summary>
    ///     Returns the built segment snapshot.
    /// </summary>
    public IReadOnlyList<HealthBarForecastSegment> Build()
    {
        return Sequence.Build();
    }
}
