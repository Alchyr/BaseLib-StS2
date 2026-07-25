using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BaseLib.Extensions;


public static class HarmonyExtensions
{
    /// <summary>
    /// Attempts to apply all patches marked by harmony attributes of the specified category in an assembly.
    /// </summary>
    /// <returns></returns>
    public static bool TryPatchAll(this Harmony harmony, Assembly assembly, string? category = null)
    {
        BaseLibMain.Logger.Info($"Starting PatchAll for assembly {assembly}");
        try
        {
            var patchProcessors = AccessTools.GetTypesFromAssembly(assembly)
                .Where(type => type.HasHarmonyAttribute())
                .Select<Type, (Type, PatchClassProcessor)>(type => new (type, harmony.CreateClassProcessor(type)));

            var successCount = 0;
            var failCount = 0;
            patchProcessors.DoIf(processor => 
                    category?.Equals(processor.Item2.Category) ?? string.IsNullOrEmpty(processor.Item2.Category),
                delegate((Type, PatchClassProcessor) processor)
                {
                    try
                    {
                        processor.Item2.Patch();
                        BaseLibMain.Logger.Debug($"Patch {processor.Item1.FullName} successful.");
                        ++successCount;
                    }
                    catch (Exception e)
                    {
                        BaseLibMain.Logger.Error($"Patch {processor.Item1.FullName} failed;\n{e}");
                        ++failCount;
                    }
                });
            
            BaseLibMain.Logger.Info($"Applied {successCount} patches successfully, {failCount} failed");

            return failCount == 0;
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Error occurred during TryPatchAll for assembly {assembly}: {e}");
            return false;
        }
    }
    
    /// <summary>
    /// Not necessary! Use `MethodType.Async` in your HarmonyPatch annotation.
    /// </summary>
    /// <param name="harmony"></param>
    /// <param name="asyncMethod"></param>
    /// <param name="prefix"></param>
    /// <param name="postfix"></param>
    /// <param name="transpiler"></param>
    /// <param name="finalizer"></param>
    [Obsolete("Use MethodType.Async instead.")]
    public static void PatchAsyncMoveNext(this Harmony harmony, MethodInfo asyncMethod, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
    {
        var moveNextMethod = asyncMethod.StateMachineType().GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);

        harmony.Patch(moveNextMethod, prefix, postfix, transpiler, finalizer);
    }
    [Obsolete("Use MethodType.Async instead.")]
    public static void PatchAsyncMoveNext(this Harmony harmony, MethodInfo asyncMethod, out Type stateMachineType, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
    {
        var stateMachineAttribute = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachineAttribute == null) throw new ArgumentException($"MethodInfo {asyncMethod.FullDescription()} passed to PatchAsync is not an async method");
        stateMachineType = stateMachineAttribute.StateMachineType;
        var moveNextMethod = stateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);

        harmony.Patch(moveNextMethod, prefix, postfix, transpiler, finalizer);
    }
}
