using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace BaseLib.Patches.UI;

[HarmonyPatch]
internal static class CharacterSelectStartingRelicsPatch
{
    private const float HolderSize = 68f;
    private const int HolderSeparation = -8;
    private const float DefaultRowScale = 0.75f;
    private const float MaxRowWidth = 220f;
    private const float RowTop = 8f;
    private const float TextPadding = 8f;

    private static readonly SpireField<NCharacterSelectScreen, StartingRelicRowState> ScreenStates = new(() => new());

    [HarmonyPatch(typeof(NRelicBasicHolder), "OnFocus")]
    [HarmonyPostfix]
    private static void RelicHolderOnFocusPostfix(NRelicBasicHolder __instance)
    {
        if (__instance.GetParent()?.Name == "BaseLibStartingRelics")
        {
            NHoverTipSet.Remove(__instance);
            if (NHoverTipSet.CreateAndShow(__instance, __instance.Relic.Model.HoverTips) is { } tipSet)
            {
                tipSet.SetAlignmentForRelic(__instance.Relic);
                tipSet.SetFollowOwner();
            }
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
    [HarmonyPostfix]
    private static void SelectCharacterPostfix(
        NCharacterSelectScreen __instance,
        NCharacterSelectButton charSelectButton,
        CharacterModel characterModel)
    {
        if (charSelectButton.IsLocked || charSelectButton.IsRandom)
        {
            Clear(__instance, restoreControllerFocus: true);
            return;
        }

        ApplyForCharacter(__instance, characterModel, charSelectButton);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    [HarmonyPrefix]
    private static void OnSubmenuOpenedPrefix(NCharacterSelectScreen __instance)
    {
        Clear(__instance, restoreControllerFocus: false);
    }

    // Runs after vanilla auto-selects a character and after other postfixes rebuild char-button
    // neighbors, so multi-relic focus links are not left pointing at the button itself.
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void OnSubmenuOpenedPostfix(NCharacterSelectScreen __instance)
    {
        RebindFocusNeighbors(__instance);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
    [HarmonyPrefix]
    private static void OnSubmenuClosedPrefix(NCharacterSelectScreen __instance)
    {
        Clear(__instance, restoreControllerFocus: false);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
    [HarmonyPrefix]
    private static void OnEmbarkPressedPrefix(NCharacterSelectScreen __instance)
    {
        var state = ScreenStates.Get(__instance);
        var focusOwner = __instance.GetViewport()?.GuiGetFocusOwner();
        if (state != null && focusOwner != null && state.Holders.Any(holder => holder == focusOwner) &&
            IsValid(state.FocusOrigin))
        {
            state.FocusOrigin!.GrabFocus();
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
    [HarmonyPostfix]
    private static void OnEmbarkPressedPostfix(NCharacterSelectScreen __instance)
    {
        if (__instance.Lobby.LocalPlayer.isReady)
        {
            SetInteractionEnabled(__instance, enabled: false);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnUnreadyPressed")]
    [HarmonyPostfix]
    private static void OnUnreadyPressedPostfix(NCharacterSelectScreen __instance)
    {
        SetInteractionEnabled(__instance, enabled: true);
    }

    internal static void ApplyForCharacter(
        NCharacterSelectScreen screen,
        CharacterModel character,
        Control? focusOrigin)
    {
        Clear(screen, restoreControllerFocus: true);

        if (character is not ICustomModel) return;

        var relics = character.StartingRelics;
        if (relics.Count <= 1)
        {
            return;
        }

        var relicPanel = screen.GetNodeOrNull<Control>("InfoPanel/VBoxContainer/Relic");
        var titleContainer = screen._relicTitle.GetParent() as Control;
        if (relicPanel == null || titleContainer == null)
        {
            BaseLibMain.Logger.Warn($"Unable to add starting relics to the character select panel for {character.Id}.");
            return;
        }

        var holders = new List<NRelicBasicHolder>(relics.Count);
        try
        {
            foreach (var relic in relics)
            {
                var holder = NRelicBasicHolder.Create(relic);
                if (holder == null)
                {
                    foreach (var createdHolder in holders)
                    {
                        createdHolder.QueueFreeSafely();
                    }
                    return;
                }

                holders.Add(holder);
            }

            var unscaledWidth = HolderSize * holders.Count + HolderSeparation * (holders.Count - 1);
            var rowScale = Math.Min(DefaultRowScale, MaxRowWidth / unscaledWidth);
            var visualWidth = unscaledWidth * rowScale;
            var row = new HBoxContainer
            {
                Name = "BaseLibStartingRelics",
                LayoutMode = 0,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(unscaledWidth, HolderSize),
                Position = new Vector2(0f, RowTop),
                Size = new Vector2(unscaledWidth, HolderSize),
                Scale = Vector2.One * rowScale
            };
            row.AddThemeConstantOverride("separation", HolderSeparation);

            var state = ScreenStates.Get(screen)!;
            CaptureOriginalLayout(screen, state, titleContainer);
            state.Row = row;
            state.FocusOrigin = IsValid(focusOrigin) ? focusOrigin : null;
            if (state.FocusOrigin != null)
            {
                state.OriginalFocusNeighborTop = state.FocusOrigin.FocusNeighborTop;
            }
            state.Holders.AddRange(holders);

            foreach (var (holder, relic) in holders.Zip(relics))
            {
                holder.MouseDefaultCursorShape = Control.CursorShape.Help;
                holder.Released += _ =>
                {
                    var game = NGame.Instance;
                    if (game == null)
                    {
                        return;
                    }

                    game.GetInspectRelicScreen().Open(relics, relic);
                };
                holder.Focused += _ =>
                {
                    SetRelicDetails(screen, relic);
                    if (IsValid(state.FocusOrigin) && IsValid(holder))
                    {
                        state.FocusOrigin!.FocusNeighborTop = holder.GetPath();
                    }
                };
                row.AddChildSafely(holder);
            }

            relicPanel.AddChildSafely(row);
            ApplyLayout(state, visualWidth);
            RebuildFocusNeighbors(state);
            SetRelicDetails(screen, relics[0]);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Failed to build the character select starting relic row for {character.Id}: {e}");
            Clear(screen, restoreControllerFocus: true);
            foreach (var holder in holders)
            {
                if (IsValid(holder) && holder.GetParent() == null)
                {
                    holder.QueueFreeSafely();
                }
            }
        }
    }

    internal static void Clear(NCharacterSelectScreen screen, bool restoreControllerFocus)
    {
        var state = ScreenStates.Get(screen);
        if (state == null)
        {
            return;
        }

        var focusOwner = screen.GetViewport()?.GuiGetFocusOwner();
        var holderHadFocus = focusOwner != null && state.Holders.Any(holder => holder == focusOwner);

        if (state.LayoutModified)
        {
            if (IsValid(state.TitleContainer))
            {
                state.TitleContainer!.OffsetLeft = state.OriginalTitleOffsetLeft;
            }
            if (IsValid(state.Description))
            {
                state.Description!.OffsetLeft = state.OriginalDescriptionOffsetLeft;
            }
            if (IsValid(state.LegacyIcon))
            {
                state.LegacyIcon!.Visible = state.LegacyIconWasVisible;
            }
            state.LayoutModified = false;
        }

        if (IsValid(state.FocusOrigin))
        {
            state.FocusOrigin!.FocusNeighborTop = state.OriginalFocusNeighborTop;
            if (restoreControllerFocus && holderHadFocus && state.FocusOrigin.IsVisibleInTree() &&
                state.FocusOrigin.FocusMode != Control.FocusModeEnum.None)
            {
                state.FocusOrigin.GrabFocus();
            }
        }

        foreach (var holder in state.Holders)
        {
            if (!IsValid(holder))
            {
                continue;
            }

            holder.Disable();
            NHoverTipSet.Remove(holder);
        }

        if (IsValid(state.Row))
        {
            var parent = state.Row!.GetParent();
            if (parent != null)
            {
                parent.RemoveChildSafely(state.Row);
            }
            state.Row.QueueFreeSafely();
        }

        state.Row = null;
        state.Holders.Clear();
        state.FocusOrigin = null;
        state.OriginalFocusNeighborTop = new NodePath();
    }

    /// <summary>
    /// Re-applies focus neighbors from the character button into the multi-relic row.
    /// Call after any code that rebuilds character-select button neighbors.
    /// </summary>
    internal static void RebindFocusNeighbors(NCharacterSelectScreen screen)
    {
        var state = ScreenStates.Get(screen);
        if (state == null || state.Holders.Count == 0)
        {
            return;
        }

        RebuildFocusNeighbors(state);
    }

    private static void SetRelicDetails(NCharacterSelectScreen screen, RelicModel relic)
    {
        screen._relicTitle.Text = relic.Title.GetFormattedText();
        screen._relicDescription.Text = relic.DynamicDescription.GetFormattedText();
    }

    internal static void SetInteractionEnabled(NCharacterSelectScreen screen, bool enabled)
    {
        var state = ScreenStates.Get(screen);
        if (state == null)
        {
            return;
        }

        if (!enabled && !IsValid(state.FocusOrigin))
        {
            Clear(screen, restoreControllerFocus: false);
            return;
        }

        if (!enabled)
        {
            var focusOwner = screen.GetViewport()?.GuiGetFocusOwner();
            if (focusOwner != null && state.Holders.Any(holder => holder == focusOwner) && IsValid(state.FocusOrigin))
            {
                state.FocusOrigin!.GrabFocus();
            }
        }

        foreach (var holder in state.Holders)
        {
            if (IsValid(holder))
            {
                holder.MouseDefaultCursorShape = enabled ? Control.CursorShape.Help : Control.CursorShape.Arrow;
                holder.SetEnabled(enabled);
            }
        }
    }

    private static void CaptureOriginalLayout(
        NCharacterSelectScreen screen,
        StartingRelicRowState state,
        Control titleContainer)
    {
        state.TitleContainer = titleContainer;
        state.Description = screen._relicDescription;
        state.LegacyIcon = screen._relicIcon;

        // Capture scene-original offsets once per screen so a partial restore cannot
        // permanently shift the title/description further right on the next apply.
        if (!state.OriginalsCaptured)
        {
            state.OriginalTitleOffsetLeft = titleContainer.OffsetLeft;
            state.OriginalDescriptionOffsetLeft = screen._relicDescription.OffsetLeft;
            state.LegacyIconWasVisible = screen._relicIcon.Visible;
            state.OriginalsCaptured = true;
        }
    }

    private static void ApplyLayout(
        StartingRelicRowState state,
        float visualWidth)
    {
        var textOffset = Math.Max(state.OriginalTitleOffsetLeft, visualWidth + TextPadding);
        state.TitleContainer!.OffsetLeft = textOffset;
        state.Description!.OffsetLeft = Math.Max(state.OriginalDescriptionOffsetLeft, textOffset);
        state.LegacyIcon!.Visible = false;
        state.LayoutModified = true;
    }

    private static void RebuildFocusNeighbors(StartingRelicRowState state)
    {
        if (state.Holders.Count == 0)
        {
            return;
        }

        for (var i = 0; i < state.Holders.Count; i++)
        {
            var holder = state.Holders[i];
            holder.FocusNeighborLeft = state.Holders[(i - 1 + state.Holders.Count) % state.Holders.Count].GetPath();
            holder.FocusNeighborRight = state.Holders[(i + 1) % state.Holders.Count].GetPath();
            holder.FocusNeighborTop = holder.GetPath();
            holder.FocusNeighborBottom = IsValid(state.FocusOrigin)
                ? state.FocusOrigin!.GetPath()
                : holder.GetPath();
        }

        if (IsValid(state.FocusOrigin))
        {
            state.FocusOrigin!.FocusNeighborTop = state.Holders[0].GetPath();
        }
    }

    private static bool IsValid(GodotObject? instance)
    {
        return instance != null && GodotObject.IsInstanceValid(instance);
    }

    private sealed class StartingRelicRowState
    {
        public HBoxContainer? Row { get; set; }
        public List<NRelicBasicHolder> Holders { get; } = [];
        public Control? FocusOrigin { get; set; }
        public NodePath OriginalFocusNeighborTop { get; set; } = new();
        public Control? TitleContainer { get; set; }
        public Control? Description { get; set; }
        public Control? LegacyIcon { get; set; }
        public float OriginalTitleOffsetLeft { get; set; }
        public float OriginalDescriptionOffsetLeft { get; set; }
        public bool LegacyIconWasVisible { get; set; }
        public bool OriginalsCaptured { get; set; }
        public bool LayoutModified { get; set; }
    }
}
