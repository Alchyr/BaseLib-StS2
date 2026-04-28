using System.Reflection.Emit;
using HarmonyLib;

namespace BaseLib.Extensions;

public static class IntExtensions
{
    internal static CodeInstruction LoadConstant(this int i)
    {
        return i switch
        {
            -1 => new CodeInstruction(opcode: OpCodes.Ldc_I4_M1),
            0 => new CodeInstruction(opcode: OpCodes.Ldc_I4_0),
            1 => new CodeInstruction(opcode: OpCodes.Ldc_I4_1),
            2 => new CodeInstruction(opcode: OpCodes.Ldc_I4_2),
            3 => new CodeInstruction(opcode: OpCodes.Ldc_I4_3),
            4 => new CodeInstruction(opcode: OpCodes.Ldc_I4_4),
            5 => new CodeInstruction(opcode: OpCodes.Ldc_I4_5),
            6 => new CodeInstruction(opcode: OpCodes.Ldc_I4_6),
            7 => new CodeInstruction(opcode: OpCodes.Ldc_I4_7),
            8 => new CodeInstruction(opcode: OpCodes.Ldc_I4_8),
            _ => new CodeInstruction(opcode: OpCodes.Ldc_I4, i)
        };
    }
}