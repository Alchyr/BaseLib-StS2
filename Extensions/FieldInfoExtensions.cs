using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BaseLib.Extensions;

public static class FieldInfoExtensions
{
    public static CodeInstruction Stfld(this FieldInfo fieldInfo)
    {
        return new CodeInstruction(OpCodes.Stfld, fieldInfo);
    }
    public static CodeInstruction Ldfld(this FieldInfo fieldInfo)
    {
        return new CodeInstruction(OpCodes.Ldfld, fieldInfo);
    }
    public static CodeInstruction Ldflda(this FieldInfo fieldInfo)
    {
        return new CodeInstruction(OpCodes.Ldflda, fieldInfo);
    }
}