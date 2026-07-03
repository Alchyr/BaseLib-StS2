using BaseLib.Hooks;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.UI;

/// <summary>
///     Harmony postfixes on <see cref="NHealthBar" /> that draw custom forecast nine-patches, middleground animation,
///     and lethal HP label tinting from <see cref="HealthBarForecastRegistry" /> data.
/// </summary>
/// <remarks>
///     When no segments apply, vanilla visuals are unchanged. Right-side segments layer above poison; left-side above
///     doom.
/// </remarks>
[HarmonyPatch]
public static class HealthBarForecastPatch
{
    private static readonly SpireField<NHealthBar, HealthBarForecastUiState?> UiStates = new(() => null);

    private static readonly Color DoomLethalTextColor = new("FB8DFF");
    private static readonly Color DoomLethalOutlineColor = new("2D1263");

    [HarmonyPatch(typeof(NHealthBar), "RefreshForeground")]
    [HarmonyPostfix]
    private static void RefreshForegroundPostfix(NHealthBar __instance)
    {
        RefreshForegroundOverlay(__instance);
    }

    [HarmonyPatch(typeof(NHealthBar), "RefreshMiddleground")]
    [HarmonyPostfix]
    private static void RefreshMiddlegroundPostfix(NHealthBar __instance)
    {
        RefreshMiddlegroundOverlay(__instance);
    }

    [HarmonyPatch(typeof(NHealthBar), "RefreshText")]
    [HarmonyPostfix]
    private static void RefreshTextPostfix(NHealthBar __instance)
    {
        RefreshTextOverlay(__instance);
    }

    private static void RefreshForegroundOverlay(NHealthBar healthBar)
    {
        var creature = healthBar._creature;
        if (creature.CurrentHp <= 0 || creature.HpDisplay.IsInfinite())
        {
            HideAllCustomSegments(healthBar);
            return;
        }

        var customSegments = GetCustomSegments(creature);
        if (customSegments.Length == 0)
        {
            HideAllCustomSegments(healthBar);
            return;
        }

        if (!EnsureUiState(healthBar))
            return;

        var state = UiStates[healthBar];
        if (state == null)
            return;
        EnsureOverlayOrder(healthBar, state);

        var maxWidth = GetMaxFgWidth(healthBar);
        var hpForeground = healthBar._hpForeground;
        var hpFromForeground =
            Math.Clamp(HpFromOffsetRight(healthBar, hpForeground.OffsetRight), 0, creature.CurrentHp);
        var baseHp = hpForeground.Visible || hpFromForeground < creature.CurrentHp ? hpFromForeground : 0;

        var rightSegments = customSegments
            .Where(segment => segment.Direction == HealthBarForecastDirection.FromRight)
            .OrderBy(segment => segment.Order)
            .ThenBy(segment => segment.SequenceOrder)
            .ToArray();

        var remainingHp = baseHp;
        var rightForecastEdgeOffsetRight = hpForeground.OffsetRight;
        Color? lethalRightColor = null;
        var rightIndex = 0;

        foreach (var segment in rightSegments)
        {
            if (remainingHp <= 0)
                break;

            var visibleAmount = Math.Min(segment.Amount, remainingHp);
            if (visibleAmount <= 0)
                continue;

            EnsureSegmentCount(state.RightSegments, state.RightContainer, rightIndex + 1, state.RightTemplate);
            var node = state.RightSegments[rightIndex];
            var previousHp = remainingHp;
            remainingHp -= visibleAmount;

            var leftWidth = GetFgWidth(healthBar, remainingHp);
            var rightWidth = GetFgWidth(healthBar, previousHp);
            node.Visible = true;
            ApplyForecastSegmentAppearance(node, segment.Color, segment.OverlayMaterial, segment.OverlaySelfModulate);
            node.OffsetLeft = remainingHp > 0 ? Math.Max(0f, leftWidth - node.PatchMarginLeft) : 0f;
            node.OffsetRight = rightWidth - maxWidth;

            if (rightIndex == 0)
                rightForecastEdgeOffsetRight = node.OffsetRight;

            if (remainingHp <= 0)
                lethalRightColor = segment.Color;

            rightIndex++;
        }

        HideSegments(state.RightSegments, rightIndex);

        if (rightIndex > 0)
        {
            if (remainingHp > 0)
            {
                hpForeground.Visible = true;
                hpForeground.OffsetRight = GetFgWidth(healthBar, remainingHp) - maxWidth;
            }
            else
            {
                hpForeground.Visible = false;
            }

            var doomForeground = healthBar._doomForeground;
            if (doomForeground.Visible)
            {
                if (remainingHp > 0)
                    doomForeground.OffsetRight = Math.Min(doomForeground.OffsetRight, hpForeground.OffsetRight);
                else
                    doomForeground.Visible = false;
            }
        }

        if (remainingHp <= 0)
        {
            HideSegments(state.LeftSegments);
            state.LastRender =
                new HealthBarForecastRenderResult(true, rightForecastEdgeOffsetRight, lethalRightColor, null);
            return;
        }

        var leftSegments = customSegments
            .Where(segment => segment.Direction == HealthBarForecastDirection.FromLeft)
            .OrderBy(segment => segment.Order)
            .ThenBy(segment => segment.SequenceOrder)
            .ToArray();

        var leftAccumulated = 0;
        var leftIndex = 0;

        foreach (var segment in leftSegments)
        {
            if (leftAccumulated >= remainingHp)
                break;

            var segmentStart = leftAccumulated;
            leftAccumulated = Math.Min(remainingHp, leftAccumulated + segment.Amount);
            if (leftAccumulated <= segmentStart)
                continue;

            EnsureSegmentCount(state.LeftSegments, state.LeftContainer, leftIndex + 1, state.LeftTemplate);
            var node = state.LeftSegments[leftIndex];
            var startWidth = GetFgWidth(healthBar, segmentStart);
            var endWidth = GetFgWidth(healthBar, leftAccumulated);

            node.Visible = true;
            ApplyForecastSegmentAppearance(node, segment.Color, segment.OverlayMaterial, segment.OverlaySelfModulate);
            node.OffsetLeft = segmentStart > 0 ? Math.Max(0f, startWidth - node.PatchMarginLeft) : 0f;
            var leftOffsetRight = Math.Min(0f, endWidth - maxWidth + node.PatchMarginRight);
            if (rightIndex > 0)
                leftOffsetRight = Math.Min(leftOffsetRight, rightForecastEdgeOffsetRight);
            node.OffsetRight = leftOffsetRight;

            leftIndex++;
        }

        HideSegments(state.LeftSegments, leftIndex);
        var lethalLeftColor = ResolveLeftLethalColor(creature, remainingHp, leftSegments);
        state.LastRender = new HealthBarForecastRenderResult(rightIndex > 0, rightForecastEdgeOffsetRight,
            lethalRightColor, lethalLeftColor);
    }

