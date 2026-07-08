using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Hooks;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateTypePlaque))]
class AddSubtypesToTypePlaquePatch
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddVisualSubtypes(IEnumerable<CodeInstruction> instructions)
    {
        var codeMatcher = new CodeMatcher(instructions);

        codeMatcher
            .MatchStartForward(
                CodeMatch.Calls(typeof(CardTypeExtensions).Method(nameof(CardTypeExtensions.ToLocString))),
                CodeMatch.Calls(typeof(LocString).Method(nameof(LocString.GetFormattedText))),
                CodeMatch.Calls(typeof(MegaLabel).Method(nameof(MegaLabel.SetTextAutoSize)))
            )
            .InsertAfterAndAdvance(
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Call, typeof(NCard).PropertyGetter(nameof(NCard.Model))),
                CodeInstruction.Call(typeof(AddSubtypesToTypePlaquePatch), nameof(TryModifyPlaqueText))
            );

        return codeMatcher.Instructions();
    }

    private const string ArgName = "Type";

    private static LocString TryModifyPlaqueText(LocString originalPlaqueText, CardModel card)
    {
        var runState = card.RunState ?? NullRunState.Instance;
        var combatState = card.CombatState ?? NullCombatState.Instance;

        IEnumerable<LocString> locStringList = [];

        // First check if the card modifies its own type text
        if (card is ICustomTypeTextCard customTypeTextCard)
        {
            locStringList = locStringList.Concat(customTypeTextCard.GetTypeModifiers());
        }

        // Then iterate over all hook listeners and get their type modifiers
        foreach (var source in runState.IterateHookListeners(combatState))
        {
            if (source is not ICardTypeTextModifier visualCardSubtypeSource) continue;
            locStringList = locStringList.Concat(visualCardSubtypeSource.GetTypeModifiers(card));
        }

        // Finally apply all gathered modifiers to the original plaque text
        var previousTypeText = originalPlaqueText;

        foreach (var locString in locStringList)
        {
            if (locString.GetRawText().Contains("{"+ArgName+"}"))
            {
                locString.Add(ArgName, previousTypeText);
                previousTypeText = locString;
            }
            else
            {
                previousTypeText.Add(ArgName, locString);
            }
        }

        // And return
        return previousTypeText;
    }
}
