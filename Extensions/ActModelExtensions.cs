using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class ActModelExtensions
{
    /// <summary>
    /// Returns 1-based index of an act, or its index value if it is less than 0.
    /// </summary>
    /// <param name="actModel"></param>
    /// <returns></returns>
    public static int ActNumber(this ActModel actModel)
    {
        return actModel.Index >= 0 ? actModel.Index + 1 : actModel.Index;
    }
}