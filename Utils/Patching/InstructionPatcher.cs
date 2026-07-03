using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;

namespace BaseLib.Utils.Patching;

public class InstructionPatcher(IEnumerable<CodeInstruction> instructions)
{
    //Placeholder-ish, other existing tools may be easier to use. Will need time to see.
    private readonly List<CodeInstruction> _code = [.. instructions];
    private int _index = -1, _lastMatchStart = -1;

    public readonly List<string> Log = [];
    public int Index => _index;

    public static implicit operator List<CodeInstruction>(InstructionPatcher locator) => locator._code;

    public InstructionPatcher ResetPosition()
    {
        _index = -1;
        _lastMatchStart = -1;
        return this;
    }

    /// <summary>
    /// Iterates over given matchers and attempts to match each in order.
    /// After matching is complete, position is on the code instruction following the last match.
    /// If a match is not found, an exception will be thrown.
    /// </summary>
    public InstructionPatcher Match(params IMatcher[] matchers)
    {
        return Match(DefaultMatchFailure, matchers);
    }
    
    /// <summary>
    /// Iterates over given matchers and attempts to match each in order.
    /// After matching is complete, position is on the code instruction following the last match.
    /// If a match is not found, onFailMatch is called. By default, this will throw an exception.
    /// </summary>
    public InstructionPatcher Match(Action<IMatcher[]> onFailMatch, params IMatcher[] matchers)
    {
        if (_index < 0) _index = 0;
        foreach (IMatcher matcher in matchers)
        {
            if (!matcher.Match(Log, _code, _index, out _lastMatchStart, out _index))
            {
                onFailMatch(matchers);
                return this;
            }
        }

        Log.Add("Found end of match at " + _index + "; last match starts at " + _lastMatchStart);

        return this;
    }

    /// <summary>
    /// Iterates over given matchers and attempts to match each in order.
    /// After matching is complete, position is on the code instruction following the last match.
    /// If a match is not found, null will be returned, skipping operations chained with the ?. operator.
    /// </summary>
    public InstructionPatcher? TryMatch(params IMatcher[] matchers)
    {
        if (_index < 0) _index = 0;
        foreach (IMatcher matcher in matchers)
        {
            if (!matcher.Match(Log, _code, _index, out _lastMatchStart, out _index))
            {
                Log.Add("TryMatch failed");
                return null;
            }
        }

        Log.Add("Found end of match at " + _index + "; last match starts at " + _lastMatchStart);
        return this;
    }
    /// <summary>
    /// Iterates over given matchers and attempts to match each in order, starting from the end of the method.
    /// After matching is complete, position is on the code instruction following the last match.
    /// If a match is not found, an exception will be thrown.
    /// </summary>
    public InstructionPatcher MatchFromEnd(params IMatcher[] matchers)
    {
        return MatchFromEnd(DefaultMatchFailure, matchers);
    }
    
    /// <summary>
    /// Iterates over given matchers and attempts to match each in order, starting from the end of the method.
    /// After matching is complete, position is on the code instruction following the last match.
    /// If a match is not found, onFailMatch is called. By default, this will throw an exception.
    /// </summary>
    public InstructionPatcher MatchFromEnd(Action<IMatcher[]> onFailMatch, params IMatcher[] matchers)
    {
        int searchIndex = _code.Count;
        while (searchIndex > 0)
        {
            _index = --searchIndex;
            var matched = true;
            
            foreach (IMatcher matcher in matchers)
            {
                if (!matcher.Match(Log, _code, _index, out _lastMatchStart, out _index))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                break;
            }
        }

        if (searchIndex == 0)
        {
            onFailMatch(matchers);
            return this;
        }

        Log.Add("Found end of match at " + _index + "; last match starts at " + _lastMatchStart);

        return this;
    }

    public InstructionPatcher MatchStart()
    {
        _index = 0;
        _lastMatchStart = 0;
        return this;
    }

    /// <summary>
    /// Matches the last instruction of the method, with the start of the match being the first instruction of the method.
    /// </summary>
    public InstructionPatcher MatchEnd()
    {
        _index = _code.Count;
        _lastMatchStart = 0;
        return this;
    }

