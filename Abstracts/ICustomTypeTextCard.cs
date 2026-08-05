using BaseLib.Hooks;
using MegaCrit.Sts2.Core.Localization;

namespace BaseLib.Abstracts;

/// <summary>
/// Interface that can be implemented on a card class to have that card visually modify its own type text.
/// To make models other than cards modify card type text, see <see cref="ICardTypeTextModifier"/>.
/// </summary>
public interface ICustomTypeTextCard
{
    /// <summary>
    /// Return a list of <c>LocString</c>s.
    /// Each of these strings has access to the formatting argument <c>{Type}</c>, which will be replaced by the original type text.
    /// </summary>
    /// <returns>A list of <c>LocString</c>s, each of which should point to a string for a card type text modification.</returns>
    public IEnumerable<LocString> GetTypeModifiers();
}
