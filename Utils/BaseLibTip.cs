using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.HoverTips;

namespace BaseLib.Utils;

/// <summary>
/// Provides <see cref="StaticHoverTip"/> entries for BaseLib's custom keywords, registered
/// as extensions of the game's hover tip enum via <see cref="CustomEnumAttribute"/>.
/// These resolve their text from the corresponding <c>BASELIB-*</c> localization entries
/// and can be attached to cards, relics, or powers like any built-in hover tip.
/// </summary>
public static class BaseLibTip
{
    /// <summary>
    /// Hover tip for the <b>Scry</b> keyword: look at the top X cards of your Draw Pile
    /// and discard any of them. Text from the <c>BASELIB-SCRY</c> localization entries.
    /// </summary>
    [CustomEnum] public static StaticHoverTip Scry;

    /// <summary>
    /// Hover tip for the <b>Refund</b> keyword: when energy is spent on the card,
    /// up to X of that energy is refunded. Text from the <c>BASELIB-REFUND</c> localization entries.
    /// </summary>
    [CustomEnum] public static StaticHoverTip Refund;

    /// <summary>
    /// Hover tip for the <b>Persist</b> keyword: the card returns to your hand the first
    /// X times it is played each turn. Text from the <c>BASELIB-PERSIST</c> localization entries.
    /// </summary>
    [CustomEnum] public static StaticHoverTip Persist;
}