    /// <summary>
    /// Adjust current position in code instructions.
    /// Should only be called after <seealso cref="Match(Action{IMatcher[]}, IMatcher[])"/> is called at least once.
    /// Avoid doing large steps into unmatched code, as this may result in issues if the code you are patching has already been modified.
    /// </summary>
    /// <param name="amt"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public InstructionPatcher Step(int amt = 1)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Step without any match found");

        _index += amt;

        Log.Add("Stepped to " + _index);

        return this;
    }

    /// <summary>
    /// Adds a label to the current instruction.
    /// </summary>
    public InstructionPatcher AddLabel(ILGenerator generator, out Label label)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to AddLabel without any match found");

        label = generator.DefineLabel();
        _code[_index].WithLabels(label);
        return this;
    }

    /// <summary>
    /// Gets all labels attached to the current instruction.
    /// </summary>
    public InstructionPatcher GetLabels(out List<Label> labels)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetLabels without any match found");

        labels = _code[_index].labels;

        if (labels.Count == 0)
        {
            if (_code[_index].operand is Label)
            {
                BaseLibMain.Logger.Info($"Code instruction {_code[_index]} has no labels. Did you mean to use GetOperandLabel instead?");
            }
            else
            {
                BaseLibMain.Logger.Info($"Code instruction {_code[_index]} has no labels");
            }
        }

        return this;
    }
    
    /// <summary>
    /// Gets and removes all labels attached to the current instruction.
    /// </summary>
    public InstructionPatcher TakeLabels(out List<Label> labels)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetLabels without any match found");

        labels = _code[_index].ExtractLabels();

        if (labels.Count == 0)
        {
            if (_code[_index].operand is Label)
            {
                BaseLibMain.Logger.Info($"Code instruction {_code[_index]} has no labels. Did you mean to use GetOperandLabel instead?");
            }
            else
            {
                BaseLibMain.Logger.Info($"Code instruction {_code[_index]} has no labels");
            }
        }

        return this;
    }

    /// <summary>
    /// Gets a label used as the operand of the current instruction.
    /// </summary>
    public InstructionPatcher GetOperandLabel(out Label label)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetOperandLabel without any match found");
        if (_code[_index].operand is Label result)
        {
            label = result;
            return this;
        }
        throw new InvalidOperationException($"Code instruction {_code[_index].ToString()} does not have a Label parameter");
    }

    public InstructionPatcher GetInstruction(out CodeInstruction instruction)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetInstruction without any match found");
        instruction = _code[_index];
        Log.Add($"Got instruction [{instruction}]");
        return this;
    }

    public InstructionPatcher GetOperand(out object operand)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetOperand without any match found");
        operand = _code[_index].operand;
        Log.Add($"Got operand [{operand?.GetType().FullName}]{operand}");
        return this;
    }
    
    public InstructionPatcher GetIndexOperand(out int operand)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to GetOperand without any match found");
        CodeInstruction instruction = _code[_index];
        switch ((int) instruction.opcode.Value)
        {
            case (int)OpCodeValues.Ldarg_0:
            case (int)OpCodeValues.Ldloc_0:
            case (int)OpCodeValues.Stloc_0:
                operand = 0;
                break;
            case (int)OpCodeValues.Ldarg_1:
            case (int)OpCodeValues.Ldloc_1:
            case (int)OpCodeValues.Stloc_1:
                operand = 1;
                break;
            case (int)OpCodeValues.Ldarg_2:
            case (int)OpCodeValues.Ldloc_2:
            case (int)OpCodeValues.Stloc_2:
                operand = 2;
                break;
            case (int)OpCodeValues.Ldarg_3:
            case (int)OpCodeValues.Ldloc_3:
            case (int)OpCodeValues.Stloc_3:
                operand = 3;
                break;
            case (int)OpCodeValues.Ldarg_S:
            case (int)OpCodeValues.Ldloc_S:
            case (int)OpCodeValues.Stloc_S:
            case (int)OpCodeValues.Ldarg:
            case (int)OpCodeValues.Ldloc:
            case (int)OpCodeValues.Stloc:
                if (instruction.operand is LocalBuilder localBuilder)
                {
                    operand = localBuilder.LocalIndex;
                }
                else
                {
                    operand = Convert.ToInt32(instruction.operand);
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported opcode for GetIndexOperand: {instruction.opcode}");
        }
        return this;
    }

    public InstructionPatcher TryGetIntValue(out int? val)
    {
        val = null;
        if (_index < 0) throw new InvalidOperationException("Attempted to TryGetIntValue without any match found");
        
        if (_code[_index].TryGetIntValue(out var result))
        {
            val = result;
        }
        
        return this;
    }

    /// <summary>
    /// Replaces a match of CodeInstructions. Note that if this removes a labeled instruction this can cause issues.
    /// Preserving labels must be done manually.
    /// </summary>
    public InstructionPatcher ReplaceLastMatch(IEnumerable<CodeInstruction> replacement)
    {
        if (_lastMatchStart < 0) throw new InvalidOperationException("Attempted to ReplaceLastMatch without any match found");

        int i = 0;
        foreach (CodeInstruction instruction in replacement)
        {
            int replaceIndex = _lastMatchStart + i;
            if (replaceIndex > _index)
            {
                _index = replaceIndex;
                _code.Insert(_index, instruction);
            }
            else
            {
                _code[_lastMatchStart + i] = instruction;
            }
            ++i;
        }

        if (_lastMatchStart + i < _index)
        {
            _code.RemoveRange(_lastMatchStart + i, _index - (_lastMatchStart + i));
            _index = _lastMatchStart + i;
        }
        else
        {
            ++_index;
        }

        return this;
    }

    /// <summary>
    /// Replaces the current instruction with another.
    /// </summary>
    public InstructionPatcher Replace(CodeInstruction replacement, bool keepLabels = true)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Replace without any match found");

        if (keepLabels)
        {
            replacement.MoveLabelsFrom(_code[_index]);
        }

        Log.Add($"{_code[_index]} => {replacement}");
        _code[_index] = replacement;
        return this;
    }

    public InstructionPatcher IncrementIntPush()
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Replace without any match found");

        switch (_code[_index].opcode.Value)
        {
            case 0x15: //m1, -1
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_0));
            case 0x16: //0
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_1));
            case 0x17: //1
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_2));
            case 0x18: //2
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_3));
            case 0x19: //3
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_4));
            case 0x1a: //4
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_5));
            case 0x1b: //5
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_6));
            case 0x1c: //6
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_7));
            case 0x1d: //7
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_8));
            case 0x1e: //8
                return Replace(new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)9));
            case 0x1f: //I4_S
                if (_code[_index].TryGetIntValue(out var byteResult))
                {
                    byteResult += 1;
                    if (byteResult > sbyte.MaxValue)
                    {
                        return Replace(new CodeInstruction(OpCodes.Ldc_I4, byteResult));
                    }
                    else
                    {
                        return Replace(new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)byteResult));
                    }
                }
                throw new InvalidOperationException("Failed to determine integer value of " + _code[_index] + " to incremented");
            case 0x20: //I4
                if (_code[_index].TryGetIntValue(out var intResult))
                {
                    return Replace(new CodeInstruction(OpCodes.Ldc_I4, intResult + 1));
                }
                throw new InvalidOperationException("Failed to determine integer value of " + _code[_index] + " to incremented");
            default:
                throw new InvalidOperationException("Instruction " + _code[_index] + " is not an int push instruction that can be incremented");
        }
    }
    public InstructionPatcher IncrementIntPush(out CodeInstruction replacedPush)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Replace without any match found");

        replacedPush = _code[_index];
        return IncrementIntPush();
    }

    /// <summary>
    /// Inserts a single CodeInstruction before the current instruction.
    /// </summary>
    /// <param name="instruction"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public InstructionPatcher Insert(CodeInstruction instruction)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Insert without any match found");

        _code.Insert(_index, instruction);
        ++_index;
        
        Log.Add($"Inserted 1 instruction, new index {_index}");

        return this;
    }

    /// <summary>
    /// Inserts a sequence of CodeInstructions before the current instruction (after the last match).
    /// </summary>
    /// <param name="insert"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public InstructionPatcher Insert(IEnumerable<CodeInstruction> insert)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to Insert without any match found");

        var codeInstructions = insert as CodeInstruction[] ?? insert.ToArray();
        _code.InsertRange(_index, codeInstructions);
        _index += codeInstructions.Length;
        
        Log.Add($"Inserted {codeInstructions.Length} instructions, new index {_index}");

        return this;
    }

    /// <summary>
    /// Inserts a sequence of CodeInstructions before the current match.
    /// </summary>
    /// <param name="insert"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public InstructionPatcher InsertBeforeMatch(IEnumerable<CodeInstruction> insert)
    {
        if (_index < 0 || _lastMatchStart < 0) throw new InvalidOperationException("Attempted to Insert without any match found");

        _index = _lastMatchStart;
        _lastMatchStart = -1;

        var codeInstructions = insert as CodeInstruction[] ?? insert.ToArray();
        _code.InsertRange(_index, codeInstructions);
        _index += codeInstructions.Length;
        
        Log.Add($"Inserted {codeInstructions.Length} instructions, new index {_index}");

        return this;
    }

    public InstructionPatcher CopyMatch(out List<CodeInstruction> match)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to CopyMatch without any match found");

        match = _code.GetRange(_lastMatchStart, _index - _lastMatchStart).Select(instruction => instruction.Clone()).ToList();
        
        Log.Add($"Copied {match.Count} instructions:\n");
        foreach (var instruction in match)
        {
            Log.Add($" - {instruction}");
        }
        return this;
    }

    /// <summary>
    /// Inserts a copy of existing CodeInstructions determined by offset.
    /// Labels and blocks are not maintained, only opcodes and operands.
    /// </summary>
    /// <param name="startOffset"></param>
    /// <param name="copyLength"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public InstructionPatcher InsertCopy(int startOffset, int copyLength)
    {
        if (_index < 0) throw new InvalidOperationException("Attempted to InsertCopy without any match found");

        int startIndex = _index + startOffset;
        if (startIndex < 0) throw new InvalidOperationException($"startIndex of InsertCopy less than 0 ({startIndex})");

        List<CodeInstruction> copy = [];
        for (int i = 0; i < copyLength; ++i)
        {
            Log.Add("Inserting Copy: " + _code[startIndex + i]);
            copy.Add(_code[startIndex + i].Clone());
        }

        return Insert(copy);
    }

    public InstructionPatcher PrintLog(Logger logger)
    {
        logger.Info(Log.AsReadable("\n"));
        return this;
    }
    public InstructionPatcher PrintResult(Logger logger)
    {
        logger.Info("----- RESULT -----\n" + ((List<CodeInstruction>)this).NumberedLines());
        return this;
    }

    private void DefaultMatchFailure(IMatcher[] matchers)
    {
        throw new Exception("Failed to find match:\n" + matchers.AsReadable("\n---------\n") + "\nLOG:\n" + Log.AsReadable("\n"));
    }
}

