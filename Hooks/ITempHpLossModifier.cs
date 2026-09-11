using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Hooks;

/// <summary>
///     Implement on a PowerModel or RelicModel to modify how much of a hit is applied to its owner's Temporary HP
///     (the StSLib OnLoseTempHpPower/OnLoseTempHpRelic equivalent).
///     Dispatched to the damaged creature's own powers and its player's relics, in that order, and only when the
///     creature actually has Temporary HP remaining.
///     Called synchronously inside the HP-loss pipeline: implementations MUST be pure — no awaits, no state
///     mutation, no VFX.
/// </summary>
public interface ITempHpLossModifier
{
    /// <summary>
    ///     Return the (possibly modified) damage amount that will be offered to Temporary HP.
    ///     Return 0 to make this hit skip Temporary HP entirely.
    /// </summary>
    /// <param name="damageToTempHp">Damage amount about to be absorbed (post-Block, post-Osty-redirect).</param>
    /// <param name="target">The creature whose Temporary HP is absorbing.</param>
    /// <param name="props">Properties of the damage.</param>
    /// <param name="dealer">Creature who dealt the damage, if any.</param>
    /// <param name="cardSource">Card that dealt the damage, if any.</param>
    decimal ModifyTempHpLoss(decimal damageToTempHp, Creature target, ValueProp props,
        Creature? dealer, CardModel? cardSource) => damageToTempHp;
}
