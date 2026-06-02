using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Extensions;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

internal class BranchingStateSection : IAsyncStateSection
{
    public static BranchingStateSection Read(AsyncMethodContext context, LoadStateSection loadStateSection, IEnumerator<CodeInstruction> codeEnumerator)
    {
        BaseLibMain.Logger.Debug("Starting branching section");
        List<CodeInstruction> branchSection = [];
        
        //First, analyze initial branching to determine starts of each state branch.
        //If branch is conditional, determine required state. If conditional branch destination is an unconditional branch, update that state's position.
        Dictionary<int, Label> stateLabels = [];
        int? checkState = null;
        int stateOffset = 0;
        
        bool exitLoop = false;

        do
        {
            var instruction = codeEnumerator.Current;

            if (branchSection.Count == 0 || instruction.opcode == OpCodes.Ldloc_0)
            {
                branchSection.Add(instruction);
            }
            else if (instruction.opcode == OpCodes.Switch)
            {
                var labelArr = (Label[]) instruction.operand;
                for (int i = 0; i < labelArr.Length; ++i)
                {
                    stateLabels[i + stateOffset] = labelArr[i];
                }
                branchSection.Add(instruction);

                codeEnumerator.MoveNext();
                break;
            }
            else if (instruction.TryGetIntValue(out var loadedConst))
            {
                checkState = loadedConst;
                branchSection.Add(instruction);
            }
            else
            {
                switch (instruction.opcode.Value)
                {
                    case (int) OpCodeValues.Nop:
                        if (instruction.labels.Count > 0)
                        {
                            exitLoop = true;
                        }
                        break;
                    case (int) OpCodeValues.Sub:
                        if (checkState == null)
                        {
                            BaseLibMain.Logger.Warn("Failed to evaluate sub, checkState null");
                            break;
                        }

                        if (stateOffset != 0)
                        {
                            BaseLibMain.Logger.Warn("Failed to process sub, stateOffset already set");
                            break;
                        }

                        stateOffset = checkState.Value;
                        checkState = null;
                        BaseLibMain.Logger.Debug($"Branching section uses sub offset of {stateOffset}");
                        break;
                    case (int) OpCodeValues.Brfalse_S:
                    case (int) OpCodeValues.Brfalse: //Branch if current state == 0
                        stateLabels[0] = (Label)instruction.operand;
                        //BaseLibMain.Logger.Info($"State 0 dest {stateLabels[0].Id}");
                        break;
                    case (int) OpCodeValues.Brtrue_S:
                    case (int) OpCodeValues.Brtrue: //What
                        BaseLibMain.Logger.Warn("Unexpected Brtrue in jump section of state machine");
                        break;
                    case (int) OpCodeValues.Beq_S:
                    case (int) OpCodeValues.Beq: //Branch if current state == ?
                        if (checkState == null)
                        {
                            BaseLibMain.Logger.Warn("Failed to evaluate beq, checkState null");
                            break;
                        }
                        stateLabels[checkState.Value] = (Label)instruction.operand;
                        //BaseLibMain.Logger.Info($"State {checkState.Value} dest {stateLabels[checkState.Value].Id}");
                        break;
                    case (int) OpCodeValues.Br_S:
                    case (int) OpCodeValues.Br: //Unconditional branch. State -1 or intermediate jump.
                        var opLabel = (Label)instruction.operand;
                        foreach (var entry in stateLabels)
                        {
                            foreach (var label in instruction.labels)
                            {
                                if (entry.Value == label)
                                {
                                    stateLabels[entry.Key] = opLabel;
                                    //BaseLibMain.Logger.Info($"State {entry.Key} dest {stateLabels[entry.Key].Id}");
                                    break;
                                }
                            }
                        }
                        break;
                    default:
                        BaseLibMain.Logger.Debug($"Found end of branching section");
                        exitLoop = true;
                        break;
                }

                if (!exitLoop)
                    branchSection.Add(instruction);
                else
                    break; //Skip MoveNext
            }
        } while (codeEnumerator.MoveNext());
        
        Dictionary<Label, int> labelStates = [];
        foreach (var entry in stateLabels)
        {
            labelStates[entry.Value] = entry.Key;
        }

        var branchingStateSection = new BranchingStateSection()
        {
            Branching = branchSection,
            LabelStates = labelStates
        };
        
        //Now check branches found
        while (stateLabels.Count > 0)
        {
            var newSection = BranchSection.Read(context, loadStateSection, branchingStateSection, codeEnumerator);
            foreach (var state in newSection.AllStates)
            {
                stateLabels.Remove(state.Index);
            }

            branchingStateSection.Sections.Add(newSection);
        }

        return branchingStateSection;
    }

