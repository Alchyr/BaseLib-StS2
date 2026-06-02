using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

//TODO - Handle more unusual cases with branching (see CombatManager.ExecuteEnemyTurn)
internal class BranchSection : IAsyncStateSection
{
    public static IAsyncStateSection Read(AsyncMethodContext context, LoadStateSection loadStateSection, BranchingStateSection branchSource, IEnumerator<CodeInstruction> codeEnumerator)
    {
        //If this is another branching section, initial parts are prep rather than truly part of a state.
        List<CodeInstruction> prepSection = [];
        List<int> stateIndices = [];
        CodeInstruction? jumpDest = null;
        
        do
        {
            var instruction = codeEnumerator.Current;
            
            foreach (var label in instruction.labels)
            {
                if (branchSource.LabelStates.TryGetValue(label, out var state))
                {
                    BaseLibMain.Logger.Debug($"Found state resume point label {label.Id} for state {state}");
                    stateIndices.Add(state);
                    jumpDest = instruction;
                }
            }
            
            if (jumpDest != null)
            {
                BaseLibMain.Logger.VeryDebug("End of state prep");
                break;
            }
                
            prepSection.Add(instruction);

            /*if (instruction.opcode == OpCodes.Br || instruction.opcode == OpCodes.Br_S)
            {
                //Unconditional branch
                BaseLibMain.Logger.Debug("Found unconditional branch in state, likely contains looping.");
            }*/
        } while(codeEnumerator.MoveNext());

        if (stateIndices.Count == 0 || jumpDest == null)
        {
            throw new Exception("Failed to find state branches.");
        }

        if (stateIndices.Count > 1)
        {
            BaseLibMain.Logger.Debug("Section is destination of multiple states; should be additional branching section.");
            var result = BranchingStateSection.Read(context, loadStateSection, codeEnumerator);
            result.Prep = prepSection;
            return result;
        }
        
        //Standard single state
        var stateIndex = stateIndices[0];
            
        List<CodeInstruction> stateSection = [..prepSection];
        HashSet<Label> leaveLabels = [];
        bool endingState = false;

        do
        {
            var instruction = codeEnumerator.Current;
            
            if (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S)
            {
                if (instruction.operand is Label label)
                {
                    leaveLabels.Add(label);
                }
            }
            
            //Check for ending of state
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo { Name: "GetResult" } methodInfo 
                                                   && (methodInfo.DeclaringType?.Name.StartsWith("TaskAwaiter") ?? false))
            {
                endingState = true;
                stateSection.Add(instruction);
            }
            else if (endingState) //Last instruction of state
            {
                if (instruction.IsStloc() || instruction.opcode == OpCodes.Nop || instruction.opcode == OpCodes.Pop)
                {
                    stateSection.Add(instruction);
                    codeEnumerator.MoveNext();
                }
                
                var state = new StateInfo(stateIndex, stateSection, context.StateField);
                BaseLibMain.Logger.Debug($"Generated StateInfo for state {stateIndex}");
                if (state.Index >= context.NextStateIndex)
                    context.NextStateIndex = state.Index + 1;

                var result = new BranchSection
                {
                    State = state,
                    ResumeInstruction = jumpDest,
                    LeaveLabels = leaveLabels
                };
                
                if (loadStateSection.AddStateLoading)
                {
                    result.State.AddSaveState(context.StateField, loadStateSection.StringDictLocal);
                }
                
                return result;
            }
            else
            {
                stateSection.Add(instruction);
            }
        } while(codeEnumerator.MoveNext());

