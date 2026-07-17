using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BaseLib.Hooks;

/// <summary>
///     Aggregates health bar forecast segments from creature powers, registered sources, and optional foreign providers.
/// </summary>
/// <remarks>
///     Typed segments use <see cref="HealthBarForecastSegment" />.
///     <see cref="RegisterForeign" /> accepts objects with public instance properties:
///     <c>Amount</c> (<see cref="int" />), <c>Color</c> (<see cref="Godot.Color" />), <c>Direction</c> (enum or string
///     containing FromLeft/FromRight); optional <c>Order</c> (<see cref="int" />), <c>OverlayMaterial</c>
///     (<see cref="Godot.Material" />), <c>OverlaySelfModulate</c> (<see cref="Godot.Color" />?),
///     <c>LeftOriginLayout</c> (enum or string containing Chained/OverlapFromOrigin),
///     <c>LeftExclusiveZGroup</c> (<see cref="int" />), and <c>AffectsHpLabel</c> (<see cref="bool" />).
/// </remarks>
public static class HealthBarForecastRegistry
{
    private static readonly Lock SyncRoot = new();
    private static readonly Dictionary<(string ModId, string SourceId), ProviderEntry> Providers = [];
    private static readonly ConcurrentDictionary<Type, ForeignSegmentAccessors?> ForeignSegmentAccessorCache = new();
    private static long _nextRegistrationOrder;

    /// <summary>
    ///     Registers or replaces a forecast source implemented by <typeparamref name="TSource" />.
    /// </summary>
    /// <typeparam name="TSource">Concrete type with a parameterless constructor.</typeparam>
    /// <param name="modId">Owning mod identifier (for logging and stable keys).</param>
    /// <param name="sourceId">Optional unique id; defaults to the type's full name.</param>
    public static void Register<TSource>(string modId, string? sourceId = null)
        where TSource : IHealthBarForecastSource, new()
    {
        Register(modId, sourceId ?? typeof(TSource).FullName ?? typeof(TSource).Name, new TSource());
    }

    /// <summary>
    ///     Registers or replaces a forecast source instance.
    /// </summary>
    /// <param name="modId">Owning mod identifier.</param>
    /// <param name="sourceId">Unique id for this source within the mod.</param>
    /// <param name="source">Provider instance.</param>
    public static void Register(string modId, string sourceId, IHealthBarForecastSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(source);
        RegisterProvider(modId, sourceId, source, null);
    }

    /// <summary>
    ///     Registers a provider that yields duck-typed segment objects (see class remarks).
    /// </summary>
    /// <param name="modId">Owning mod identifier.</param>
    /// <param name="sourceId">Unique id for this provider within the mod.</param>
    /// <param name="provider">Returns segment objects per creature; null entries are ignored.</param>
    public static void RegisterForeign(string modId, string sourceId, Func<Creature, IEnumerable<object>> provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(provider);
        RegisterProvider(modId, sourceId, null, provider);
    }

    /// <summary>
    ///     Removes a previously registered typed or foreign provider.
    /// </summary>
    /// <param name="modId">Mod identifier used at registration.</param>
    /// <param name="sourceId">Source id used at registration.</param>
    /// <returns><see langword="true" /> if an entry was removed.</returns>
    public static bool Unregister(string modId, string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        lock (SyncRoot)
        {
            return Providers.Remove((modId, sourceId));
        }
    }

    /// <summary>
    ///     Collects all applicable segments for <paramref name="creature" />, in registration order with sequence keys.
    /// </summary>
    /// <param name="creature">Creature whose bar is being evaluated.</param>
    internal static IReadOnlyList<RegisteredHealthBarForecastSegment> GetSegments(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var context = new HealthBarForecastContext(creature);
        List<RegisteredHealthBarForecastSegment> segments = [];
        var powerSequenceOrder = 0L;

        foreach (var source in creature.Powers.OfType<IHealthBarForecastSource>())
            AppendTypedSegments(
                source,
                source.GetType().FullName ?? source.GetType().Name,
                context,
                powerSequenceOrder++,
                segments,
                "creature power");

        ProviderEntry[] snapshot;
        lock (SyncRoot)
        {
            snapshot = Providers.Values
                .OrderBy(entry => entry.RegistrationOrder)
                .ToArray();
        }

        const long externalOrderOffset = 1_000_000L;
        foreach (var entry in snapshot)
        {
            if (entry.Source != null)
            {
                AppendTypedSegments(
                    entry.Source,
                    entry.SourceId,
                    context,
                    externalOrderOffset + entry.RegistrationOrder,
                    segments,
                    $"registered source ({entry.ModId})");
                continue;
            }

            if (entry.ForeignProvider != null)
                AppendForeignSegments(
                    entry.ForeignProvider,
                    entry.SourceId,
                    creature,
                    externalOrderOffset + entry.RegistrationOrder,
                    segments,
                    entry.ModId);
        }

        return segments;
    }