    private static void RefreshMiddlegroundOverlay(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state == null || !state.LastRender.HasRightForecast)
            return;

        var creature = healthBar._creature;
        if (creature.CurrentHp <= 0 || creature.HpDisplay.IsInfinite())
            return;

        var hpMiddleground = healthBar._hpMiddleground;
        var targetOffsetRight = state.LastRender.RightForecastEdgeOffsetRight;
        var shouldAnimateImmediately = targetOffsetRight >= hpMiddleground.OffsetRight;
        hpMiddleground.OffsetRight += 1f;

        healthBar._middlegroundTween?.Kill();
        var tween = healthBar.CreateTween();
        tween.TweenProperty(hpMiddleground, "offset_right", targetOffsetRight - 2f, 1.0)
            .SetDelay(shouldAnimateImmediately ? 0.0 : 1.0)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        healthBar._middlegroundTween = tween;
    }

    private static void RefreshTextOverlay(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state == null)
            return;

        var creature = healthBar._creature;
        if (creature.CurrentHp <= 0 || creature.HpDisplay.IsInfinite())
            return;

        var lethalColor = state.LastRender.LethalRightColor ?? state.LastRender.LethalLeftColor;
        var hpLabel = healthBar._hpLabel;
        if (!lethalColor.HasValue)
        {
            if (!IsDoomLethalAfterRight(healthBar, creature))
                return;
            hpLabel.AddThemeColorOverride("font_color", DoomLethalTextColor);
            hpLabel.AddThemeColorOverride("font_outline_color", DoomLethalOutlineColor);
            return;
        }

        hpLabel.AddThemeColorOverride("font_color", lethalColor.Value);
        hpLabel.AddThemeColorOverride("font_outline_color", DarkenForOutline(lethalColor.Value));
    }

    private static CustomSegment[] GetCustomSegments(Creature creature)
    {
        return HealthBarForecastRegistry.GetSegments(creature)
            .Select(registered => new CustomSegment(
                registered.Segment.Amount,
                registered.Segment.Color,
                registered.Segment.Direction,
                registered.Segment.Order,
                registered.SequenceOrder,
                registered.Segment.OverlayMaterial,
                registered.Segment.OverlaySelfModulate))
            .Where(segment => segment.Amount > 0)
            .ToArray();
    }

    private static void HideAllCustomSegments(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state == null)
            return;

        HideSegments(state.RightSegments);
        HideSegments(state.LeftSegments);
        state.LastRender = HealthBarForecastRenderResult.Empty;
    }

    private static bool EnsureUiState(NHealthBar healthBar)
    {
        if (UiStates[healthBar] != null)
            return true;

        if (healthBar._poisonForeground is not NinePatchRect poisonForeground)
            return false;

        if (healthBar._doomForeground is not NinePatchRect doomForeground)
            return false;

        if (poisonForeground.GetParent() is not Control mask)
            return false;

        var rightContainer = CreateContainer("BaseLibForecastRightContainer");
        var leftContainer = CreateContainer("BaseLibForecastLeftContainer");

        mask.AddChild(rightContainer);
        mask.AddChild(leftContainer);

        UiStates[healthBar] = new HealthBarForecastUiState(
            rightContainer,
            leftContainer,
            CreateSegmentTemplate(poisonForeground, "BaseLibForecastRightTemplate"),
            CreateSegmentTemplate(doomForeground, "BaseLibForecastLeftTemplate"),
            []);
        return true;
    }

    private static Control CreateContainer(string name)
    {
        var container = new Control
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return container;
    }

    private static NinePatchRect CreateSegmentTemplate(NinePatchRect template, string name)
    {
        var duplicate = (NinePatchRect)template.Duplicate();
        duplicate.Name = name;
        duplicate.Visible = false;
        duplicate.SelfModulate = Colors.White;
        duplicate.Material = null;
        return duplicate;
    }

    private static void EnsureOverlayOrder(NHealthBar healthBar, HealthBarForecastUiState state)
    {
        if (healthBar._poisonForeground is not Control poisonForeground ||
            healthBar._hpForeground is not Control hpForeground ||
            healthBar._doomForeground is not Control doomForeground ||
            poisonForeground.GetParent() is not Control mask)
            return;

        // Right forecast should override poison, but still be clipped by HP.
        var poisonIndex = poisonForeground.GetIndex();
        var hpIndex = hpForeground.GetIndex();
        var rightTargetIndex = Math.Clamp(poisonIndex + 1, 0, hpIndex);
        mask.MoveChild(state.RightContainer, rightTargetIndex);

        // Left forecast should override doom-like overlays.
        var doomIndex = doomForeground.GetIndex();
        var childCount = mask.GetChildCount();
        var leftTargetIndex = Math.Clamp(doomIndex + 1, 0, Math.Max(0, childCount - 1));
        mask.MoveChild(state.LeftContainer, leftTargetIndex);
    }

    private static void EnsureSegmentCount(
        List<NinePatchRect> segments,
        Control container,
        int requiredCount,
        NinePatchRect template)
    {
        while (segments.Count < requiredCount)
        {
            var segment = (NinePatchRect)template.Duplicate();
            segment.Name = $"BaseLibForecastSegment{segments.Count}";
            segment.Visible = false;
            container.AddChild(segment);
            segments.Add(segment);
        }
    }

    private static void HideSegments(IEnumerable<NinePatchRect> segments, int startIndex = 0)
    {
        var index = 0;
        foreach (var segment in segments)
        {
            if (index++ < startIndex)
                continue;

            segment.Visible = false;
            segment.Material = null;
            segment.SelfModulate = Colors.White;
        }
    }

    /// <summary>
    ///     Applies material and <see cref="CanvasItem.SelfModulate" />; overlay modulate defaults to <paramref name="color" />
    ///     when <paramref name="overlaySelfModulate" /> is null.
    /// </summary>
    private static void ApplyForecastSegmentAppearance(
        NinePatchRect node,
        Color color,
        Material? overlayMaterial,
        Color? overlaySelfModulate)
    {
        node.Material = overlayMaterial;
        node.SelfModulate = overlaySelfModulate ?? color;
    }

    private static float GetMaxFgWidth(NHealthBar healthBar)
    {
        var expectedMaxFgWidth = healthBar._expectedMaxFgWidth;
        return expectedMaxFgWidth > 0f
            ? expectedMaxFgWidth
            : healthBar._hpForegroundContainer.Size.X;
    }

    private static float GetFgWidth(NHealthBar healthBar, int amount)
    {
        var creature = healthBar._creature;
        if (creature.MaxHp <= 0 || amount <= 0)
            return 0f;

        var width = (float)amount / creature.MaxHp * GetMaxFgWidth(healthBar);
        return Math.Max(width, creature.CurrentHp > 0 ? 12f : 0f);
    }

    private static int HpFromOffsetRight(NHealthBar healthBar, float offsetRight)
    {
        var creature = healthBar._creature;
        if (creature.MaxHp <= 0)
            return 0;

        var maxWidth = GetMaxFgWidth(healthBar);
        if (maxWidth <= 0f)
            return 0;

        var width = Math.Clamp(offsetRight + maxWidth, 0f, maxWidth);
        return (int)Math.Round(width / maxWidth * creature.MaxHp);
    }

    private static Color DarkenForOutline(Color color)
    {
        return new Color(
            Math.Clamp(color.R * 0.3f, 0f, 1f),
            Math.Clamp(color.G * 0.3f, 0f, 1f),
            Math.Clamp(color.B * 0.3f, 0f, 1f));
    }

    private static bool IsDoomLethalAfterRight(NHealthBar healthBar, Creature creature)
    {
        var doomAmount = creature.GetPowerAmount<DoomPower>();
        if (doomAmount <= 0)
            return false;

        var hpAfterRight = Math.Clamp(
            HpFromOffsetRight(healthBar, healthBar._hpForeground.OffsetRight),
            0,
            creature.CurrentHp);
        return hpAfterRight > 0 && doomAmount >= hpAfterRight;
    }

    private static Color? ResolveLeftLethalColor(Creature creature, int remainingHp,
        IReadOnlyList<CustomSegment> leftSegments)
    {
        if (remainingHp <= 0)
            return null;

        List<LethalCandidate> candidates = [];
        foreach (var segment in leftSegments)
        {
            if (segment.Amount <= 0)
                continue;
            candidates.Add(new LethalCandidate(segment.Amount, segment.Color, segment.Order, segment.SequenceOrder));
        }

        var doomAmount = creature.GetPowerAmount<DoomPower>();
        if (doomAmount > 0)
            candidates.Add(new LethalCandidate(doomAmount, DoomLethalTextColor, 0, long.MinValue / 4));

        if (candidates.Count == 0)
            return null;

        var ordered = candidates
            .OrderBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.SequenceOrder);

        var accumulated = 0;
        foreach (var candidate in ordered)
        {
            accumulated = Math.Min(remainingHp, accumulated + candidate.Amount);
            if (accumulated >= remainingHp)
                return candidate.Color;
        }

        return null;
    }

    private sealed class HealthBarForecastUiState(
        Control rightContainer,
        Control leftContainer,
        NinePatchRect rightTemplate,
        NinePatchRect leftTemplate,
        List<NinePatchRect> rightSegments)
    {
        public Control RightContainer { get; } = rightContainer;
        public Control LeftContainer { get; } = leftContainer;
        public NinePatchRect RightTemplate { get; } = rightTemplate;
        public NinePatchRect LeftTemplate { get; } = leftTemplate;
        public List<NinePatchRect> RightSegments { get; } = rightSegments;
        public List<NinePatchRect> LeftSegments { get; } = [];
        public HealthBarForecastRenderResult LastRender { get; set; } = HealthBarForecastRenderResult.Empty;
    }

    private readonly record struct CustomSegment(
        int Amount,
        Color Color,
        HealthBarForecastDirection Direction,
        int Order,
        long SequenceOrder,
        Material? OverlayMaterial,
        Color? OverlaySelfModulate);

    private readonly record struct LethalCandidate(
        int Amount,
        Color Color,
        int Order,
        long SequenceOrder);

    private readonly record struct HealthBarForecastRenderResult(
        bool HasRightForecast,
        float RightForecastEdgeOffsetRight,
        Color? LethalRightColor,
        Color? LethalLeftColor)
    {
        public static HealthBarForecastRenderResult Empty => new(false, 0f, null, null);
    }
}