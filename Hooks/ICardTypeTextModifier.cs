using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
/// Interface for visually modifying cards' type text.
/// Intended for use on models other than cards. To have a card modify its own type text, see
/// <see cref="ICustomTypeTextCard" />.
/// </summary>
public interface ICardTypeTextModifier
{
    /// <summary>
    /// Return a list of <c>LocString</c>s.
    /// Each of these strings has access to the formatting argument <c>{Type}</c>, which will be replaced by the original type text.
    /// </summary>
    /// <param name="card">The card to get the type text modifiers for.</param>
    /// <returns>A list of <c>LocString</c>s, each of which should point to a string for a card type text modification.</returns>
    public IEnumerable<LocString> GetTypeModifiers(CardModel card);
}
