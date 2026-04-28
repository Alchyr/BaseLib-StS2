using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;

namespace BaseLib.Extensions;

public static class ActModelExtensions
{
    public static int ActNumber(this ActModel actModel)
    {
        return actModel switch
        {
            Overgrowth or Underdocks => 1,
            Hive => 2,
            Glory => 3,
            CustomActModel custom => custom.ActNumber,
            _ => -1
        };
    }
}