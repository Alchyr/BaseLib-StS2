using HarmonyLib;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace BaseLib.Extensions;

public static class MethodInfoExtensions
{
    public static Type StateMachineType(this MethodInfo methodInfo)
    {
        var stateMachineAttribute = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachineAttribute == null) throw new ArgumentException($"MethodInfo {methodInfo.FullDescription()} is not an async method");
        return stateMachineAttribute.StateMachineType;
    }

    public static CodeInstruction Call(this MethodInfo methodInfo)
    {
        return new CodeInstruction(OpCodes.Call, methodInfo);
    }
    public static CodeInstruction CallVirt(this MethodInfo methodInfo)
    {
        return new CodeInstruction(OpCodes.Callvirt, methodInfo);
    }
}
