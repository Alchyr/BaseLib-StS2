using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class ModelExtensions
{
    public static string LocKey(this AbstractModel model, string subKey)
    {
        return $"{model.Id.Entry}.{subKey}";
    }
}