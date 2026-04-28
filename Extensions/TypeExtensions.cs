using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace BaseLib.Extensions;

public static class TypeExtensions
{
    private static Dictionary<Type, List<FieldInfo>> _declaredFields = [];

    /// <summary>
    /// Finds a field in a generated state machine class for an async method, given the
    /// state machine type and a name that the field's name should contain.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="originalFieldName"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static FieldInfo FindStateMachineField(this Type type, string originalFieldName)
    {
        string stateMachineFieldName = $"<{originalFieldName}>";
        if (!_declaredFields.TryGetValue(type, out var declaredFields))
        {
            declaredFields = type.GetDeclaredFields();
        }
        foreach (var field in declaredFields)
        {
            if (field.Name.StartsWith(stateMachineFieldName))
            {
                return field;
            }
            if (field.Name.Equals(originalFieldName)) //local variables may have their name kept as-is
            {
                return field;
            }
        }
        foreach (var field in declaredFields)
        {
            if (field.Name.Contains(originalFieldName))
            {
                return field;
            }
        }
        throw new ArgumentException($"No matching field found in type {type} for name {originalFieldName}");
    }
    
    
    // Source - https://stackoverflow.com/a/7182379

    /// <summary>
    /// Search for a method by name and parameter types.  
    /// Unlike GetMethod(), does 'loose' matching on generic
    /// parameter types, and searches base interfaces.
    /// </summary>
    /// <exception cref="AmbiguousMatchException"/>
    public static MethodInfo? GetMethodExt( this Type thisType, 
                                            string name, 
                                            params Type?[] parameterTypes)
    {
        return GetMethodExt(thisType, 
                            name, 
                            BindingFlags.Instance 
                            | BindingFlags.Static 
                            | BindingFlags.Public 
                            | BindingFlags.NonPublic
                            | BindingFlags.FlattenHierarchy, 
                            parameterTypes);
    }

    /// <summary>
    /// Search for a method by name, parameter types, and binding flags.  
    /// Unlike GetMethod(), does 'loose' matching on generic
    /// parameter types, and searches base interfaces.
    /// </summary>
    /// <exception cref="AmbiguousMatchException"/>
    public static MethodInfo? GetMethodExt( this Type thisType, 
                                            string name, 
                                            BindingFlags bindingFlags, 
                                            params Type?[] parameterTypes)
    {
        MethodInfo? matchingMethod = null;

        // Check all methods with the specified name, including in base classes
        GetMethodExt(ref matchingMethod, thisType, name, bindingFlags, parameterTypes);

        // If we're searching an interface, we have to manually search base interfaces
        if (matchingMethod == null && thisType.IsInterface)
        {
            foreach (Type interfaceType in thisType.GetInterfaces())
                GetMethodExt(ref matchingMethod, 
                             interfaceType, 
                             name, 
                             bindingFlags, 
                             parameterTypes);
        }

        return matchingMethod;
    }

    private static void GetMethodExt(   ref MethodInfo? matchingMethod, 
                                        Type type, 
                                        string name, 
                                        BindingFlags bindingFlags, 
                                        params Type?[] parameterTypes)
    {
        // Check all methods with the specified name, including in base classes
        foreach (MethodInfo methodInfo in type.GetMethods(bindingFlags))
        {
            if (!methodInfo.Name.Equals(name))
            {
                continue;
            }
            // Check that the parameter counts and types match, 
            // with 'loose' matching on generic parameters
            ParameterInfo[] parameterInfos = methodInfo.GetParameters();
            if (parameterInfos.Length == parameterTypes.Length)
            {
                int i = 0;
                for (; i < parameterInfos.Length; ++i)
                {
                    if (!parameterInfos[i].ParameterType
                                          .IsSimilarType(parameterTypes[i]))
                        break;
                }
                if (i == parameterInfos.Length)
                {
                    if (matchingMethod == null)
                        matchingMethod = methodInfo;
                    else
                        throw new AmbiguousMatchException(
                               "More than one matching method found!");
                }
            }
        }
    }

    /// <summary>
    /// Special type used to match any generic parameter type in GetMethodExt().
    /// </summary>
    public static class GenericParam
    { }

    /// <summary>
    /// Determines if the two types are either identical, or are both generic 
    /// parameters or generic types with generic parameters in the same
    ///  locations (generic parameters match any other generic paramter,
    /// but NOT concrete types).
    /// </summary>
    private static bool IsSimilarType(this Type thisType, Type? type)
    {
        if (type == null) return true; //null type is wildcard
        
        // Ignore any 'ref' types
        if (thisType.IsByRef)
            thisType = thisType.GetElementType()!;
        if (type.IsByRef)
            type = type.GetElementType()!;

        // Handle array types
        if (thisType.IsArray && type.IsArray)
            return thisType.GetElementType()!.IsSimilarType(type.GetElementType()!);

        // If the types are identical, or they're both generic parameters 
        // or the special 'GenericParam' type, treat as a match
        if (thisType == type || ((thisType.IsGenericParameter || thisType == typeof(GenericParam)) 
                             && (type.IsGenericParameter || type == typeof(GenericParam))))
            return true;

        // Handle any generic arguments
        if (thisType.IsGenericType && type.IsGenericType)
        {
            Type[] thisArguments = thisType.GetGenericArguments();
            Type[] arguments = type.GetGenericArguments();
            if (thisArguments.Length == arguments.Length)
            {
                for (int i = 0; i < thisArguments.Length; ++i)
                {
                    if (!thisArguments[i].IsSimilarType(arguments[i]))
                        return false;
                }
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<CodeInstruction> BoxArg0(this Type t) {
        yield return CodeInstruction.LoadArgument(0);
        if (!t.IsValueType) yield break;
        yield return new CodeInstruction(OpCodes.Box, t);
    }
}
