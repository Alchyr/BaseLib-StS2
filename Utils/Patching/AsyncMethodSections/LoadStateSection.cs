using System.Reflection.Emit;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

internal class LoadStateSection : IAsyncMethodSection
{
    /// <summary>
    /// Reads a LoadStateSection using an enumerator that is already ready to read.
    /// </summary>
    public static LoadStateSection Read(AsyncMethodContext context, IEnumerator<CodeInstruction> codeEnumerator)
    {
        bool loadsFoundStateField = false;
        bool loadsStateFromDict = false;
        bool loadedDictionary = false;
        int? stateKeyLocalIndex = null;
        int? stringDictLocalIndex = null;
        
        List<CodeInstruction> loadStateSection = [];
        do
        {
            var instruction = codeEnumerator.Current;
            if (instruction.HasBlock(ExceptionBlockType.BeginExceptionBlock))
            {
                break;
            }

            loadStateSection.Add(instruction);
            if (instruction.LoadsField(context.StateField))
            {
                loadsFoundStateField = true;
            }

            if (!loadsStateFromDict)
            {
                if (instruction.Calls(AsyncMethodCall.LoadStateFromDictMethod))
                {
                    loadsStateFromDict = true;
                }
            }
            else
            {
                if (stateKeyLocalIndex == null && instruction.IsStloc())
                {
                    stateKeyLocalIndex = instruction.LocalIndex();
                }

                if (!loadedDictionary)
                {
                    if (instruction.Calls(AsyncMethodCall.LoadDictionaryForStateMethod))
                    {
                        loadedDictionary = true;
                    }
                }
                else
                {
                    if (stringDictLocalIndex == null && instruction.IsStloc())
                    {
                        stringDictLocalIndex = instruction.LocalIndex();
                    }
                }
            }
        }
        while (codeEnumerator.MoveNext());

        if (!loadsFoundStateField)
        {
            throw new ArgumentException(
                $"MoveNext does not load found state field {context.StateField}; failed to set up AsyncMethodCall properly");
        }

        if (!loadsStateFromDict)
        {
            BaseLibMain.Logger.Debug("Setting up external state");
            var stateKeyLocal = context.Generator.DeclareLocal(typeof(int));
            var stringDictLocal = context.Generator.DeclareLocal(typeof(Dictionary<string, object>));
            
            stateKeyLocalIndex = stateKeyLocal.LocalIndex;
            stringDictLocalIndex = stringDictLocal.LocalIndex;
            
            loadStateSection =
            [
                CodeInstruction.LoadArgument(0), //One for ldfld, one for stfld
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, context.StateField),
                new CodeInstruction(OpCodes.Dup),
                CodeInstruction.StoreLocal(stateKeyLocalIndex.Value),
                new CodeInstruction(OpCodes.Call, AsyncMethodCall.LoadStateFromDictMethod),
                new CodeInstruction(OpCodes.Stfld, context.StateField),
                CodeInstruction.LoadLocal(stateKeyLocalIndex.Value),
                new CodeInstruction(OpCodes.Call, AsyncMethodCall.LoadDictionaryForStateMethod),
                CodeInstruction.StoreLocal(stringDictLocalIndex.Value),
                ..loadStateSection
            ];
        }
        else if (stateKeyLocalIndex == null) //Loads state from dict but did not find local in which it is stored
        {
            throw new ArgumentException(
                "Failed to find local used to hold temporary state key.");
        }
        else if (stringDictLocalIndex == null)
        {
            throw new ArgumentException(
                "Failed to find local used to hold extra saved values.");
        }

        return new LoadStateSection
        {
            Code = loadStateSection,
            AddStateLoading = !loadsStateFromDict,
            StateKeyLocal = stateKeyLocalIndex.Value,
            StringDictLocal = stringDictLocalIndex.Value
        };
    }

    public required List<CodeInstruction> Code { get; init; }
    public required bool AddStateLoading { get; init; }
    public required int StringDictLocal { get; init; }
    public required int StateKeyLocal { get; init; }
    
    public IEnumerable<StateInfo> AllStates => [];
}