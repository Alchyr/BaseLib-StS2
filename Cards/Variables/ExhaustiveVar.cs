using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

/// <summary>
/// Defines a <see cref="DynamicVar"/> that marks a card as Exhaustive.
/// </summary>
/// <remarks>
/// Exhaustive cards automatically exhaust after a fixed number of plays within a combat.<br></br>
/// BaseLib will handle displaying a <see cref="HoverTip"/>, and exhausting the card once it has been played enough.<br></br>
/// To display the text on the card, add <c>"[gold]Exhaustive[/gold] {Exhaustive:diff()}"</c> to your card's description.
/// </remarks>
public class ExhaustiveVar : DynamicVar
{
    /// <summary>
    /// The Key to find an Exhaustive var in a <see cref="DynamicVarSet"/>.
    /// </summary>
    public const string Key = "Exhaustive";

    /// <summary>
    /// Create a new <see cref="ExhaustiveVar"/> instance with the count provided.
    /// </summary>
    /// <param name="exhaustiveCount">The number of times the card can be played before it is exhausted.</param>
    public ExhaustiveVar(decimal exhaustiveCount) : base(Key, exhaustiveCount)
    {
        this.WithTooltip();
    }

    /// <inheritdoc />
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        PreviewValue = ExhaustiveCount(card, IntValue);
    }

    /// <summary>
    /// Helper method to determine how many plays remain on an Exhaustive card.
    /// </summary>
    /// <param name="card">The exhaustive card.</param>
    /// <param name="baseExhaustive">The base value from the <see cref="ExhaustiveVar"/> instance.</param>
    /// <returns>The number of plays remaining until this card is exhausted.</returns>
    public static int ExhaustiveCount(CardModel card, int baseExhaustive)
    {
        if (baseExhaustive <= 0)
            return 0;
        int playCount = CombatManager.Instance.History.CardPlaysFinished.Count((entry) => entry.CardPlay.Card == card);
        return Math.Max(1, baseExhaustive - playCount);
    }
}