/// <summary>
/// Copied from System.Reflection.Emit.OpCodes
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnusedMember.Local")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal enum OpCodeValues
{
    Nop = 0x00,
    Break = 0x01,
    Ldarg_0 = 0x02,
    Ldarg_1 = 0x03,
    Ldarg_2 = 0x04,
    Ldarg_3 = 0x05,
    Ldloc_0 = 0x06,
    Ldloc_1 = 0x07,
    Ldloc_2 = 0x08,
    Ldloc_3 = 0x09,
    Stloc_0 = 0x0a,
    Stloc_1 = 0x0b,
    Stloc_2 = 0x0c,
    Stloc_3 = 0x0d,
    Ldarg_S = 0x0e,
    Ldarga_S = 0x0f,
    Starg_S = 0x10,
    Ldloc_S = 0x11,
    Ldloca_S = 0x12,
    Stloc_S = 0x13,
    Ldnull = 0x14,
    Ldc_I4_M1 = 0x15,
    Ldc_I4_0 = 0x16,
    Ldc_I4_1 = 0x17,
    Ldc_I4_2 = 0x18,
    Ldc_I4_3 = 0x19,
    Ldc_I4_4 = 0x1a,
    Ldc_I4_5 = 0x1b,
    Ldc_I4_6 = 0x1c,
    Ldc_I4_7 = 0x1d,
    Ldc_I4_8 = 0x1e,
    Ldc_I4_S = 0x1f,
    Ldc_I4 = 0x20,
    Ldc_I8 = 0x21,
    Ldc_R4 = 0x22,
    Ldc_R8 = 0x23,
    Dup = 0x25,
    Pop = 0x26,
    Jmp = 0x27,
    Call = 0x28,
    Calli = 0x29,
    Ret = 0x2a,
    Br_S = 0x2b,
    Brfalse_S = 0x2c,
    Brtrue_S = 0x2d,
    Beq_S = 0x2e,
    Bge_S = 0x2f,
    Bgt_S = 0x30,
    Ble_S = 0x31,
    Blt_S = 0x32,
    Bne_Un_S = 0x33,
    Bge_Un_S = 0x34,
    Bgt_Un_S = 0x35,
    Ble_Un_S = 0x36,
    Blt_Un_S = 0x37,
    Br = 0x38,
    Brfalse = 0x39,
    Brtrue = 0x3a,
    Beq = 0x3b,
    Bge = 0x3c,
    Bgt = 0x3d,
    Ble = 0x3e,
    Blt = 0x3f,
    Bne_Un = 0x40,
    Bge_Un = 0x41,
    Bgt_Un = 0x42,
    Ble_Un = 0x43,
    Blt_Un = 0x44,
    Switch = 0x45,
    Ldind_I1 = 0x46,
    Ldind_U1 = 0x47,
    Ldind_I2 = 0x48,
    Ldind_U2 = 0x49,
    Ldind_I4 = 0x4a,
    Ldind_U4 = 0x4b,
    Ldind_I8 = 0x4c,
    Ldind_I = 0x4d,
    Ldind_R4 = 0x4e,
    Ldind_R8 = 0x4f,
    Ldind_Ref = 0x50,
    Stind_Ref = 0x51,
    Stind_I1 = 0x52,
    Stind_I2 = 0x53,
    Stind_I4 = 0x54,
    Stind_I8 = 0x55,
    Stind_R4 = 0x56,
    Stind_R8 = 0x57,
    Add = 0x58,
    Sub = 0x59,
    Mul = 0x5a,
    Div = 0x5b,
    Div_Un = 0x5c,
    Rem = 0x5d,
    Rem_Un = 0x5e,
    And = 0x5f,
    Or = 0x60,
    Xor = 0x61,
    Shl = 0x62,
    Shr = 0x63,
    Shr_Un = 0x64,
    Neg = 0x65,
    Not = 0x66,
    Conv_I1 = 0x67,
    Conv_I2 = 0x68,
    Conv_I4 = 0x69,
    Conv_I8 = 0x6a,
    Conv_R4 = 0x6b,
    Conv_R8 = 0x6c,
    Conv_U4 = 0x6d,
    Conv_U8 = 0x6e,
    Callvirt = 0x6f,
    Cpobj = 0x70,
    Ldobj = 0x71,
    Ldstr = 0x72,
    Newobj = 0x73,
    Castclass = 0x74,
    Isinst = 0x75,
    Conv_R_Un = 0x76,
    Unbox = 0x79,
    Throw = 0x7a,
    Ldfld = 0x7b,
    Ldflda = 0x7c,
    Stfld = 0x7d,
    Ldsfld = 0x7e,
    Ldsflda = 0x7f,
    Stsfld = 0x80,
    Stobj = 0x81,
    Conv_Ovf_I1_Un = 0x82,
    Conv_Ovf_I2_Un = 0x83,
    Conv_Ovf_I4_Un = 0x84,
    Conv_Ovf_I8_Un = 0x85,
    Conv_Ovf_U1_Un = 0x86,
    Conv_Ovf_U2_Un = 0x87,
    Conv_Ovf_U4_Un = 0x88,
    Conv_Ovf_U8_Un = 0x89,
    Conv_Ovf_I_Un = 0x8a,
    Conv_Ovf_U_Un = 0x8b,
    Box = 0x8c,
    Newarr = 0x8d,
    Ldlen = 0x8e,
    Ldelema = 0x8f,
    Ldelem_I1 = 0x90,
    Ldelem_U1 = 0x91,
    Ldelem_I2 = 0x92,
    Ldelem_U2 = 0x93,
    Ldelem_I4 = 0x94,
    Ldelem_U4 = 0x95,
    Ldelem_I8 = 0x96,
    Ldelem_I = 0x97,
    Ldelem_R4 = 0x98,
    Ldelem_R8 = 0x99,
    Ldelem_Ref = 0x9a,
    Stelem_I = 0x9b,
    Stelem_I1 = 0x9c,
    Stelem_I2 = 0x9d,
    Stelem_I4 = 0x9e,
    Stelem_I8 = 0x9f,
    Stelem_R4 = 0xa0,
    Stelem_R8 = 0xa1,
    Stelem_Ref = 0xa2,
    Ldelem = 0xa3,
    Stelem = 0xa4,
    Unbox_Any = 0xa5,
    Conv_Ovf_I1 = 0xb3,
    Conv_Ovf_U1 = 0xb4,
    Conv_Ovf_I2 = 0xb5,
    Conv_Ovf_U2 = 0xb6,
    Conv_Ovf_I4 = 0xb7,
    Conv_Ovf_U4 = 0xb8,
    Conv_Ovf_I8 = 0xb9,
    Conv_Ovf_U8 = 0xba,
    Refanyval = 0xc2,
    Ckfinite = 0xc3,
    Mkrefany = 0xc6,
    Ldtoken = 0xd0,
    Conv_U2 = 0xd1,
    Conv_U1 = 0xd2,
    Conv_I = 0xd3,
    Conv_Ovf_I = 0xd4,
    Conv_Ovf_U = 0xd5,
    Add_Ovf = 0xd6,
    Add_Ovf_Un = 0xd7,
    Mul_Ovf = 0xd8,
    Mul_Ovf_Un = 0xd9,
    Sub_Ovf = 0xda,
    Sub_Ovf_Un = 0xdb,
    Endfinally = 0xdc,
    Leave = 0xdd,
    Leave_S = 0xde,
    Stind_I = 0xdf,
    Conv_U = 0xe0,
    Prefix7 = 0xf8,
    Prefix6 = 0xf9,
    Prefix5 = 0xfa,
    Prefix4 = 0xfb,
    Prefix3 = 0xfc,
    Prefix2 = 0xfd,
    Prefix1 = 0xfe,
    Prefixref = 0xff,
    Arglist = 0xfe00,
    Ceq = 0xfe01,
    Cgt = 0xfe02,
    Cgt_Un = 0xfe03,
    Clt = 0xfe04,
    Clt_Un = 0xfe05,
    Ldftn = 0xfe06,
    Ldvirtftn = 0xfe07,
    Ldarg = 0xfe09,
    Ldarga = 0xfe0a,
    Starg = 0xfe0b,
    Ldloc = 0xfe0c,
    Ldloca = 0xfe0d,
    Stloc = 0xfe0e,
    Localloc = 0xfe0f,
    Endfilter = 0xfe11,
    Unaligned_ = 0xfe12,
    Volatile_ = 0xfe13,
    Tail_ = 0xfe14,
    Initobj = 0xfe15,
    Constrained_ = 0xfe16,
    Cpblk = 0xfe17,
    Initblk = 0xfe18,
    Rethrow = 0xfe1a,
    Sizeof = 0xfe1c,
    Refanytype = 0xfe1d,
    Readonly_ = 0xfe1e,
}