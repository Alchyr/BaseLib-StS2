using System.Reflection;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

internal class MoveNextSection : IAsyncMethodSection
{
    /// <summary>
    /// Reads a new MoveNextSection using an enumerator that is already ready to read.
    /// </summary>
    public static MoveNextSection Read(AsyncMethodContext context, IEnumerator<CodeInstruction> codeEnumerator)
    {
        var loadStateSection = LoadStateSection.Read(context, codeEnumerator);
        var mainSection = BranchingStateSection.Read(context, loadStateSection, codeEnumerator);
        var endingSection = EndingSection.Read(mainSection, codeEnumerator);
        
        return new MoveNextSection
        {
            LoadSection = loadStateSection,
            MainSection = mainSection,
            EndingSection = endingSection
        };
    }
    
    public required LoadStateSection LoadSection { get; internal init; }
    public required IAsyncStateSection MainSection { get; internal init; }
    public required EndingSection EndingSection { get; internal init; }

    public List<CodeInstruction> Code =>
    [
        ..LoadSection.Code,
        ..MainSection.Code,
        ..EndingSection.Code
    ];

    public IEnumerable<StateInfo> AllStates => MainSection.AllStates;

    public void InsertState(AsyncMethodContext context, bool before, StateInfo targetState,
        MethodInfo callMethod, List<StateParamInfo> methodCallParams,
        AsyncMethodCall.ResultType resultType, string? resultName)
    {
        var generator = context.Generator;

        var branchParent = MainSection.BranchTo(context, targetState, generator, out var resumeLabel);

        branchParent.InsertState(context, LoadSection, EndingSection,
            before, targetState,  callMethod, methodCallParams, resumeLabel, resultType, resultName);
    }
}