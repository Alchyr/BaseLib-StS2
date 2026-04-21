using BaseLib.Extensions;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Utils;

/// <summary>
/// Contains commonly used actions in cards as shortcuts that handle the most common ways these commands are used.
/// </summary>
public static class CommonActions
{
    /// <summary>
    /// Performs an attack using a card's DamageVar or CalculatedDamageVar on the card play's target.
    /// </summary>
    /// <param name="card"></param>
    /// <param name="play"></param>
    /// <param name="hitCount"></param>
    /// <param name="vfx"></param>
    /// <param name="sfx"></param>
    /// <param name="tmpSfx"></param>
    /// <returns></returns>
    public static AttackCommand CardAttack(CardModel card, CardPlay play, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        return CardAttack(card, play.Target, hitCount, vfx, sfx, tmpSfx);
    }
    /// <summary>
    /// Performs an attack using a card's DamageVar or CalculatedDamageVar on a specified target.
    /// </summary>
    /// <param name="card"></param>
    /// <param name="target"></param>
    /// <param name="hitCount"></param>
    /// <param name="vfx"></param>
    /// <param name="sfx"></param>
    /// <param name="tmpSfx"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static AttackCommand CardAttack(CardModel card, Creature? target, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        if (card.DynamicVars.ContainsKey(CalculatedDamageVar.defaultName))
        {
            return CardAttack(card, target, card.DynamicVars.CalculatedDamage, hitCount, vfx, sfx, tmpSfx);
        }
        else if (card.DynamicVars.ContainsKey(DamageVar.defaultName))
        {
            return CardAttack(card, target, card.DynamicVars.Damage.BaseValue, hitCount, vfx, sfx, tmpSfx);
        }
        throw new Exception($"Card {card.Title} does not have a damage variable supported by CommonActions.CardAttack");
    }
    /// <summary>
    /// Performs an attacking using a specified amount of damage on a target.
    /// </summary>
    /// <param name="card"></param>
    /// <param name="target"></param>
    /// <param name="damage"></param>
    /// <param name="hitCount"></param>
    /// <param name="vfx"></param>
    /// <param name="sfx"></param>
    /// <param name="tmpSfx"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static AttackCommand CardAttack(CardModel card, Creature? target, decimal damage, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        AttackCommand cmd = DamageCmd.Attack(damage).WithHitCount(hitCount).FromCard(card);
        var combatState = card.CombatState;
        
        switch (card.TargetType)
        {
            case TargetType.AnyEnemy:
                if (target == null) return cmd;
                cmd.Targeting(target);
                break;
            case TargetType.AllEnemies:
                if (combatState == null) return cmd;
                cmd.TargetingAllOpponents(combatState);
                break;
            case TargetType.RandomEnemy:
                if (combatState == null) return cmd;
                cmd.TargetingRandomOpponents(combatState);
                break;
            default:
                throw new Exception($"Unsupported AttackCommand target type {card.TargetType} for card {card.Title}");
        }

        if (vfx != null || sfx != null || tmpSfx != null) cmd.WithHitFx(vfx: vfx, sfx: sfx, tmpSfx: tmpSfx);

        return cmd;
    }
    /// <summary>
    /// Performs an attacking using aCalculatedDamageVar on a target.
    /// </summary>
    /// <param name="card"></param>
    /// <param name="target"></param>
    /// <param name="calculatedDamage"></param>
    /// <param name="hitCount"></param>
    /// <param name="vfx"></param>
    /// <param name="sfx"></param>
    /// <param name="tmpSfx"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static AttackCommand CardAttack(CardModel card, Creature? target, CalculatedDamageVar calculatedDamage, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        AttackCommand cmd = DamageCmd.Attack(calculatedDamage).WithHitCount(hitCount).FromCard(card);
        var combatState = card.CombatState;
        
        switch (card.TargetType)
        {
            case TargetType.AnyEnemy:
                if (target == null) return cmd;
                cmd.Targeting(target);
                break;
            case TargetType.AllEnemies:
                if (combatState == null) return cmd;
                cmd.TargetingAllOpponents(combatState);
                break;
            case TargetType.RandomEnemy:
                if (combatState == null) return cmd;
                cmd.TargetingRandomOpponents(combatState);
                break;
            default:
                throw new Exception($"Unsupported AttackCommand target type {card.TargetType} for card {card.Title}");
        }

        if (vfx != null || sfx != null || tmpSfx != null) cmd.WithHitFx(vfx: vfx, sfx: sfx, tmpSfx: tmpSfx);

        return cmd;
    }

    /// <summary>
    /// Gains Block based on the card's BlockVar<seealso cref="BlockVar"/>.
    /// </summary>
    /// <returns></returns>
    public static async Task<decimal> CardBlock(CardModel card, CardPlay? play)
    {
        return await CardBlock(card, card.DynamicVars.Block, play);
    }

    /// <summary>
    /// Gains Block based on the given BlockVar<seealso cref="BlockVar"/>.
    /// </summary>
    /// <returns></returns>
    public static async Task<decimal> CardBlock(CardModel card, BlockVar blockVar, CardPlay? play)
    {
        return await CreatureCmd.GainBlock(card.Owner.Creature, blockVar, play);
    }

    /// <summary>
    /// Gains Block based on the given DynamicVar (supports CalculatedBlockVar)
    /// </summary>
    /// <param name="card"></param>
    /// <param name="var"></param>
    /// <param name="play"></param>
    /// <param name="fast"></param>
    /// <returns></returns>
    public static async Task<decimal> CardBlock(CardModel card, DynamicVar var, CardPlay? play, bool fast = false)
    {
        if (var is CalculatedBlockVar calculated)
        {
            return await CreatureCmd.GainBlock(card.Owner.Creature, calculated.Calculate(play.Target), calculated.Props, play, fast);
        }
        return await CreatureCmd.GainBlock(card.Owner.Creature, var.BaseValue, (var as BlockVar)?.Props ?? ValueProp.Move, play, fast);
    }

    /// <summary>
    /// Draws cards based on the card's CardsVar<seealso cref="CardsVar"/>.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> Draw(CardModel card, PlayerChoiceContext context)
    {
        return await CardPileCmd.Draw(context, card.DynamicVars.Cards.BaseValue, card.Owner);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(Creature target, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(target, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent);
    }
    /// <summary>
    /// Applies the power specified as the generic parameter to multiple targets using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<IReadOnlyList<T>> Apply<T>(IEnumerable<Creature> targets, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(targets, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(Creature target, CardModel card, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(target, card.DynamicVars.Power<T>().BaseValue, card.Owner.Creature, card, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(Creature target, CardModel? card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(target, amount, card?.Owner.Creature, card, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner using a PowerVar defined for that power.
    /// </summary>
    public static async Task<T?> ApplySelf<T>(CardModel card, bool silent = false) where T : PowerModel
    {
        return await ApplySelf<T>(card, card.DynamicVars.Power<T>().BaseValue, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner.
    /// </summary>
    public static async Task<T?> ApplySelf<T>(CardModel card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await PowerCmd.Apply<T>(card.Owner.Creature, amount, card.Owner.Creature, card, silent);
    }

    /// <summary>
    /// Opens a card selection screen where a specific number of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int count = 1)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, count);
        var pile = pileType.GetPile(card.Owner);
        var cards = pile.Cards;
        if (pile.Type == PileType.Draw)
        {
            cards = cards
                .OrderBy(c => c.Rarity)
                .ThenBy(c => c.Id)
                .ToList();
        }
        return await CardSelectCmd.FromSimpleGrid(context, cards, card.Owner, prefs);
    }
    
    /// <summary>
    /// Opens a card selection screen where a range of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int minCount, int maxCount)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, minCount, maxCount);
        var pile = pileType.GetPile(card.Owner);
        var cards = pile.Cards;
        if (pile.Type == PileType.Draw)
        {
            cards = cards
                .OrderBy(c => c.Rarity)
                .ThenBy(c => c.Id)
                .ToList();
        }
        return await CardSelectCmd.FromSimpleGrid(context, cards, card.Owner, prefs);
    }

    /// <summary>
    /// Opens a card selection screen selecting a single card and returns that single card (or null if no card could be selected).
    /// </summary>
    /// <returns></returns>
    public static async Task<CardModel?> SelectSingleCard(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, 1);
        var pile = pileType.GetPile(card.Owner);
        var cards = pile.Cards;
        if (pile.Type == PileType.Draw)
        {
            cards = cards
                .OrderBy(c => c.Rarity)
                .ThenBy(c => c.Id)
                .ToList();
        }
        return (await CardSelectCmd.FromSimpleGrid(context, cards, card.Owner, prefs)).FirstOrDefault();
    }
}