    public required Dictionary<Label, int> LabelStates { get; init; }

    public List<CodeInstruction> Prep { get; set; } = [];
    public required List<CodeInstruction> Branching { get; set; }
    public readonly List<IAsyncStateSection> Sections = [];
    
    public CodeInstruction ResumeInstruction => Branching.First();

    public List<CodeInstruction> Code =>
    [
        ..Prep,
        ..Branching,
        ..Sections.SelectMany(section => section.Code)
    ];

    public IEnumerable<StateInfo> AllStates => Sections.SelectMany(section => section.AllStates);
    public IEnumerable<Label> LeaveLabels => Sections.SelectMany(section => section.LeaveLabels);


    /// <summary>
    /// Generates code instructions to branch to a new state labeled with resumeLabel.
    /// </summary>
    public BranchingStateSection BranchTo(AsyncMethodContext context, StateInfo targetState, ILGenerator generator, out Label resumeLabel)
    {
        foreach (var branch in Sections)
        {
            if (!branch.AllStates.Contains(targetState))
                continue;
            
            resumeLabel = generator.DefineLabel();
            BaseLibMain.Logger.Debug($"Generating branch instruction in branching section using label {resumeLabel.Id}");

            for (var i = 0; i < Branching.Count; ++i)
            {
                var ci = Branching[i];
                if (ci.blocks.Count == 0) continue;
                
                if (i == 0)
                {
                    Branching = [
                        CodeInstruction.LoadLocal(0).MoveLabelsFrom(Branching[0]).MoveBlocksFrom(Branching[0]),
                        context.NextStateIndex.LoadConstant(),
                        new CodeInstruction(OpCodes.Beq, resumeLabel),
                        ..Branching
                    ];
                }
                else
                {
                    Branching = [
                        ..Branching.Take(i),
                        CodeInstruction.LoadLocal(0).MoveBlocksFrom(Branching[i]),
                        context.NextStateIndex.LoadConstant(),
                        new CodeInstruction(OpCodes.Beq, resumeLabel),
                        ..Branching.Skip(i)
                    ];
                }
                break;
            }
            
            
            if (branch is BranchingStateSection branchingBranch)
            {
                branchingBranch.ResumeInstruction.WithLabels(resumeLabel);
                var innerBranch = branchingBranch.BranchTo(context, targetState, generator, out resumeLabel);
                return innerBranch;
            }
            
            return this;
        }

        throw new InvalidOperationException("Failed to find target state in branching state section.");
    }

    public void InsertState(AsyncMethodContext context, LoadStateSection loadSection, EndingSection endingSection, 
        bool before, StateInfo targetState, MethodInfo callMethod, 
        List<StateParamInfo> methodCallParams, Label resumeLabel, AsyncMethodCall.ResultType resultType, string? resultName)
    {
        int insertIndex = Sections.FindIndex(section => section is BranchSection branchSection && branchSection.State == targetState);
        if (insertIndex < 0)
            throw new InvalidOperationException("Failed to find target state in branching state section.");

        if (!before)
            ++insertIndex;
        
        Sections.Insert(insertIndex, BranchSection.Create(context, loadSection, endingSection, 
            callMethod, methodCallParams, resumeLabel, resultType, resultName));
    }
}