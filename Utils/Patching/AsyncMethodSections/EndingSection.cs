using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

internal class EndingSection : IAsyncMethodSection
{
    public static EndingSection Read(IAsyncStateSection mainSection, IEnumerator<CodeInstruction> codeEnumerator)
    {
        //Analyze ending section
        Type? returnType = null;
        CodeInstruction? retValInstruction = null;
        CodeInstruction? directRetInstruction = null;

        List<Label> leaveLabels = [..mainSection.LeaveLabels];
        
        List<CodeInstruction> endingSection = [];
        do
        {
            var instruction = codeEnumerator.Current;
            endingSection.Add(instruction);
            
            //When code after last async method call and no conditional early returns that will be in a state
            if (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S)
            {
                if (instruction.operand is Label label)
                {
                    leaveLabels.Add(label);
                }
            }
            
            foreach (var label in instruction.labels)
            {
                if (!leaveLabels.Remove(label)) continue;
                
                //This instruction is left to from a state.
                if (instruction.opcode == OpCodes.Ret)
                {
                    directRetInstruction ??= instruction;
                }
                else
                {
                    retValInstruction ??= instruction;
                }
            }
            
            if (returnType != null || instruction.opcode != OpCodes.Call
                                   || instruction.operand is not MethodInfo { Name: "SetResult" } info) continue;
            
            var declaringType = info.DeclaringType;
            if (declaringType == null) continue;

            if (declaringType == typeof(AsyncTaskMethodBuilder)
                || declaringType == typeof(AsyncValueTaskMethodBuilder))
            {
                continue;
            }
            if (declaringType.IsConstructedGenericType 
                && (declaringType.GetGenericTypeDefinition() == typeof(AsyncTaskMethodBuilder<>)
                    || declaringType.GetGenericTypeDefinition() == typeof(AsyncValueTaskMethodBuilder<>)))
            {
                returnType = declaringType.GenericTypeArguments[0];
            }
        } while (codeEnumerator.MoveNext());

        if (retValInstruction == null)
            throw new Exception($"Failed to find instruction to jump to when done with async method;\n" +
                                $"CODE:\n{endingSection.Join(instruction => instruction.ToString(),"\n")}");
        if (directRetInstruction == null)
            throw new Exception("Failed to find instruction to jump to when awaiting;\n" +
                                $"CODE:\n{endingSection.Join(instruction => instruction.ToString(),"\n")}");

        return new EndingSection
        {
            Code = endingSection,
            ReturnType = returnType,
            FinishAsyncInstruction = retValInstruction,
            AwaitAsyncInstruction = directRetInstruction
        };
    }

    
    public required List<CodeInstruction> Code { get; init; }
    public IEnumerable<StateInfo> AllStates => [];
    public required Type? ReturnType { get; init; }
    public required CodeInstruction AwaitAsyncInstruction { get; init; }
    public required CodeInstruction FinishAsyncInstruction { get; init; }
}