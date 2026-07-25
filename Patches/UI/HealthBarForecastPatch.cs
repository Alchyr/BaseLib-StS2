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
///     When no segments apply, vanilla visuals are unchanged. Right-side segments layer above poison; left-side segments
///     share the doom side according to their configured left-origin layout.
/// </remarks>
[HarmonyPatch]
public static class HealthBarForecastPatch
{
    private static readonly SpireField<NHealthBar, HealthBarForecastUiState?> UiStates = new(() => null);

    private static readonly Color DoomLethalTextColor = new("FB8DFF");
    private static readonly Color DoomLethalOutlineColor = new("2D1263");

    [ThreadStatic] private static bool _isRefreshingOverlay;

    [HarmonyPatch(typeof(NHealthBar), "RefreshForeground")]
    [HarmonyPostfix]
    private static void RefreshForegroundPostfix(NHealthBar __instance)
    {
        RunOverlayRefresh(__instance);
    }

    [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately")]
    [HarmonyPostfix]
    private static void SetHpBarContainerSizeWithOffsetsImmediatelyPostfix(NHealthBar __instance)
    {
        if (__instance._creature == null)
            return;

        if (!RunOverlayRefresh(__instance))
            return;

        SnapMiddlegroundToForecast(__instance);
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

    private static bool RunOverlayRefresh(NHealthBar healthBar)
    {
        if (_isRefreshingOverlay)
            return false;

        _isRefreshingOverlay = true;
        try
        {
            RefreshForegroundOverlay(healthBar);
            return true;
        }
        finally
        {
            _isRefreshingOverlay = false;
        }
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
        var visualDenom = creature.MaxHp;
        var hpForeground = healthBar._hpForeground;
        var poisonDamage = Math.Max(0, creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0);
        var baseHp = Math.Max(0, creature.CurrentHp - poisonDamage);

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

            var leftWidth = GetFgWidth(healthBar, remainingHp, visualDenom);
            var rightWidth = GetFgWidth(healthBar, previousHp, visualDenom);
            node.Visible = true;
            ApplyForecastSegmentAppearance(node, segment.Color, segment.OverlayMaterial, segment.OverlaySelfModulate);
            node.OffsetLeft = remainingHp > 0 ? Math.Max(0f, leftWidth - node.PatchMarginLeft) : 0f;
            node.OffsetRight = rightWidth - maxWidth;

            if (rightIndex == 0)
                rightForecastEdgeOffsetRight = node.OffsetRight;

            if (remainingHp <= 0)
                lethalRightColor = segment.AffectsHpLabel ? segment.Color : null;

            rightIndex++;
        }

        HideSegments(state.RightSegments, rightIndex);

        if (rightIndex > 0)
        {
            if (remainingHp > 0)
            {
                hpForeground.Visible = true;
                hpForeground.OffsetRight = GetFgWidth(healthBar, remainingHp, visualDenom) - maxWidth;
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
            state.OverlapLeftZ.Clear();
            state.LastRender = new(true, rightForecastEdgeOffsetRight, lethalRightColor, null, 0);
            return;
        }

        var leftSegments = customSegments
            .Where(segment => segment.Direction == HealthBarForecastDirection.FromLeft)
            .OrderBy(segment => segment.Order)
            .ThenBy(segment => segment.SequenceOrder)
            .ToArray();

        state.OverlapLeftZ.Clear();
        var leftIndex = 0;
        var chainedLeft = leftSegments
            .Where(segment => segment.LeftOriginLayout == HealthBarForecastLeftOriginLayout.Chained)
            .ToArray();
        PlaceChainedLeftSegments(
            healthBar,
            state,
            chainedLeft,
            remainingHp,
            maxWidth,
            rightIndex,
            rightForecastEdgeOffsetRight,
            visualDenom,
            ref leftIndex);

        var overlapLeft = leftSegments
            .Where(segment => segment.LeftOriginLayout == HealthBarForecastLeftOriginLayout.OverlapFromOrigin)
            .ToArray();
        PlaceOverlapLeftSegments(
            healthBar,
            state,
            overlapLeft,
            remainingHp,
            maxWidth,
            rightIndex,
            rightForecastEdgeOffsetRight,
            visualDenom,
            ref leftIndex);

        HideSegments(state.LeftSegments, leftIndex);
        var lethalLeftColor = ResolveLeftLethalColor(creature, remainingHp, leftSegments, state.OverlapLeftZ);
        state.LastRender =
            new(rightIndex > 0, rightForecastEdgeOffsetRight, lethalRightColor, lethalLeftColor, remainingHp);
    }

    private static void RefreshMiddlegroundOverlay(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state == null)
            return;

        if (!state.LastRender.HasRightForecast)
        {
            state.MiddlegroundTweenTarget = null;
            return;
        }

        var creature = healthBar._creature;
        if (creature.CurrentHp <= 0 || creature.HpDisplay.IsInfinite())
            return;

        var hpMiddleground = healthBar._hpMiddleground;
        var targetOffsetRight = state.LastRender.RightForecastEdgeOffsetRight;
        var hpChanged = creature.CurrentHp != state.MiddlegroundHpOnLastTween ||
                        creature.MaxHp != state.MiddlegroundMaxHpOnLastTween;
        var targetChanged = state.MiddlegroundTweenTarget is not { } lastTarget ||
                            !Mathf.IsEqualApprox(lastTarget, targetOffsetRight);
        if (!hpChanged && !targetChanged)
            return;

        state.MiddlegroundHpOnLastTween = creature.CurrentHp;
        state.MiddlegroundMaxHpOnLastTween = creature.MaxHp;
        state.MiddlegroundTweenTarget = targetOffsetRight;

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

    private static void SnapMiddlegroundToForecast(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state == null || !state.LastRender.HasRightForecast)
            return;

        var creature = healthBar._creature;
        if (creature.CurrentHp <= 0 || BetaMainCompatibility.Creature_.ShowsInfiniteHp(creature))
            return;

        var targetOffsetRight = state.LastRender.RightForecastEdgeOffsetRight;
        healthBar._middlegroundTween?.Kill();
        healthBar._hpMiddleground.OffsetRight = targetOffsetRight - 2f;
        state.MiddlegroundHpOnLastTween = creature.CurrentHp;
        state.MiddlegroundMaxHpOnLastTween = creature.MaxHp;
        state.MiddlegroundTweenTarget = targetOffsetRight;
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

    private static void PlaceChainedLeftSegments(
        NHealthBar healthBar,
        HealthBarForecastUiState state,
        CustomSegment[] chainedOrdered,
        int remainingHp,
        float maxWidth,
        int rightIndex,
        float rightForecastEdgeOffsetRight,
        int visualDenom,
        ref int leftIndex)
    {
        var leftAccumulated = 0;
        foreach (var segment in chainedOrdered)
        {
            if (leftAccumulated >= remainingHp)
                break;

            var segmentStart = leftAccumulated;
            leftAccumulated = Math.Min(remainingHp, leftAccumulated + segment.Amount);
            if (leftAccumulated <= segmentStart)
                continue;

            EnsureSegmentCount(state.LeftSegments, state.LeftContainer, leftIndex + 1, state.LeftTemplate);
            var node = state.LeftSegments[leftIndex];
            var startWidth = GetFgWidth(healthBar, segmentStart, visualDenom);
            var endWidth = GetFgWidth(healthBar, leftAccumulated, visualDenom);

            node.Visible = true;
            ApplyForecastSegmentAppearance(node, segment.Color, segment.OverlayMaterial, segment.OverlaySelfModulate);
            node.OffsetLeft = segmentStart > 0 ? Math.Max(0f, startWidth - node.PatchMarginLeft) : 0f;
            var leftOffsetRight = Math.Min(0f, endWidth - maxWidth + node.PatchMarginRight);
            if (rightIndex > 0)
                leftOffsetRight = Math.Min(leftOffsetRight, rightForecastEdgeOffsetRight);
            node.OffsetRight = leftOffsetRight;

            leftIndex++;
        }
    }

    private static void PlaceOverlapLeftSegments(
        NHealthBar healthBar,
        HealthBarForecastUiState state,
        CustomSegment[] overlapSegments,
        int remainingHp,
        float maxWidth,
        int rightIndex,
        float rightForecastEdgeOffsetRight,
        int visualDenom,
        ref int leftIndex)
    {
        if (overlapSegments.Length == 0)
            return;

        foreach (var group in overlapSegments.GroupBy(segment => segment.LeftExclusiveZGroup).OrderBy(group => group.Key))
        {
            var sorted = group
                .OrderByDescending(segment => segment.Amount)
                .ThenBy(segment => segment.Order)
                .ThenBy(segment => segment.SequenceOrder)
                .ToArray();

            foreach (var segment in sorted)
            {
                var visibleAmount = Math.Min(segment.Amount, remainingHp);
                if (visibleAmount <= 0)
                    continue;

                EnsureSegmentCount(state.LeftSegments, state.LeftContainer, leftIndex + 1, state.LeftTemplate);
                var node = state.LeftSegments[leftIndex];
                var endWidth = GetFgWidth(healthBar, visibleAmount, visualDenom);
                state.OverlapLeftZ.Add((segment, leftIndex));

                node.Visible = true;
                ApplyForecastSegmentAppearance(node, segment.Color, segment.OverlayMaterial, segment.OverlaySelfModulate);
                node.OffsetLeft = 0f;
                var leftOffsetRight = Math.Min(0f, endWidth - maxWidth + node.PatchMarginRight);
                if (rightIndex > 0)
                    leftOffsetRight = Math.Min(leftOffsetRight, rightForecastEdgeOffsetRight);
                node.OffsetRight = leftOffsetRight;

                leftIndex++;
            }
        }
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
                registered.Segment.OverlaySelfModulate,
                registered.Segment.LeftOriginLayout,
                registered.Segment.LeftExclusiveZGroup,
                registered.Segment.AffectsHpLabel))
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
        state.OverlapLeftZ.Clear();
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

        var rightTemplate = CreateSegmentTemplate(poisonForeground, "BaseLibForecastRightTemplate");
        var leftTemplate = CreateSegmentTemplate(doomForeground, "BaseLibForecastLeftTemplate");
        rightContainer.AddChild(rightTemplate);
        leftContainer.AddChild(leftTemplate);

        UiStates[healthBar] = new HealthBarForecastUiState(
            rightContainer,
            leftContainer,
            rightTemplate,
            leftTemplate,
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
        duplicate.Modulate = Colors.White;
        duplicate.SelfModulate = Colors.White;
        duplicate.Material = null;
        duplicate.ZIndex = 0;
        duplicate.MouseFilter = Control.MouseFilterEnum.Ignore;
        duplicate.OffsetLeft = 0f;
        duplicate.OffsetRight = 0f;
        return duplicate;
    }

    private static void EnsureOverlayOrder(NHealthBar healthBar, HealthBarForecastUiState state)
    {
        if (healthBar._poisonForeground is not { } poisonForeground ||
            healthBar._hpForeground is not { } hpForeground ||
            healthBar._doomForeground is not { } doomForeground ||
            poisonForeground.GetParent() is not Control mask)
            return;

        if (poisonForeground.GetIndex() < hpForeground.GetIndex())
            MoveChildAfter(mask, state.RightContainer, poisonForeground);
        else
            MoveChildBefore(mask, state.RightContainer, hpForeground);

        MoveChildBefore(mask, state.LeftContainer, doomForeground);
    }

    private static void MoveChildAfter(Control parent, Control node, Control anchor)
    {
        if (node.GetParent() != parent || anchor.GetParent() != parent)
            return;

        var nodeIndex = node.GetIndex();
        var anchorIndex = anchor.GetIndex();
        var targetIndex = nodeIndex > anchorIndex ? anchorIndex + 1 : anchorIndex;
        if (nodeIndex != targetIndex)
            parent.MoveChild(node, targetIndex);
    }

    private static void MoveChildBefore(Control parent, Control node, Control anchor)
    {
        if (node.GetParent() != parent || anchor.GetParent() != parent)
            return;

        var nodeIndex = node.GetIndex();
        var anchorIndex = anchor.GetIndex();
        var targetIndex = nodeIndex > anchorIndex ? anchorIndex : Math.Max(0, anchorIndex - 1);
        if (nodeIndex != targetIndex)
            parent.MoveChild(node, targetIndex);
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
            segment.ZIndex = 0;
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

    private static float GetFgWidth(NHealthBar healthBar, int amount, int visualDenom)
    {
        var creature = healthBar._creature;
        if (visualDenom <= 0 || amount <= 0)
            return 0f;

        var width = (float)amount / visualDenom * GetMaxFgWidth(healthBar);
        return Math.Max(width, creature.CurrentHp > 0 ? 12f : 0f);
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

        var state = UiStates[healthBar];
        if (state == null || !state.LastRender.HasRightForecast)
            return false;

        var remainingHp = state.LastRender.RemainingHpAfterRight;
        return remainingHp > 0 && doomAmount >= remainingHp;
    }

    private static Color? ResolveLeftLethalColor(
        Creature creature,
        int remainingHp,
        IReadOnlyList<CustomSegment> leftSegments,
        List<(CustomSegment Segment, int DrawIndex)> overlapZ)
    {
        if (remainingHp <= 0)
            return null;

        Color? overlapLethal = null;
        var hasOverlapLethal = false;
        var bestDrawIndex = int.MinValue;
        foreach (var (segment, drawIndex) in overlapZ)
        {
            if (segment.Amount < remainingHp)
                continue;
            if (drawIndex < bestDrawIndex)
                continue;

            bestDrawIndex = drawIndex;
            hasOverlapLethal = true;
            overlapLethal = segment.AffectsHpLabel ? segment.Color : null;
        }

        if (hasOverlapLethal)
            return overlapLethal;

        List<LethalCandidate> candidates = [];
        candidates.AddRange(from segment in leftSegments
            where segment is
            {
                Amount: > 0,
                Direction: HealthBarForecastDirection.FromLeft,
                LeftOriginLayout: HealthBarForecastLeftOriginLayout.Chained
            }
            select new LethalCandidate(segment.Amount, segment.AffectsHpLabel ? segment.Color : null, segment.Order,
                segment.SequenceOrder));

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
        public List<(CustomSegment Segment, int DrawIndex)> OverlapLeftZ { get; } = [];
        public HealthBarForecastRenderResult LastRender { get; set; } = HealthBarForecastRenderResult.Empty;
        public float? MiddlegroundTweenTarget { get; set; }
        public int MiddlegroundHpOnLastTween { get; set; } = -1;
        public int MiddlegroundMaxHpOnLastTween { get; set; } = -1;
    }

    private readonly record struct CustomSegment(
        int Amount,
        Color Color,
        HealthBarForecastDirection Direction,
        int Order,
        long SequenceOrder,
        Material? OverlayMaterial,
        Color? OverlaySelfModulate,
        HealthBarForecastLeftOriginLayout LeftOriginLayout,
        int LeftExclusiveZGroup,
        bool AffectsHpLabel);

    private readonly record struct LethalCandidate(
        int Amount,
        Color? Color,
        int Order,
        long SequenceOrder);

    private readonly record struct HealthBarForecastRenderResult(
        bool HasRightForecast,
        float RightForecastEdgeOffsetRight,
        Color? LethalRightColor,
        Color? LethalLeftColor,
        int RemainingHpAfterRight)
    {
        public static HealthBarForecastRenderResult Empty => new(false, 0f, null, null, 0);
    }
}
