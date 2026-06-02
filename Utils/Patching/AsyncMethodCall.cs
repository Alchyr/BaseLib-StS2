using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Extensions;
using BaseLib.Utils.Patching.AsyncMethodSections;
using HarmonyLib;

namespace BaseLib.Utils.Patching;

public static class AsyncMethodCall
{
    internal enum ResultType
    {
        None,
        Named,
        Return,
        ReturnIf
    }

    internal static readonly MethodInfo StoreStateInDictMethod = typeof(AsyncMethodCall).Method(nameof(StoreStateInDict));
    internal static readonly MethodInfo LoadStateFromDictMethod = typeof(AsyncMethodCall).Method(nameof(LoadStateFromDict));

    internal static readonly MethodInfo StoreDictionaryForStateMethod = typeof(AsyncMethodCall).Method(nameof(StoreDictionaryForState));
    internal static readonly MethodInfo LoadDictionaryForStateMethod = typeof(AsyncMethodCall).Method(nameof(LoadDictionaryForState));
    
    internal static readonly MethodInfo GetAwaiterMethod = typeof(AsyncMethodCall).Method(nameof(GetAwaiter));
    internal static readonly MethodInfo StoreAwaiterMethod = typeof(AsyncMethodCall).Method(nameof(StoreAwaiter));
    internal static readonly MethodInfo GetNamedMethod = typeof(AsyncMethodCall).Method(nameof(GetNamed));
    internal static readonly MethodInfo StoreNamedMethod = typeof(AsyncMethodCall).Method(nameof(StoreNamed));
    

    internal static readonly InstructionMatcher StateAwaitMatcher = new InstructionMatcher()
        .any().PredicateMatch(arg =>
        {
            switch (arg)
            {
                case MethodInfo method
                    when method.ReturnType.IsAssignableTo(typeof(Task)):
                case FieldInfo field
                    when field.FieldType.IsAssignableTo(typeof(Task)):
                    return true;
                default:
                    return false;
            }
        })
        .callvirt(null).PredicateMatch(arg => arg is MethodInfo { Name: "GetAwaiter" });
    
    #region external_state_methods
    
    private static readonly Dictionary<MethodBase, HashSet<string>> AddedNames = [];

    private static readonly ConcurrentDictionary<int, int> StateDictionary = [];
    private const int MinKey = -357913941;
    private const int MaxKey = -178956970;
    private static int _fakeStateKey = MinKey;

    private static readonly Dictionary<string, object> AwaiterDictionary = [];
    private static readonly Dictionary<int, Dictionary<string, object>> SavedValuesDictionary = [];
    

    private static int StoreStateInDict(int stateKey)
    {
        int tempKey = Interlocked.Increment(ref _fakeStateKey);
        if (tempKey > MaxKey)
        {
            //May happen twice, but shouldn't cause an issue as tempKey values will remain unique.
            _fakeStateKey = MinKey;
        }

        if (StateDictionary.ContainsKey(tempKey))
        {
            BaseLibMain.Logger.Warn($"Extremely old state key {tempKey} still left in async state dictionary");
        }

        BaseLibMain.Logger.Debug($"Stored temp state key: {stateKey} -> {tempKey}");
        StateDictionary[tempKey] = stateKey;
        return tempKey;
    }
    private static int LoadStateFromDict(int stateKey)
    {
        if (StateDictionary.Remove(stateKey, out var realState))
        {
            BaseLibMain.Logger.Debug($"Loaded state from dict: {stateKey} -> {realState}");
            return realState;
        }
        BaseLibMain.Logger.VeryDebug($"State not in dict: {stateKey}");
        return stateKey;
    }

    private static void StoreDictionaryForState(int stateKey, Dictionary<string, object> dict)
    {
        if (SavedValuesDictionary.ContainsKey(stateKey))
        {
            BaseLibMain.Logger.Warn($"Extremely old state key {stateKey} still left in async saved values dictionary");
        }

        SavedValuesDictionary[stateKey] = dict;
    }
    private static Dictionary<string, object> LoadDictionaryForState(int stateKey)
    {
        if (SavedValuesDictionary.Remove(stateKey, out var stringDict))
        {
            BaseLibMain.Logger.Debug($"Loaded dictionary for state {stateKey}");
            return stringDict;
        }
        return [];
    }

    private static void StoreAwaiter(object awaiter, int fakeStateIndex) //object keyObj, int stateIndex)
    {
        var stringKey = $"__state__{fakeStateIndex}";
        BaseLibMain.Logger.Debug($"Storing awaiter using fake state key {stringKey}");
        AwaiterDictionary[stringKey] = awaiter;
    }
    private static object GetAwaiter(int fakeStateIndex)
    {
        var stringKey = $"__state__{fakeStateIndex}";
        AwaiterDictionary.Remove(stringKey, out var result);
        BaseLibMain.Logger.Debug($"Retrieved awaiter state {fakeStateIndex}: {result}");
        return result!;
    }
    
