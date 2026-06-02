using System.Collections;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;

namespace BaseLib.Extensions;

public static class IEnumerableExtensions
{
    public static string AsReadable<T>(this IEnumerable<T> enumerable, string separator = ",")
    {
        return string.Join(separator, enumerable);
    }
    public static string AsReadable(this IEnumerable enumerable, string separator = ",")
    {
        return string.Join(separator, enumerable);
    }
    public static string NumberedLines<T>(this IEnumerable<T> enumerable)
    {
        StringBuilder sb = new();
        int line = 0;
        foreach (var item in enumerable)
        {
            sb.Append(line).Append(": ").Append(item).AppendLine();
            ++line;
        }
        return sb.ToString();
    }

    public static void CheckCode(this IEnumerable<CodeInstruction> code)
    {
        //Check for duplicate or missing labels
        var codeList = code.ToList();
        HashSet<Label> allLabels = [];
        HashSet<Label> jumpDestinations = [];
        for (var index = 0; index < codeList.Count; index++)
        {
            var ci = codeList[index];
            
            foreach (var label in ci.labels)
            {
                if (!allLabels.Add(label))
                {
                    BaseLibMain.Logger.Warn($"DUPLICATE LABEL: {label.Id}");
                }
            }

            if (ci.Branches(out var dest))
            {
                if (dest == null)
                {
                    BaseLibMain.Logger.Warn($"Branch operation missing operand at index {index}");
                }
                else
                {
                    if (!jumpDestinations.Add(dest.Value))
                    {
                        BaseLibMain.Logger.Warn($"Minor: Label {dest.Value.Id} is reused");
                    }
                }
            }
            else if (ci.opcode == OpCodes.Switch)
            {
                if (ci.operand is Label[] labels)
                {
                    foreach (var label in labels)
                    {
                        if (!jumpDestinations.Add(label))
                        {
                            BaseLibMain.Logger.Warn($"Minor: Label {label.Id} is reused");
                        }
                    }
                }
            }
            else if (ci.opcode == OpCodes.Leave || ci.opcode == OpCodes.Leave_S)
            {
                if (ci.operand is Label label)
                {
                    if (!jumpDestinations.Add(label))
                    {
                        BaseLibMain.Logger.Warn($"Minor: Label {label.Id} is reused");
                    }
                }
                else
                {
                    BaseLibMain.Logger.Warn($"Leave operation missing label operand at index {index}");
                }
            }
        }

        if (jumpDestinations.Count > allLabels.Count)
        {
            jumpDestinations.RemoveWhere(allLabels.Contains);
            BaseLibMain.Logger.Warn($"Jump destinations not found: {jumpDestinations.Join(label => label.Id.ToString())}");
        }
        else if (allLabels.Count > jumpDestinations.Count)
        {
            allLabels.RemoveWhere(jumpDestinations.Contains);
            BaseLibMain.Logger.Warn($"Unused labels: {allLabels.Join(label => label.Id.ToString())}");
        }
    }
}