    private static void RegisterProvider(
        string modId,
        string sourceId,
        IHealthBarForecastSource? source,
        Func<Creature, IEnumerable<object>>? foreignProvider)
    {
        lock (SyncRoot)
        {
            var key = (modId, sourceId);
            var registrationOrder = Providers.TryGetValue(key, out var existing)
                ? existing.RegistrationOrder
                : _nextRegistrationOrder++;

            Providers[key] = new ProviderEntry(modId, sourceId, source, foreignProvider, registrationOrder);
        }
    }

    private static void AppendTypedSegments(
        IHealthBarForecastSource source,
        string sourceId,
        HealthBarForecastContext context,
        long sequenceOrder,
        List<RegisteredHealthBarForecastSegment> destination,
        string owner)
    {
        try
        {
            var providedSegments = source.GetHealthBarForecastSegments(context);
            destination.AddRange(from segment in providedSegments
                where segment.Amount > 0
                select new RegisteredHealthBarForecastSegment(segment, sequenceOrder));
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Warn(
                $"[HealthBarForecast] Source '{sourceId}' from {owner} failed for creature '{context.Creature}': {ex}");
        }
    }

    private static void AppendForeignSegments(
        Func<Creature, IEnumerable<object>> provider,
        string sourceId,
        Creature creature,
        long sequenceOrder,
        List<RegisteredHealthBarForecastSegment> destination,
        string modId)
    {
        try
        {
            var foreignSegments = provider(creature);
            foreach (var foreignSegment in foreignSegments)
            {
                if (!TryConvertForeignSegment(foreignSegment, out var converted))
                    continue;

                if (converted.Amount <= 0)
                    continue;

                destination.Add(new RegisteredHealthBarForecastSegment(converted, sequenceOrder));
            }
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Warn(
                $"[HealthBarForecast] Foreign source '{sourceId}' from mod '{modId}' failed for creature '{creature}': {ex}");
        }
    }

    private static bool TryConvertForeignSegment(object? segment, out HealthBarForecastSegment converted)
    {
        converted = default;
        switch (segment)
        {
            case null:
                return false;
            case HealthBarForecastSegment direct:
                converted = direct;
                return true;
        }

        var accessorOrNull = ForeignSegmentAccessorCache.GetOrAdd(segment.GetType(), CreateForeignSegmentAccessors);
        if (accessorOrNull == null)
            return false;
        var accessors = accessorOrNull.Value;

        var amount = accessors.ReadAmount(segment);
        var color = accessors.ReadColor(segment);
        if (!TryParseDirection(accessors.ReadDirection(segment), out var direction))
            return false;

        converted = new HealthBarForecastSegment(
            amount,
            color,
            direction,
            accessors.ReadOrder(segment),
            accessors.ReadOverlayMaterial(segment),
            accessors.ReadOverlaySelfModulate(segment),
            ParseLeftOriginLayout(accessors.ReadLeftOriginLayout(segment)),
            accessors.ReadLeftExclusiveZGroup(segment),
            accessors.ReadAffectsHpLabel(segment));
        return true;
    }

    private static bool TryParseDirection(object? directionValue, out HealthBarForecastDirection direction)
    {
        direction = HealthBarForecastDirection.FromRight;
        switch (directionValue)
        {
            case null:
                return false;
            case HealthBarForecastDirection typedDirection:
                direction = typedDirection;
                return true;
        }

        var directionName = directionValue.ToString();
        if (string.IsNullOrWhiteSpace(directionName))
            return false;

        if (directionName.Contains("FromLeft", StringComparison.OrdinalIgnoreCase))
        {
            direction = HealthBarForecastDirection.FromLeft;
            return true;
        }

        if (!directionName.Contains("FromRight", StringComparison.OrdinalIgnoreCase)) return false;
        direction = HealthBarForecastDirection.FromRight;
        return true;

    }

