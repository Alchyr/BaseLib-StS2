using System.Reflection;
using HarmonyLib;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

internal record StateParamInfo(
    ParameterInfo Parameter,
    Action<List<CodeInstruction>> AddLoadInstructions,
    Action<List<CodeInstruction>> AddStoreInstructions);