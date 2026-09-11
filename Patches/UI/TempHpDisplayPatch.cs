using BaseLib.Commands;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.addons.mega_text;

namespace BaseLib.Patches.UI;

/// <summary>
///     Health-bar display for Temporary HP (StSLib-style): a gold outline around the whole bar plus a gold
///     shield chip with the amount, mirroring the Block chip on the opposite (right) end of the bar.
///     All nodes are duplicated from the vanilla scene lazily on first use, so no custom assets are required;
///     vanilla nodes are never modified. Refreshing rides on the existing RefreshValues pipeline — power changes
///     already trigger it via CombatStateTracker (combat bars) and NMultiplayerPlayerState (MP side panel).
/// </summary>
[HarmonyPatch]
public static class TempHpDisplayPatch
{
    private static readonly SpireField<NHealthBar, TempHpUiState?> UiStates = new(() => null);

    private static readonly Color GoldOutlineColor = StsColors.rewardLabelGoldOutline;

    /// StSLib's heart icon (a white heart, tinted gold like StSLib does), shipped in BaseLib.pck.
    private const string HeartTexturePath = "BaseLib/images/ui/temp_hp.png";

    private static Texture2D? _heartTexture;
    private static bool _heartLoadAttempted;

    [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
    [HarmonyPostfix]
    private static void RefreshBlockUiPostfix(NHealthBar __instance)
    {
        RefreshTempHpUi(__instance);
    }

    [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately")]
    [HarmonyPostfix]
    private static void SetHpBarContainerSizeWithOffsetsImmediatelyPostfix(NHealthBar __instance)
    {
        RepositionAfterLayoutChange(__instance);
    }

    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.UpdateLayoutForCreatureBounds))]
    [HarmonyPostfix]
    private static void UpdateLayoutForCreatureBoundsPostfix(NHealthBar __instance)
    {
        RepositionAfterLayoutChange(__instance);
    }

    /// <summary>
    ///     The hidden power contributes no hover tips of its own (invisible powers never do), so the
    ///     "Temporary HP" keyword tip is appended at the creature level and shows when hovering the bar.
    /// </summary>
    [HarmonyPatch(typeof(Creature), nameof(Creature.HoverTips), MethodType.Getter)]
    [HarmonyPostfix]
    private static void HoverTipsPostfix(Creature __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (TempHpCmd.Get(__instance) <= 0)
            return;
        __result = __result.Append(HoverTipFactory.Static(TempHpCmd.TempHp));
    }

    private static void RefreshTempHpUi(NHealthBar healthBar)
    {
        var creature = healthBar._creature;
        if (creature == null)
            return;

        var tempHp = TempHpCmd.Get(creature);
        var state = UiStates[healthBar];
        if (tempHp <= 0 || creature.CurrentHp <= 0 || creature.HpDisplay.IsInfinite())
        {
            if (state != null)
            {
                state.Outline.Visible = false;
                state.Chip.Visible = false;
            }
            return;
        }

        state ??= EnsureUiState(healthBar);
        if (state == null)
            return;

        state.Outline.Visible = true;
        state.Chip.Visible = true;
        state.Label.SetTextAutoSize(tempHp.ToString());
        PositionChip(healthBar, state);
    }

    private static void RepositionAfterLayoutChange(NHealthBar healthBar)
    {
        var state = UiStates[healthBar];
        if (state is { Chip.Visible: true })
            PositionChip(healthBar, state);
    }

    /// <summary>
    ///     Mirror of the vanilla Block chip rule: Block is centered on the bar's left edge
    ///     (see NHealthBar.UpdateLayoutForCreatureBounds), so the Temporary HP chip is centered on the right edge.
    ///     Computed from HpBarContainer in local space, which works for both combat bars (creature-bounds layout)
    ///     and the multiplayer side panel (reference-width layout).
    /// </summary>
    private static void PositionChip(NHealthBar healthBar, TempHpUiState state)
    {
        var barContainer = healthBar.HpBarContainer;
        if (barContainer == null)
            return;
        state.Chip.Position = new Vector2(
            barContainer.Position.X + barContainer.Size.X - state.Chip.Size.X * 0.5f,
            healthBar._originalBlockPosition.Y);
    }

    private static TempHpUiState? EnsureUiState(NHealthBar healthBar)
    {
        if (UiStates[healthBar] is { } existing)
            return existing;

        var blockOutline = healthBar._blockOutline;
        var blockContainer = healthBar._blockContainer;
        if (blockOutline == null || blockContainer == null)
            return null; // _Ready hasn't run yet
        if (blockOutline.GetParent() is not Control barContainer ||
            blockContainer.GetParent() is not Control chipParent)
            return null;

        // Gold outline around the whole bar, layered right above the blue Block outline.
        var outline = (Control)blockOutline.Duplicate();
        outline.Name = "BaseLibTempHpOutline";
        outline.Visible = false;
        outline.Modulate = Colors.White;
        outline.SelfModulate = StsColors.gold;
        outline.MouseFilter = Control.MouseFilterEnum.Ignore;
        barContainer.AddChild(outline);
        barContainer.MoveChild(outline, blockOutline.GetIndex() + 1);

        // Gold shield chip with the amount, duplicated from the Block chip.
        var chip = (Control)blockContainer.Duplicate();
        chip.Name = "BaseLibTempHpContainer";
        chip.Visible = false;
        var icon = chip.GetNodeOrNull<TextureRect>("BlockIcon");
        var label = chip.GetNodeOrNull<MegaLabel>("BlockLabel");
        if (icon == null || label == null)
        {
            outline.QueueFreeSafely();
            chip.QueueFreeSafely();
            return null;
        }
        if (TryLoadHeartTexture() is { } heartTexture)
            icon.Texture = heartTexture; // fallback: keep the duplicated block.png if the pck texture is missing
        icon.SelfModulate = StsColors.gold;
        label.AddThemeColorOverride(ThemeConstants.Label.FontColor, StsColors.gold);
        label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, GoldOutlineColor);
        chip.MouseFilter = Control.MouseFilterEnum.Stop;
        // CreateAndShow does NOT position the tip set itself — vanilla callers set the position explicitly
        // (see NMultiplayerPlayerState's network-problem tip); without this it lands at the screen origin.
        chip.MouseEntered += () => NHoverTipSet.CreateAndShow(chip, HoverTipFactory.Static(TempHpCmd.TempHp))
            ?.SetGlobalPosition(chip.GlobalPosition + Vector2.Down * 80f);
        chip.MouseExited += () => NHoverTipSet.Remove(chip);
        chipParent.AddChild(chip);

        var state = new TempHpUiState(outline, chip, label);
        UiStates[healthBar] = state;
        return state;
    }

    private static Texture2D? TryLoadHeartTexture()
    {
        if (_heartLoadAttempted)
            return _heartTexture;
        _heartLoadAttempted = true;
        try
        {
            _heartTexture = PreloadManager.Cache.GetTexture2D(HeartTexturePath);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Warn($"Failed to load {HeartTexturePath}, falling back to the block icon: {e.Message}");
        }
        return _heartTexture;
    }

    private sealed class TempHpUiState(Control outline, Control chip, MegaLabel label)
    {
        public Control Outline { get; } = outline;
        public Control Chip { get; } = chip;
        public MegaLabel Label { get; } = label;
    }
}
