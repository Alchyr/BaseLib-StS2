using MegaCrit.Sts2.Core.Entities.Creatures;

namespace BaseLib.Hooks;

public interface IHealAmountModifier
{
    decimal ModifyHealAdditive(Creature creature, decimal amount) => 0m;
    decimal ModifyHealMultiplicative(Creature creature, decimal amount) => 1m;
}