    private static object GetNamed(Dictionary<string, object> dict, string name)
    {
        BaseLibMain.Logger.Debug($"Load awaiter val {name}");
        return dict[name];
    }
    private static void StoreNamed(object val, Dictionary<string, object> dict, string name)
    {
        dict[name] = val;
        BaseLibMain.Logger.Debug($"Store awaiter val {name}: {val}");
    }
    #endregion


    /// <summary>
    /// Given the CodeInstructions of an async state machine's MoveNext method,
    /// insert an async method call into it, creating another state.
    /// beforeState or afterState must be provided, being an async method awaited by the original method (and so having its own state).
    /// The original method itself can be passed to beforeState or afterState. If so, the target position will be the first or last state.
    /// 
    /// Parameters of the method will attempt to be found by name. If a parameter cannot be determined an exception will be thrown.
    /// 
    /// If resultName is provided and the method to call returns a value, it will be stored in a variable of this name using
    /// an external dictionary, and can be passed into subsequent calls by defining a parameter with this name.
    /// 
    /// If resultName is "return" the result of the method will attempt to be returned immediately.
    /// If resultName is "returnIf" and the method called has a boolean return value, the state machine will return early.
    /// This will not work if the state machine method has a non-void return type.
    /// 
    /// If resultName is the same as one of the method's parameters, it will be attempted to store the result in the variable
    /// passed to that parameter.
    /// If this does not match the correct return type, an exception will be thrown when patching.
    /// 
    /// </summary>
    /// <param name="generator"></param>
    /// <param name="code"></param>
    /// <param name="original">The method being patched, provided to transpiler patches.</param>
    /// <param name="callMethod">A method that returns a Task that will be called.</param>
    /// <param name="beforeState"></param>
    /// <param name="afterState"></param>
    /// <param name="resultName"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static List<CodeInstruction> Create(ILGenerator generator, IEnumerable<CodeInstruction> code, MethodBase original, MethodInfo callMethod, MethodBase? beforeState = null, MethodBase? afterState = null, string? resultName = null)
    {
        if (beforeState == null && afterState == null)
        {
            throw new ArgumentException(
                "Only one of beforeState or afterState should be provided to determine where to insert the async method call.");
        }

        var targetMethod = beforeState ?? afterState ?? throw new ArgumentException(
            "Either beforeState or afterState must be provided to determine where to insert the async method call.");
        var before = beforeState != null;

        if (!original.Name.Equals("MoveNext"))
        {
            throw new ArgumentException("Target method of AsyncMethodCall should be MoveNext of async state machine");
        }
        
        // Check if method to call is even an async method.
        if (!callMethod.ReturnType.IsAssignableTo(typeof(Task)))
        {
            throw new ArgumentException("Method to call must return a Task");
        }

        if (!callMethod.IsStatic)
        {
            throw new ArgumentException("Method to call must be static");
        }

        var stateMachineType = original.DeclaringType ??
                               throw new ArgumentException(
                                   $"Failed to get state machine type from method '{original.FullDescription()}'");
        
        BaseLibMain.Logger.Info($"Patching state machine: {stateMachineType.FullName}");

        AsyncMethodContext context = new()
        {
            Generator = generator,
            BuilderField = stateMachineType.FindStateMachineField("t__builder"),
            StateField = stateMachineType.FindStateMachineField("__state"),
            StateMachineType = stateMachineType
        };

        MoveNextSection stateSections;
        using (var codeEnumerator = code.GetEnumerator()) {
            codeEnumerator.MoveNext();
            stateSections = MoveNextSection.Read(context, codeEnumerator);
        }
        
        if (!stateSections.AllStates.Any())
            throw new Exception($"Failed to find any states for async method {original.Name}");

        //Find target state
        StateInfo? targetState = null;
        if (targetMethod == original)
        {
            targetState = before ? stateSections.AllStates.First() : stateSections.AllStates.Last();
            targetMethod = targetState.StateMethod;
        }
        else
        {
            foreach (var state in stateSections.AllStates)
            {
                if (state.StateMethod == targetMethod)
                {
                    targetState = state;
                    break;
                }
            }
        }

        if (targetState == null)
            throw new ArgumentException($"Unable to find state for target method {targetMethod?.Name}");
        
        //Generate getters/setters for fields that match target method parameter names
        var methodCallParams = callMethod.GetParameters()
            .Select(param => MakeStateParameter(original, context, stateSections.LoadSection.StringDictLocal, param)).ToList();
        
        var resultType = resultName?.ToLowerInvariant() == "return" ? ResultType.Return : 
            resultName?.ToLowerInvariant() == "returnif" ? ResultType.ReturnIf : 
            resultName != null ? ResultType.Named : ResultType.None;

        var returnType = stateSections.EndingSection.ReturnType;
        
        switch (resultType)
        {
            case ResultType.Return:
                if (returnType != null)
                {
                    if (!callMethod.ReturnType.IsGenericType)
                    {
                        throw new ArgumentException($"resultName set to return patching method with return type {returnType} but method to call does not return a value; return type {callMethod.ReturnType}");
                    }
                
                    if (!callMethod.ReturnType.GenericTypeArguments[0].IsAssignableTo(returnType))
                    {
                        throw new ArgumentException(
                            $"Cannot assign result of type {callMethod.ReturnType.GenericTypeArguments[0]} to return type {returnType}");
                    }
                }
                break;
            case ResultType.ReturnIf:
                if (!callMethod.ReturnType.IsGenericType)
                {
                    throw new ArgumentException("resultName set to returnIf but method to call does not return a value; requires bool");
                }
                
                if (!callMethod.ReturnType.GenericTypeArguments[0].IsAssignableTo(typeof(bool)))
                {
                    throw new ArgumentException(
                        $"Result  {callMethod.ReturnType.GenericTypeArguments[0]} to return type {returnType}");
                }
                break;
            case ResultType.Named:
                if (!callMethod.ReturnType.IsGenericType)
                {
                    throw new ArgumentException($"resultName set but method to call does not return a value");
                }

                bool storeExisting = false;
                foreach (var info in methodCallParams)
                {
                    if (info.Parameter.Name == resultName)
                    {
                        storeExisting = true;
                        break;
                    }
                }

                if (!storeExisting) //New named result
                {
                    if (!AddedNames.TryGetValue(original, out var dict))
                    {
                        dict = [];
                        AddedNames[original] = dict;
                    }

                    dict.Add(resultName!);
                }
                break;
        }
        
        BaseLibMain.Logger.Info($"Adding new state {context.NextStateIndex} for method {callMethod.DeclaringType?.Name ?? "???"}.{callMethod.Name} {(before ? "before" : "after")} {targetMethod?.Name ?? targetState.Index.ToString()} with result type {resultType} ({resultName})");
        
        //Generate new state
        stateSections.InsertState(context, before, targetState, callMethod, methodCallParams, resultType, resultName);
        
        //Generate combined result
        var instructions = stateSections.Code;
        BaseLibMain.Logger.Debug($"CODE:\n{instructions.Join(instruction => instruction.ToString(), "\n")}");
        instructions.CheckCode();
        return instructions;
    }

