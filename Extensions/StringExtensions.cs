using System.Reflection.Emit;
using BaseLib.Utils.NodeFactories;
using Godot;
using HarmonyLib;

namespace BaseLib.Extensions;

public static class StringExtensions
{
    public static string RemovePrefix(this string id)
    {
        return id[(id.IndexOf(TypePrefix.PrefixSplitChar) + 1)..];
    }

    /// <summary>
    /// Registers a scene to be automatically converted to the specified node type when instantiated.
    /// Requires a factory to exist in NodeFactory<seealso cref="NodeFactory"/> to perform the conversion to the specified type.
    /// </summary>
    public static void RegisterSceneForConversion<TNode>(this string scenePath, Action<TNode>? postConversion = null) where TNode : Node
    {
        NodeFactory.RegisterSceneType(scenePath, postConversion);
    }

    internal static IEnumerable<CodeInstruction> MakeWriteLog(this string s)
    {
        yield return new CodeInstruction(OpCodes.Ldstr, s);
        yield return CodeInstruction.Call(typeof(StringExtensions), nameof(WriteLog));
    }

    internal static void WriteLog(string s)
    {
        BaseLibMain.Logger.Info(s);
    }
    internal static void WriteLogInt(int i)
    {
        BaseLibMain.Logger.Info(i.ToString());
    }
    internal static void WriteLogObj(object? o)
    {
        BaseLibMain.Logger.Info(o?.ToString() ?? "NULL");
    }
}
