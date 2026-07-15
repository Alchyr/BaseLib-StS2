using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

public interface IAfterSpendResource<T> where T : CustomResource
{
    Task AfterSpendResource(ICombatState combatState, AbstractModel? spender, int amount);
}