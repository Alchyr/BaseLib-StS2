using BaseLib.Extensions;
using BaseLib.Patches.Features;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Factories;
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
    [Obsolete("Use an overload that receives a CardPlay parameter. This is required on the beta branch.")]
    public static AttackCommand CardAttack(CardModel card, Creature? target, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        if (card.DynamicVars.ContainsKey(CalculatedDamageVar.defaultName))
        {
            return CardAttack(card, target, card.DynamicVars.CalculatedDamage, card.DynamicVars.CalculatedDamage.Props, hitCount, vfx, sfx, tmpSfx);
        }

        if (card.DynamicVars.ContainsKey(DamageVar.defaultName))
        {
            return CardAttack(card, target, card.DynamicVars.Damage.BaseValue, card.DynamicVars.Damage.Props, hitCount, vfx, sfx, tmpSfx);
        }
        throw new Exception($"Card {card.Title} does not have a damage variable supported by CommonActions.CardAttack");
    }
    
    /// <summary>
    /// Performs an attack using a card's DamageVar or CalculatedDamageVar on the card play's target.
    /// </summary>
    public static AttackCommand CardAttack(CardModel card, CardPlay? play, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        if (card.DynamicVars.ContainsKey(CalculatedDamageVar.defaultName))
        {
            return CardAttack(card, play, play?.Target, card.DynamicVars.CalculatedDamage, card.DynamicVars.CalculatedDamage.Props, hitCount, vfx, sfx, tmpSfx);
        }

        if (card.DynamicVars.ContainsKey(DamageVar.defaultName))
        {
            return CardAttack(card, play, play?.Target, card.DynamicVars.Damage.BaseValue, card.DynamicVars.Damage.Props, hitCount, vfx, sfx, tmpSfx);
        }
        throw new Exception($"Card {card.Title} does not have a damage variable supported by CommonActions.CardAttack");
    }

    /// <summary>
    /// Performs an attacking using a specified amount of damage on a target.
    /// </summary>
    [Obsolete("Use the variant that has a CardPlay as the second parameter instead. This will be required for the beta branch." +
              "If no CardPlay is available, use null.")]
    public static AttackCommand CardAttack(CardModel card, Creature? target, decimal damage, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        return CardAttack(card, target, damage, ValueProp.Move, hitCount, vfx, sfx, tmpSfx);
    }

    /// <summary>
    /// Performs an attacking using a specified amount of damage on a target.
    /// Note random targeting will default to allowing the same target multiple times.
    /// </summary>
    [Obsolete("Use the variant that has a CardPlay as the second parameter instead. This will be required for the beta branch." +
              "If no CardPlay is available, use null.")]
    public static AttackCommand CardAttack(CardModel card, Creature? target, decimal damage, ValueProp valueProp,
        int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        return CardAttack(card, null, target, damage, valueProp, hitCount, vfx, sfx, tmpSfx);
    }

    /// <summary>
    /// Performs an attacking using a specified amount of damage on a target.
    /// Note random targeting will default to allowing the same target multiple times.
    /// </summary>
    public static AttackCommand CardAttack(CardModel card, CardPlay? cardPlay, Creature? target, decimal damage, ValueProp valueProp, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        AttackCommand cmd = DamageCmd.Attack(damage).WithHitCount(hitCount).WithValueProp(valueProp).FromCardCompatibility(card, cardPlay);

        if (CustomTargetType.IsCustomSingleTargetType(card.TargetType))
        {
            if (target == null) return cmd;
            cmd.Targeting(target);
        }
        else if (CustomTargetType.IsCustomMultiTargetType(card.TargetType))
        {
            var state = card.CombatState;
            if (state == null) return cmd;
            var targets = state.Creatures.Where(c => CustomTargetType.CanMultiTarget(card.TargetType, c, card.Owner));
            cmd.TargetingFiltered(targets);
        }
        else
        {
            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    if (target == null) return cmd;
                    cmd.Targeting(target);
                    break;
                case TargetType.AllEnemies:
                    var combatStateA = card.CombatState;
                    if (combatStateA == null) return cmd;
                    cmd.TargetingAllOpponents(combatStateA);
                    break;
                case TargetType.RandomEnemy:
                    var combatStateB = card.CombatState;
                    if (combatStateB == null) return cmd;
                    cmd.TargetingAllOpponents(combatStateB);
                    break;
                default:
                    throw new Exception($"Unsupported AttackCommand target type {card.TargetType} for card {card.Title}");
            }

        }
      
     
        
        if (vfx != null || sfx != null || tmpSfx != null) cmd.WithHitFx(vfx: vfx, sfx: sfx, tmpSfx: tmpSfx);

        return cmd;
    }

    public static AttackCommand CardAttack(CardModel card, Creature? target, CalculatedDamageVar calculatedDamage, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        return CardAttack(card, target, calculatedDamage, ValueProp.Move, hitCount, vfx, sfx, tmpSfx);
    }

    /// <summary>
    /// Performs an attack using a CalculatedDamageVar on a target.
    /// </summary>
    [Obsolete("Use the variant that has a CardPlay as the second parameter instead. This will be required for the beta branch." +
              "If no CardPlay is available, use null.")]
    public static AttackCommand CardAttack(CardModel card, Creature? target, CalculatedDamageVar calculatedDamage,
        ValueProp valueProp, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        return CardAttack(card, null, target, calculatedDamage, valueProp, hitCount, vfx, sfx, tmpSfx);
    }

    /// <summary>
    /// Performs an attack using a CalculatedDamageVar on a target.
    /// Note random targeting will default to allowing the same target multiple times.
    /// </summary>
    public static AttackCommand CardAttack(CardModel card, CardPlay? cardPlay, Creature? target, 
        CalculatedDamageVar calculatedDamage, ValueProp valueProp, int hitCount = 1, 
        string? vfx = null, string? sfx = null, string? tmpSfx = null)
    {
        AttackCommand cmd = DamageCmd.Attack(calculatedDamage).WithHitCount(hitCount).WithValueProp(valueProp).FromCardCompatibility(card, cardPlay);
        
        if (CustomTargetType.IsCustomSingleTargetType(card.TargetType))
        {
            if (target == null) return cmd;
            cmd.Targeting(target);
        }
        else if (CustomTargetType.IsCustomMultiTargetType(card.TargetType))
        {
            var state = card.CombatState;
            if (state == null) return cmd;
            var targets = state.Creatures.Where(c =>  CustomTargetType.CanMultiTarget(card.TargetType, c, card.Owner));
            cmd.TargetingFiltered(targets);
        }
        else
        {
            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    if (target == null) return cmd;
                    cmd.Targeting(target);
                    break;
                case TargetType.AllEnemies:
                    var combatStateA = card.CombatState;
                    if (combatStateA == null) return cmd;
                    cmd.TargetingAllOpponents(combatStateA);
                    break;
                case TargetType.RandomEnemy:
                    var combatStateB = card.CombatState;
                    if (combatStateB == null) return cmd;
                    cmd.TargetingRandomOpponents(combatStateB);
                    break;
                default:
                    throw new Exception(
                        $"Unsupported AttackCommand target type {card.TargetType} for card {card.Title}");
            }
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
        if (card.DynamicVars.TryGetValue(BlockVar.defaultName, out var blockVar))
        {
            return await CardBlock(card, blockVar, play);
        }
        else if (card.DynamicVars.TryGetValue(CalculatedBlockVar.defaultName, out blockVar))
        {
            return await CardBlock(card, blockVar, play);
        }

        throw new InvalidOperationException(
            $"No valid block var found in card {card.GetType()} for CommonActions.CardBlock; define a block var or " +
            "pass a variable in manually.");
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
    public static async Task<decimal> CardBlock(CardModel card, DynamicVar var, CardPlay? play, bool fast = false)
    {
        if (var is CalculatedBlockVar calculated)
        {
            return await CreatureCmd.GainBlock(card.Owner.Creature, calculated.Calculate(card.Owner.Creature), calculated.Props, play, fast);
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
        
    /// FOR COMPATIBILITY - WILL BE REMOVED
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<T?> Apply<T>(Creature target, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, new ThrowingPlayerChoiceContext(), target, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent)!;
    }
    /// <summary>
    /// Applies the power specified as the generic parameter to multiple targets using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<IReadOnlyList<T>> Apply<T>(IEnumerable<Creature> targets, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.ApplyMulti.InvokeGeneric<Task<IReadOnlyList<T>>, T>
            (null, new ThrowingPlayerChoiceContext(), targets, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<T?> Apply<T>(Creature target, CardModel card, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, new ThrowingPlayerChoiceContext(), target, card.DynamicVars.Power<T>().BaseValue, card.Owner.Creature, card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target.
    /// </summary>
    /// <returns></returns>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<T?> Apply<T>(Creature target, CardModel? card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, new ThrowingPlayerChoiceContext(), target, amount, card?.Owner.Creature, card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner using a PowerVar defined for that power.
    /// </summary>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<T?> ApplySelf<T>(CardModel card, bool silent = false) where T : PowerModel
    {
        return await ApplySelf<T>(new ThrowingPlayerChoiceContext(), card, card.DynamicVars.Power<T>().BaseValue, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner.
    /// </summary>
    [Obsolete("Will be removed. Change to calling the overload that receives a PlayerChoiceContext if you are on the beta branch.")]
    public static async Task<T?> ApplySelf<T>(CardModel card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, new ThrowingPlayerChoiceContext(), card.Owner.Creature, amount, card.Owner.Creature, card, silent)!;
    }
    /* END OF COMPATIBILITY SECTION */
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(PlayerChoiceContext context, Creature target, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, context, target, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent)!;
    }
    /// <summary>
    /// Applies the power specified as the generic parameter to multiple targets using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<IReadOnlyList<T>> Apply<T>(PlayerChoiceContext context, IEnumerable<Creature> targets, DynamicVarSource dynVarSource, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.ApplyMulti.InvokeGeneric<Task<IReadOnlyList<T>>, T>
            (null, context, targets, dynVarSource.DynamicVars.Power<T>().BaseValue, dynVarSource.Owner, dynVarSource.Card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target using a PowerVar defined for that power.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(PlayerChoiceContext context, Creature target, CardModel card, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, context, target, card.DynamicVars.Power<T>().BaseValue, card.Owner.Creature, card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the target.
    /// </summary>
    /// <returns></returns>
    public static async Task<T?> Apply<T>(PlayerChoiceContext context, Creature target, CardModel? card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, context, target, amount, card?.Owner.Creature, card, silent)!;
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner using a PowerVar defined for that power.
    /// </summary>
    public static async Task<T?> ApplySelf<T>(PlayerChoiceContext context, CardModel card, bool silent = false) where T : PowerModel
    {
        return await ApplySelf<T>(context, card, card.DynamicVars.Power<T>().BaseValue, silent);
    }
    
    /// <summary>
    /// Applies the power specified as the generic parameter to the card's owner.
    /// </summary>
    public static async Task<T?> ApplySelf<T>(PlayerChoiceContext context, CardModel card, decimal amount, bool silent = false) where T : PowerModel
    {
        return await BetaMainCompatibility.PowerCmd_.Apply.InvokeGeneric<Task<T?>, T>
            (null, context, card.Owner.Creature, amount, card.Owner.Creature, card, silent)!;
    }
    
    /// <summary>
    /// Opens a card selection screen with specific CardSelectorPrefs and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, CardSelectorPrefs prefs, PlayerChoiceContext context, PileType pileType)
    {
        return await SelectCards(card, prefs, context, pileType, null);
    }
    
    /// <summary>
    /// Opens a card selection screen with specific CardSelectorPrefs and a filter, returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, CardSelectorPrefs prefs, PlayerChoiceContext context, PileType pileType, Func<CardModel, bool>? filter)
    {
        var pile = pileType.GetPile(card.Owner);
        var cards = pile.Cards;
        if (pile.Type == PileType.Draw)
        {
            cards = cards
                .OrderBy(c => c.Rarity)
                .ThenBy(c => c.Id)
                .ToList();
        }
        
        if (pile.Type == PileType.Hand)
        {
            return await CardSelectCmd.FromHand(context, card.Owner, prefs, filter, card);
        }

        if (filter != null)
        {
            cards = cards.Where(filter).ToList();
        }
        
        return await CardSelectCmd.FromSimpleGrid(context, cards, card.Owner, prefs);
    }
    
    /// <summary>
    /// Opens a card selection screen where a specific number of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int count = 1)
    {
        return await SelectCards(card, selectionPrompt, context, pileType, null, count);
    }
    
    /// <summary>
    /// Opens a card selection screen with a filter where a specific number of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, Func<CardModel, bool>? filter, int count = 1)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, count);
        return await SelectCards(card, prefs, context, pileType, filter);
    }
    
    /// <summary>
    /// Opens a card selection screen where a range of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int minCount, int maxCount)
    {
        return await SelectCards(card, selectionPrompt, context, pileType, null, minCount, maxCount);
    }
    
    /// <summary>
    /// Opens a card selection screen with a filter where a range of cards must be selected and returns the selection result.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, Func<CardModel, bool>? filter, int minCount, int maxCount)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, minCount, maxCount);
        return await SelectCards(card, prefs, context, pileType, filter);
    }
    
    /// <summary>
    /// Opens a card selection screen selecting a single card and returns that single card (or null if no card could be selected).
    /// </summary>
    /// <returns></returns>
    public static async Task<CardModel?> SelectSingleCard(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType)
    {
        return await SelectSingleCard(card, selectionPrompt, context, pileType, null);
    }

    /// <summary>
    /// Opens a card selection screen with a filter selecting a single card and returns that single card (or null if no card could be selected).
    /// </summary>
    /// <returns></returns>
    public static async Task<CardModel?> SelectSingleCard(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, Func<CardModel, bool>? filter)
    {
        CardSelectorPrefs prefs = new(selectionPrompt, 1);
        return (await SelectCards(card, prefs, context, pileType, filter)).FirstOrDefault();
    }
    
    
    /// <summary>
    /// Applies a power of type <typeparamref name="T"/> to the appropriate creature(s) based on
    /// the card's <see cref="TargetType"/>, handling both vanilla and custom target types.
    /// </summary>
    /// <typeparam name="T">The <see cref="PowerModel"/> type to apply.</typeparam>
    /// <param name="ctx">The player choice context for the current action.</param>
    /// <param name="card">The card being played, used to determine targeting behaviour.</param>
    /// <param name="cardPlay">
    /// The card play instance carrying the selected target for single-target cards.
    /// May be <see langword="null"/> for untargeted cards.
    /// </param>
    /// <param name="silent">
    /// If <see langword="true"/>, suppresses visual effects when the power is applied.
    /// </param>
    /// <returns>
    /// A list of all applied power instances, or an empty list if no valid targets were found.
    /// </returns>
    public static async Task<IReadOnlyList<T>> Apply<T>(PlayerChoiceContext ctx, CardModel card, CardPlay? cardPlay, bool silent = false) where T : PowerModel
    {
        if (cardPlay?.Target != null)
        {
            return await Apply<T>(ctx, cardPlay.Target, card, silent) is { } result ? [result] : [];
        }

        return await ApplyToCreatures<T>(card, ctx, card.GetTargets(), silent);
    }
    
    private static async Task<IReadOnlyList<T>> ApplyToCreatures<T>(CardModel card, PlayerChoiceContext ctx, params Creature[] targets) where T : PowerModel
    {
        return await Apply<T>(ctx, targets, card);
    }
    private static async Task<IReadOnlyList<T>> ApplyToCreatures<T>(CardModel card, PlayerChoiceContext ctx, IEnumerable<Creature> targets, bool silent = false) where T : PowerModel
    {
        return await Apply<T>(ctx, targets, card, silent);
    }

    /// <summary>
    /// Generate cards based on the character and unlock status.
    /// </summary>
    public static IEnumerable<CardModel> GenerateCards(CardModel card, int count, Func<CardModel, bool>? filter = null)
    {
        var owner = card.Owner;
        var cards = owner.Character.CardPool.GetUnlockedCards(owner.UnlockState, owner.RunState.CardMultiplayerConstraint);
        if (filter != null)
        {
            cards = cards.Where(filter).ToList();
        }
        return CardFactory.GetDistinctForCombat(owner, cards, count, owner.RunState.Rng.CombatCardGeneration);
    }

    /// <summary>
    /// Generate a card based on the character and unlock status.
    /// </summary>
    public static CardModel? GenerateSingleCard(CardModel card, Func<CardModel, bool>? filter = null)
    {
        return GenerateCards(card, 1, filter).FirstOrDefault();
    }
}
