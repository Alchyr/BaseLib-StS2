using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.UI;

/// <summary>
/// Appends a character stats section for every registered, playable custom character to the
/// stats screen. The base game hardcodes the five vanilla characters in NGeneralStatsGrid.LoadStats,
/// if and only if they have had one run recorded into the saves. Presumably due to character unlocking.
/// Unlike the vanilla sections, custom characters will appear even before their first run (showing
/// zeroed stats), so an installed character is always visible. Otherwise, it is reasonable for a user to report
/// a modded character missing as a bug. 
/// </summary>

[HarmonyPatch(typeof(NGeneralStatsGrid), nameof(NGeneralStatsGrid.LoadStats))]
internal static class CustomCharacterStatsPatch
{
    // Run after any other LoadStats postfix (from other mod's patches, or other libraries like Ritsu) so this
    // sees every section already added and never double-renders a character.
    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void AddCustomCharacterSections(NGeneralStatsGrid __instance)
    {
        if (CustomContentDictionary.CustomCharacters.Count == 0) return; // no modded characters to load

        var container = __instance._characterStatContainer;
        if (container == null) return; // no container to add to

        ProgressState progress = SaveManager.Instance.Progress;

        // Ids already rendered: the five vanilla sections plus anything another patch appended.
        // Find them and save them so we can check against the list later.
        
        // FreeChildren (called at the top of LoadStats) uses QueueFreeSafely, which is deferred: on
        // every second open the previous open's sections are still children here, just queued for
        // deletion. Skipping them is essential, otherwise we drop the custom characters every other time
        // the tab is opened.
        var alreadyShown = new HashSet<ModelId>();
        foreach (var child in container.GetChildren())
        {
            if (child is NCharacterStats existing && !existing.IsQueuedForDeletion()
                && existing._characterStats?.Id is { } shownId && shownId != ModelId.none)
            {
                alreadyShown.Add(shownId);
            }
        }

        foreach (var character in CustomContentDictionary.CustomCharacters)
        {
            if (!character.IsPlayable) continue; //character is not marked as playable
            if (!alreadyShown.Add(character.Id)) continue; // already rendered by another patch

            // Real stats if the character has been played, otherwise a transient zeroed entry for
            // display only. We intentionally do not use GetOrCreateCharacterStats, which would
            // persist empty entries into the player's save just from opening the menu. So a fake is
			// used instead.
            CharacterStats stats = progress.GetStatsForCharacter(character.Id)
                                   ?? new CharacterStats { Id = character.Id };

            NCharacterStats section = NCharacterStats.Create(stats);
            container.AddChildSafely(section);
        }
    }
}