    private static HealthBarForecastLeftOriginLayout ParseLeftOriginLayout(object? layoutValue)
    {
        switch (layoutValue)
        {
            case null:
                return HealthBarForecastLeftOriginLayout.Chained;
            case HealthBarForecastLeftOriginLayout typedLayout:
                return typedLayout;
        }

        var layoutName = layoutValue.ToString();
        if (string.IsNullOrWhiteSpace(layoutName))
            return HealthBarForecastLeftOriginLayout.Chained;

        return layoutName.Contains("OverlapFromOrigin", StringComparison.OrdinalIgnoreCase)
            ? HealthBarForecastLeftOriginLayout.OverlapFromOrigin
            : HealthBarForecastLeftOriginLayout.Chained;
    }

    private static ForeignSegmentAccessors? CreateForeignSegmentAccessors(Type type)
    {
        var amount = type.GetProperty("Amount", BindingFlags.Instance | BindingFlags.Public);
        var color = type.GetProperty("Color", BindingFlags.Instance | BindingFlags.Public);
        var direction = type.GetProperty("Direction", BindingFlags.Instance | BindingFlags.Public);
        var order = type.GetProperty("Order", BindingFlags.Instance | BindingFlags.Public);
        var overlayMaterial = type.GetProperty("OverlayMaterial", BindingFlags.Instance | BindingFlags.Public);
        var overlaySelfModulate = type.GetProperty("OverlaySelfModulate", BindingFlags.Instance | BindingFlags.Public);
        var leftOriginLayout = type.GetProperty("LeftOriginLayout", BindingFlags.Instance | BindingFlags.Public);
        var leftExclusiveZGroup = type.GetProperty("LeftExclusiveZGroup", BindingFlags.Instance | BindingFlags.Public);
        var affectsHpLabel = type.GetProperty("AffectsHpLabel", BindingFlags.Instance | BindingFlags.Public);

        if (amount?.PropertyType != typeof(int) ||
            color?.PropertyType != typeof(Color) ||
            direction == null)
            return null;

        Func<object, Material?> readOverlay = overlayMaterial?.PropertyType == typeof(Material)
            ? segment => (Material?)overlayMaterial.GetValue(segment)
            : _ => null;

        Func<object, Color?> readOverlaySelfModulate =
            overlaySelfModulate?.PropertyType == typeof(Color?)
                ? segment => (Color?)overlaySelfModulate.GetValue(segment)
                : _ => null;

        return new ForeignSegmentAccessors(
            segment => (int)amount.GetValue(segment)!,
            segment => (Color)color.GetValue(segment)!,
            segment => direction.GetValue(segment),
            order?.PropertyType == typeof(int)
                ? segment => (int)order.GetValue(segment)!
                : _ => 0,
            readOverlay,
            readOverlaySelfModulate,
            leftOriginLayout == null
                ? _ => null
                : segment => leftOriginLayout.GetValue(segment),
            leftExclusiveZGroup?.PropertyType == typeof(int)
                ? segment => (int)leftExclusiveZGroup.GetValue(segment)!
                : _ => 0,
            affectsHpLabel?.PropertyType == typeof(bool)
                ? segment => (bool)affectsHpLabel.GetValue(segment)!
                : _ => true);
    }

    /// <summary>
    ///     A segment plus a monotonic key used to break ties when <see cref="HealthBarForecastSegment.Order" /> matches.
    /// </summary>
    /// <param name="Segment">Typed forecast data.</param>
    /// <param name="SequenceOrder">Stable ordering among sources (powers first, then registered providers).</param>
    internal readonly record struct RegisteredHealthBarForecastSegment(
        HealthBarForecastSegment Segment,
        long SequenceOrder);

    private readonly record struct ProviderEntry(
        string ModId,
        string SourceId,
        IHealthBarForecastSource? Source,
        Func<Creature, IEnumerable<object>>? ForeignProvider,
        long RegistrationOrder);

    private readonly record struct ForeignSegmentAccessors(
        Func<object, int> ReadAmount,
        Func<object, Color> ReadColor,
        Func<object, object?> ReadDirection,
        Func<object, int> ReadOrder,
        Func<object, Material?> ReadOverlayMaterial,
        Func<object, Color?> ReadOverlaySelfModulate,
        Func<object, object?> ReadLeftOriginLayout,
        Func<object, int> ReadLeftExclusiveZGroup,
        Func<object, bool> ReadAffectsHpLabel);
}