        throw new Exception($"Failed to find end of state {stateIndex}");
    }

    public static BranchSection Create(AsyncMethodContext context, LoadStateSection loadSection, EndingSection endingSection,
        MethodInfo callMethod, IEnumerable<StateParamInfo> loadFields, Label resumeLabel, 
        AsyncMethodCall.ResultType resultType, string? resultName)
    {
        var generator = context.Generator;
        
        var awaiterType = typeof(TaskAwaiter);
        var taskMethodBuilderType = context.BuilderField.FieldType;
        var valueTypeMachine = context.StateMachineType.IsValueType;

        var returnType = typeof(void);
        if (callMethod.ReturnType.IsGenericType)
        {
            returnType = callMethod.ReturnType.GetGenericArguments()[0];
            BaseLibMain.Logger.Debug($"Method to call has return type; making generic awaiter type [{returnType}]");
            awaiterType = typeof(TaskAwaiter<>).MakeGenericType(returnType);
        }
        
        var taskGetAwaiter = callMethod.ReturnType.GetMethod("GetAwaiter");
        if (taskGetAwaiter == null)
            throw new Exception($"Failed to get GetAwaiter for type {callMethod.ReturnType}");

        var awaitUnsafe = taskMethodBuilderType.GetMethod("AwaitUnsafeOnCompleted");
        if (awaitUnsafe == null)
            throw new Exception($"Failed to get AwaitUnsafeOnCompleted for type {taskMethodBuilderType}");
        if (awaitUnsafe.IsGenericMethodDefinition)
        {
            awaitUnsafe = awaitUnsafe.MakeGenericMethod(awaiterType, context.StateMachineType);
        }
        
        var taskAwaiter = generator.DeclareLocal(awaiterType);
        var isCompleted = awaiterType.PropertyGetter("IsCompleted");
        
        var endingSectionLabel = generator.DefineLabel();
        
        //Prep initial method call
        List<Label> leaveLabels = [];
        List<CodeInstruction> stateInstructions = [];
        StateParamInfo? resultParam = null;

        foreach (var loadField in loadFields) //Load fields as parameters for method
        {
            if (resultType == AsyncMethodCall.ResultType.Named && loadField.Parameter.Name == resultName)
            {
                resultParam = loadField;
            }
            loadField.AddLoadInstructions(stateInstructions);
        }

        if (resultParam != null)
        {
            //Validate result param type
            if (!returnType.IsAssignableTo(resultParam.Parameter.ParameterType))
            {
                throw new ArgumentException(
                    $"Cannot store method result of type {returnType} to parameter {resultParam.Parameter.Name} of type {resultParam.Parameter.ParameterType}");
            }
        }
        
        //Loaded parameters, now call async method and store awaiter in local
        stateInstructions.AddRange([
            new CodeInstruction(OpCodes.Call, callMethod),
            taskGetAwaiter.CallVirt(),
            CodeInstruction.StoreLocal(taskAwaiter.LocalIndex),
            CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true),
            new CodeInstruction(OpCodes.Call, isCompleted),
            new CodeInstruction(OpCodes.Brtrue, endingSectionLabel) //If already complete, skip to end
        ]);
        
        //await block; prep for awaiting
        stateInstructions.AddRange([
            CodeInstruction.LoadArgument(0),  //load "this" for stfld
            context.NextStateIndex.LoadConstant(),
            new CodeInstruction(OpCodes.Call, AsyncMethodCall.StoreStateInDictMethod), //Use external dict for real state
            new CodeInstruction(OpCodes.Dup),
            CodeInstruction.LoadLocal(loadSection.StringDictLocal),
            new CodeInstruction(OpCodes.Call, AsyncMethodCall.StoreDictionaryForStateMethod), //Store string dict
            new CodeInstruction(OpCodes.Dup),
            CodeInstruction.StoreLocal(0), //Store state in local 0
            context.StateField.Stfld(), //and in state field
        
            CodeInstruction.LoadLocal(taskAwaiter.LocalIndex), //load awaiter and store in external dict
            new CodeInstruction(OpCodes.Box, awaiterType),
            CodeInstruction.LoadLocal(0), //Load generated fake state key
            AsyncMethodCall.StoreAwaiterMethod.Call()
        ]);
        
        //perform await
        var retLabel = generator.DefineLabel();
        leaveLabels.Add(retLabel);
        endingSection.AwaitAsyncInstruction.WithLabels(retLabel);
        
        stateInstructions.AddRange([
            CodeInstruction.LoadArgument(0), //Get builder and AwaitUnsafeOnCompleted
            new CodeInstruction(OpCodes.Ldflda, context.BuilderField),
            CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true),
            CodeInstruction.LoadArgument(0, !valueTypeMachine),
            awaitUnsafe.Call(),
            new CodeInstruction(OpCodes.Leave, retLabel)
        ]);
        
        //Section 3 - restore state
        var resumeInstruction = new CodeInstruction(OpCodes.Nop).WithLabels(resumeLabel);
        
        stateInstructions.AddRange([
            resumeInstruction,
            CodeInstruction.LoadLocal(loadSection.StateKeyLocal),
            AsyncMethodCall.GetAwaiterMethod.Call(),
            new CodeInstruction(OpCodes.Unbox_Any, awaiterType),
            CodeInstruction.StoreLocal(taskAwaiter.LocalIndex) //Store in local
        ]);
        //Code to reset field that was holding the awaiter, not necessary due to external store
        //newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true));
        //newCode.Add(new CodeInstruction(OpCodes.Initobj, awaiterType));
        
        //Set state to -1
        stateInstructions.AddRange([
            CodeInstruction.LoadArgument(0),
            (-1).LoadConstant(),
            new CodeInstruction(OpCodes.Dup),
            CodeInstruction.StoreLocal(0),
            context.StateField.Stfld()
        ]);
        
        //Section 4 - get result
        stateInstructions.Add(new CodeInstruction(OpCodes.Nop).WithLabels(endingSectionLabel));
        stateInstructions.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true));
        stateInstructions.Add(CodeInstruction.Call(awaiterType, "GetResult"));

        //Can do 3 things with result:
        //Store (in resultParam or by name)
        //Return
        //Ignore
        switch (resultType)
        {
            case AsyncMethodCall.ResultType.Return:
                var endAsyncUnconditionalLabel = generator.DefineLabel();
                endingSection.FinishAsyncInstruction.WithLabels(endAsyncUnconditionalLabel);
                
                stateInstructions.Add(returnType == typeof(void)
                    ? new CodeInstruction(OpCodes.Nop)
                    : CodeInstruction.StoreLocal(1));
                stateInstructions.Add(new CodeInstruction(OpCodes.Leave, endAsyncUnconditionalLabel));
                break;
            case AsyncMethodCall.ResultType.ReturnIf:
                //Currently a bool on stack.
                var skipLeaveLabel = generator.DefineLabel();
                var endAsyncConditionalLabel = generator.DefineLabel();
                endingSection.FinishAsyncInstruction.WithLabels(endAsyncConditionalLabel);
                stateInstructions.Add(new CodeInstruction(OpCodes.Brfalse_S, skipLeaveLabel));
                stateInstructions.Add(new CodeInstruction(OpCodes.Leave, endAsyncConditionalLabel));
                stateInstructions.Add(new CodeInstruction(OpCodes.Nop).WithLabels(skipLeaveLabel));
                break;
            case AsyncMethodCall.ResultType.Named:
                if (resultParam != null)
                {
                    resultParam.AddStoreInstructions(stateInstructions);
                }
                else if (resultName != null)
                {
                    if (returnType.IsValueType)
                    {
                        stateInstructions.Add(new CodeInstruction(OpCodes.Box, returnType));
                    }
                    stateInstructions.Add(CodeInstruction.LoadLocal(loadSection.StringDictLocal));
                    stateInstructions.Add(new CodeInstruction(OpCodes.Ldstr, resultName));
                    stateInstructions.Add(AsyncMethodCall.StoreNamedMethod.Call());
                }
                break;
            default:
                //Don't need to keep result.
                stateInstructions.Add(new CodeInstruction(returnType == typeof(void) ? OpCodes.Nop : OpCodes.Pop));
                break;
        }

        return new BranchSection
        {
            State = new StateInfo(context.NextStateIndex, stateInstructions, callMethod),
            ResumeInstruction = resumeInstruction,
            LeaveLabels = leaveLabels
        };
    }

    public required CodeInstruction ResumeInstruction { get; init; }

    public required StateInfo State { get; init; }
    public required IEnumerable<Label> LeaveLabels { get; init; }
    
    public List<CodeInstruction> Code => State.Code;
    public IEnumerable<StateInfo> AllStates => [State];

    public BranchingStateSection BranchTo(AsyncMethodContext context, StateInfo targetState, ILGenerator generator, out Label resumeLabel)
    {
        throw new InvalidOperationException("Non-branching state section cannot perform branching.");
    }
}