using Godot;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
///     Custom hand-card outline tint for <see cref="MegaCrit.Sts2.Core.Nodes.Cards.NCardHighlight" /> after vanilla
///     playable / gold / red. Register with <see cref="ModCardHandOutlineRegistry" />.
/// </summary>
/// <param name="When">When this returns true for the card instance, the outline color may apply.</param>
/// <param name="Color">Godot modulate color (alpha is respected; vanilla highlights use ~0.98).</param>
/// <param name="Priority">
///     When several rules match, the highest <paramref name="Priority" /> wins; ties favor the most recently registered
///     rule.
/// </param>
/// <param name="VisibleWhenUnplayable">
///     If true, the highlight is forced visible with this color even when the card is not playable and vanilla would not
///     show gold/red (still only while combat is in progress).
/// </param>
public readonly record struct ModCardHandOutlineRule(
    Func<CardModel, bool> When,
    Color Color,
    int Priority = 0,
    bool VisibleWhenUnplayable = false);