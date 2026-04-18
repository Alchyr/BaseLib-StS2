using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Localization;

namespace BaseLib.Extensions;

public static class CardSelectorPrefsExtensions
{
    public static LocString TransformAndUpgradeSelectionPrompt => new LocString(CardSelectorPrefs._cardSelectionLocFilePath, "TO_TRANSFORM_AND_UPGRADE");
}
