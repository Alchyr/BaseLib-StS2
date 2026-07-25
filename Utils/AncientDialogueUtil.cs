using System.Text;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Localization;

namespace BaseLib.Utils;

public static class AncientDialogueUtil
{
    private const string ArchitectKey = "THE_ARCHITECT";
    private const string AttackKey = "-attack";
    private const string StartAttackKey = "-startattack";
    private const string EndAttackKey = "-endattack";
    private const string VisitIndexKey = "-visit";
    
    public static string SfxPath(string dialogueLoc) =>
        LocString.GetIfExists("ancients", dialogueLoc + ".sfx")?.GetRawText() ?? "";
    
    public static string BaseLocKey(string ancientId, string charId) => $"{ancientId}.talk.{charId}.";
    
    /// <summary>
    /// Generates a list of AncientDialogue (conversations) by checking for existing localization with a specified file and key.
    /// </summary>
    /// <param name="locTable">name of table to check for localization</param>
    /// <param name="baseKey"></param>
    /// <param name="log"></param>
    /// <returns></returns>
    public static List<AncientDialogue> GetDialoguesForKey(string locTable, string baseKey, StringBuilder? log = null)
    {
        log?.AppendLine($"Looking for dialogues for '{baseKey}' in {locTable}.json");
        List<AncientDialogue> dialogues = [];
        var isArchitect = baseKey.StartsWith(ArchitectKey);
        
        int index = 0, visitIndex = 0;
        while (DialogueExists(locTable, baseKey, index))
        {
            log?.Append($"Found dialogue '{index}'");

            if (isArchitect)
            {
                visitIndex = index;
            }
            else
            {
                visitIndex = index switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 4,
                    _ => visitIndex + 3
                };
            }
            var indexLoc = LocString.GetIfExists(locTable, $"{baseKey}{index}{VisitIndexKey}");
            //Directly use Parse; if it exists it must be defined correctly.
            if (indexLoc != null) visitIndex = int.Parse(indexLoc.GetRawText());
            
            List<string> sfxPaths = [];

            var line = ExistingLine(locTable, baseKey, index, sfxPaths.Count);

            while (line != null)
            {
                sfxPaths.Add(SfxPath(line));
                line = ExistingLine(locTable, baseKey, index, sfxPaths.Count);
            }

            log?.AppendLine($" with {sfxPaths.Count} lines");

            var startAttackers = ArchitectAttackers.None;
            var endAttackers = ArchitectAttackers.Architect;
            
            var attackString = LocString.GetIfExists(locTable, $"{baseKey}{index}{AttackKey}");
            if (Enum.TryParse(attackString?.GetRawText(), true, out ArchitectAttackers result)) endAttackers = result;
            attackString = LocString.GetIfExists(locTable, $"{baseKey}{index}{StartAttackKey}");
            if (Enum.TryParse(attackString?.GetRawText(), true, out result)) startAttackers = result;
            attackString = LocString.GetIfExists(locTable, $"{baseKey}{index}{EndAttackKey}");
            if (Enum.TryParse(attackString?.GetRawText(), true, out result)) endAttackers = result;
            
            dialogues.Add(new AncientDialogue(sfxPaths.ToArray())
            {
                VisitIndex = visitIndex,
                StartAttackers = startAttackers,
                EndAttackers = endAttackers
            });
            ++index;
        }

        return dialogues;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="locTable"></param>
    /// <param name="baseKey">first section of key ending with a '.'</param>
    /// <param name="index">index of conversation</param>
    /// <returns></returns>
    private static bool DialogueExists(string locTable, string baseKey, int index)
    {
        return LocString.Exists(locTable, $"{baseKey}{index}-0.ancient") ||
               LocString.Exists(locTable, $"{baseKey}{index}-0r.ancient") ||
               LocString.Exists(locTable, $"{baseKey}{index}-0.char") ||
               LocString.Exists(locTable, $"{baseKey}{index}-0r.char");
    }

    private static string? ExistingLine(string locTable, string baseKey, int dialogueIndex, int lineIndex)
    {
        var locEntry = $"{baseKey}{dialogueIndex}-{lineIndex}r.ancient";
        if (LocString.Exists(locTable, locEntry)) return locEntry;
        
        locEntry = $"{baseKey}{dialogueIndex}-{lineIndex}r.char";
        if (LocString.Exists(locTable, locEntry)) return locEntry;
        
        locEntry = $"{baseKey}{dialogueIndex}-{lineIndex}.ancient";
        if (LocString.Exists(locTable, locEntry)) return locEntry;
        
        locEntry = $"{baseKey}{dialogueIndex}-{lineIndex}.char";
        if (LocString.Exists(locTable, locEntry)) return locEntry;

        return null;
    }
}