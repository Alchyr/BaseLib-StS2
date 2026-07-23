using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace BaseLib.Hooks;

internal static class ModCardHandOutlinePatchHelper
{
    internal static bool TryGetRule(NHandCardHolder? holder, out CardModel model, out ModCardHandOutlineRule rule)
    {
        model = null!;
        rule = default;

        if (!TryGetCardModel(holder, out var m))
            return false;

        var evaluated = ModCardHandOutlineRegistry.EvaluateBest(m);
        if (evaluated is not { } r)
            return false;

        model = m;
        rule = r;
        return true;
    }

    internal static void ApplyHighlight(NHandCardHolder? holder, CardModel model, ModCardHandOutlineRule rule)
    {
        if (CombatManager.Instance is not { IsInProgress: true } ||
            !TryGetCardModel(holder, out var currentModel) ||
            !ReferenceEquals(currentModel, model))
            return;

        try
        {
            var cardNode = holder!.CardNode;
            if (cardNode == null || !GodotObject.IsInstanceValid(cardNode) ||
                !GodotObject.IsInstanceValid(cardNode.CardHighlight))
                return;

            var vanillaShow = model.CanPlay() || model.ShouldGlowRed || model.ShouldGlowGold;
            var force = rule.VisibleWhenUnplayable && !vanillaShow;
            if (!vanillaShow && !force)
                return;

            var highlight = cardNode.CardHighlight;
            if (force)
                highlight.AnimShow();

            highlight.Modulate = rule.ResolveColor(model);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal static void ApplyFlash(NHandCardHolder? holder, CardModel model, ModCardHandOutlineRule rule)
    {
        if (!IsHolderUsable(holder))
            return;

        try
        {
            if (AccessTools.Field(typeof(NHandCardHolder), "_flash")?.GetValue(holder!) is not Control flash ||
                !GodotObject.IsInstanceValid(flash))
                return;

            flash.Modulate = rule.ResolveColor(model);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal static bool TryGetUsableTree(NHandCardHolder? holder, out SceneTree tree)
    {
        tree = null!;

        if (!IsHolderUsable(holder))
            return false;

        try
        {
            if (holder!.GetTree() is not { } t || !GodotObject.IsInstanceValid(t))
                return false;

            tree = t;
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool TryGetCardModel(NHandCardHolder? holder, out CardModel model)
    {
        model = null!;

        if (!IsHolderUsable(holder))
            return false;

        try
        {
            if (holder!.CardNode is not { } cardNode ||
                !GodotObject.IsInstanceValid(cardNode) ||
                cardNode.Model is not { } m)
                return false;

            model = m;
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsHolderUsable(NHandCardHolder? holder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder))
            return false;

        try
        {
            return holder.IsNodeReady() && holder.IsInsideTree();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
