using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace BaseLib.Cards;

public class BaseLibKeywords
{
    /// See PurgePatch and AfterCardPlayedPatch
    /// <summary>
    /// A card that removes itself from the deck when played.
    /// </summary>
    [CustomEnum] [KeywordProperties(AutoKeywordPosition.After)] public static CardKeyword Purge;

    /// See TempHpPower and TempHpCmd
    /// <summary>
    /// Temporary HP absorbs damage before HP and disappears at the end of combat.
    /// </summary>
    [CustomEnum("Temp_HP")] [KeywordProperties(AutoKeywordPosition.After)] public static CardKeyword TempHp;
}