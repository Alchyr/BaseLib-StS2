using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class RelicModelExtensions
{
    private static readonly Dictionary<ModelId, Func<AncientEventModel, bool>> CanSpawnAtAncient = [];
    /// <summary>
    /// Call this in your relic's constructor to add a condition to its ability to spawn at a custom ancient.
    /// The condition should be based on the given event model's owner.
    /// </summary>
    public static void AddCustomAncientSpawnCondition(this RelicModel model, Func<AncientEventModel, bool> condition)
    {
        if (CanSpawnAtAncient.Remove(model.Id))
        {
            BaseLibMain.Logger.Warn($"Custom ancient spawn condition set for relic {model.Id} multiple times");
        }
        CanSpawnAtAncient[model.Id] = condition;
    }
    public static bool RelicCanSpawnAtCustomAncient(this RelicModel model, AncientEventModel ancient)
    {
        if (!CanSpawnAtAncient.TryGetValue(model.Id, out var condition)) return true;
        return condition(ancient);
    }
}