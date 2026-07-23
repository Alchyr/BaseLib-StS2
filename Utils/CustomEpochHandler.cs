using HarmonyLib;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Utils;

public static class CustomEpochHandler
{
    
    private static readonly List<Type> AllEpochs = AccessTools.StaticFieldRefAccess<List<Type>>(typeof(EpochModel), "_allEpochs");
    
    /// <summary>
    /// Insert the epoch type into <see cref="EpochModel._allEpochs"/>
    /// </summary>
    public static void InsertIntoAllEpochs(EpochModel model)
    {
        if (!AllEpochs.Contains(model.GetType()))
            AllEpochs.Add(model.GetType());
    }
    /// <summary>
    /// Insert the epochs type into <see cref="EpochModel._allEpochs"/>
    /// </summary>
    public static void InsertIntoAllEpochs(IEnumerable<EpochModel> models)
    {
        foreach (var model in models)
            InsertIntoAllEpochs(model);
    }
}