using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;


internal class StateInfo //, MethodBase stateMethod, int indexLoadIndex)
{
    public int Index { get; }
    public List<CodeInstruction> Code { get; private set; }
    public MethodInfo? StateMethod { get; }
    
    public StateInfo(int index, List<CodeInstruction> code, FieldInfo stateField)
    {
        Index = index;
        Code = code;

        StateMethod = AnalyzeCode(stateField, Index, Code);
    }
    public StateInfo(int index, List<CodeInstruction> code, MethodInfo stateMethod)
    {
        Index = index;
        Code = code;

        StateMethod = stateMethod;
    }

    private static MethodInfo? AnalyzeCode(FieldInfo stateField, int stateIndex, List<CodeInstruction> code)
    {
        var storeStateMatcher = new InstructionMatcher().stfld(stateField);

        InstructionPatcher reader = new(code);
    
        bool[] matched = [true];
        reader.Match(_ => matched[0] = false, AsyncMethodCall.StateAwaitMatcher);
        if (!matched[0])
        {
            BaseLibMain.Logger.Info($"CODE:\n{code.Join(instruction => instruction.ToString(), "\n")}");
            throw new InvalidOperationException($"Failed to find state awaiter for state {stateIndex}");
        }

        reader
            .Step(-2).GetOperand(out var operand) //Get method called to get awaited task
            .Step(3)
            .Match(storeStateMatcher); //Move to next state store (occurs when awaited task does not end immediately)
        
        return operand as MethodInfo;
    }

    public void AddSaveState(FieldInfo stateField, int stringDictLocal)
    {
        Code = new InstructionPatcher(Code)
            .Match(AsyncMethodCall.StateAwaitMatcher)
            .Match(new InstructionMatcher()
                .dup()
                .stloc_0()
                .stfld(stateField))
            .Step(-3)
            .Insert([
                new CodeInstruction(OpCodes.Call, AsyncMethodCall.StoreStateInDictMethod), //Replace state with temp state key
                new CodeInstruction(OpCodes.Dup), //Extra copy of temp state key
                CodeInstruction.LoadLocal(stringDictLocal),
                new CodeInstruction(OpCodes.Call, AsyncMethodCall.StoreDictionaryForStateMethod)
            ]);
    }
}