    private static StateParamInfo MakeStateParameter(MethodBase method, AsyncMethodContext context, int stringDictLocal, ParameterInfo param)
    {
        if (param.Name == null) throw new Exception("Unable to determine parameter name for method to call for async method call");

        Action<List<CodeInstruction>>? addLoadInstructions;
        Action<List<CodeInstruction>>? addStoreInstructions;
        
        if (param.Name == "__instance")
        {
            if (method.IsStatic)
                throw new ArgumentException("Unable to use __instance parameter when patching static method");
            var thisField = context.StateMachineType.FindStateMachineField("__this");

            addLoadInstructions = list =>
            {
                list.Add(CodeInstruction.LoadArgument(0));
                list.Add(thisField.Ldfld());
            };
            addStoreInstructions = list =>
            {
                list.Add(new CodeInstruction(OpCodes.Pop));
            };
        }
        else if (AddedNames.TryGetValue(method, out var dict) && dict.Contains(param.Name))
        {
            BaseLibMain.Logger.Debug($"Using named result {param.Name} in method {method.Name}");
            addLoadInstructions = list =>
            {
                list.Add(CodeInstruction.LoadLocal(stringDictLocal));
                list.Add(new CodeInstruction(OpCodes.Ldstr, param.Name));
                list.Add(GetNamedMethod.Call());
                if (param.ParameterType.IsValueType)
                {
                    list.Add(new CodeInstruction(OpCodes.Unbox_Any, param.ParameterType));
                }
            };
            addStoreInstructions = list =>
            {
                if (param.ParameterType.IsValueType)
                {
                    list.Add(new CodeInstruction(OpCodes.Box, param.ParameterType));
                }
                list.Add(CodeInstruction.LoadLocal(stringDictLocal));
                list.Add(new CodeInstruction(OpCodes.Ldstr, param.Name));
                list.Add(StoreNamedMethod.Call());
            };
        }
        else
        {
            var field = context.StateMachineType.FindStateMachineField(param.Name);
            if (!field.FieldType.IsAssignableTo(param.ParameterType))
            {
                throw new ArgumentException(
                    $"Unable to pass field {field.Name} of type {field.FieldType} to parameter {param.Name} of type {param.ParameterType}");
            }
            
            addLoadInstructions = list =>
            {
                list.Add(CodeInstruction.LoadArgument(0));
                list.Add(field.Ldfld());
            };
            addStoreInstructions = list =>
            {
                //Last two instructions of list should be get awaiter and get result
                list.Insert(list.Count - 2, CodeInstruction.LoadArgument(0));
                list.Add(field.Stfld());
            };
        }
        
        return new StateParamInfo(param, addLoadInstructions, addStoreInstructions);
    }
}