using HarmonyLib;

namespace BaseLib.Utils.Patching;

public interface IMatcher
{
    public bool Match(List<string> log, List<CodeInstruction> code, int startIndex, out int matchStart, out int matchEnd);
}
