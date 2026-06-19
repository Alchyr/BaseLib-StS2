using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using BaseLib.Patches.Features;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class CardExtensions
{
    /// <summary>
    /// Should not be called arbitrarily, only when actually performing actions.
    /// Random targeting will advance RunState.Rng.CombatTargets.
    /// Does not work for single-targeting types that require choosing a target.
    /// </summary>
    /// <returns>All targets of the card based on its TargetType, or an empty list.</returns>
    public static List<Creature> GetTargets(this CardModel card)
    {
        switch (card.TargetType)
        {
            case TargetType.AllAllies:
                var state = BetaMainCompatibility.CardModel_.WrappedCombatState(card);
                return state?.PlayerCreatures.Where(c => c is { IsAlive: true }).ToList() ?? [];
            case TargetType.AllEnemies:
                return BetaMainCompatibility.CardModel_.WrappedCombatState(card)?.HittableEnemies.ToList() ?? [];
            case TargetType.RandomEnemy:
                var allTargets = BetaMainCompatibility.CardModel_.WrappedCombatState(card)?.HittableEnemies;
                if (allTargets == null || allTargets.Count == 0) return [];
                var target = card.Owner.RunState.Rng.CombatTargets.NextItem(allTargets);
                if (target == null) return [];
                return [target];
            case TargetType.None:
                return [];
            case TargetType.Self:
                return [card.Owner.Creature];
            default:
                if (CustomTargetType.IsCustomMultiTargetType(card.TargetType))
                {
                    state = BetaMainCompatibility.CardModel_.WrappedCombatState(card);
                    return state?.Creatures.Where(c => CustomTargetType.CanMultiTarget(card.TargetType, c, card.Owner)).ToList() ?? [];
                }
                
                var targetTypeName = CustomEnums.EnumName<TargetType>((int) card.TargetType) ?? 
                                (card.TargetType <= TargetType.Osty ? card.TargetType.ToString() : ((int)card.TargetType).ToString());
                BaseLibMain.Logger.Error($"Target type {targetTypeName} is not supported by GetTargets, either because it requires" +
                    $"single targeting or is an unknown type of targeting.");
                return [];
        }
    }

    /// <summary>
    /// Convenience shortcut to <see cref="CardModifier.AddModifier"/>.
    /// Adds a modifier to a card.
    /// </summary>
    public static void AddModifier(this CardModel card, CardModifier modifier)
    {
        CardModifier.AddModifier(card, modifier);
    }

    /// <summary>
    /// Get all <see cref="CardModifier"/>s currently attached to a card.
    /// </summary>
    public static ReadOnlyCollection<CardModifier> GetModifiers(this CardModel card)
    {
        return CardModifier.Modifiers(card);
    }
    /// <summary>
    /// Get a specific type of <see cref="CardModifier"/> attached to a card, if it exists.
    /// </summary>
    public static T? GetModifier<T>(this CardModel card) where T : CardModifier
    {
        return card.GetModifiers().OfType<T>().FirstOrDefault();
    }
    /// <summary>
    /// Get a specific type of <see cref="CardModifier"/> attached to a card, if it exists.
    /// </summary>
    public static bool TryGetModifier<T>(this CardModel card, [NotNullWhen(true)] out T? modifier) where T : CardModifier
    {
        modifier = card.GetModifier<T>();
        return modifier != null;
    }
    /// <summary>
    /// Get a specific <see cref="CardModifier"/> attached to a card by ID, if it exists.
    /// </summary>
    public static CardModifier? GetModifier(this CardModel card, ModelId modifierId)
    {
        return card.GetModifiers().FirstOrDefault(modifier => modifier.Id.Equals(modifierId));
    }
    /// <summary>
    /// Get a specific <see cref="CardModifier"/> attached to a card by ID, if it exists.
    /// </summary>
    public static bool TryGetModifier(this CardModel card, ModelId modifierId, [NotNullWhen(true)] out CardModifier? modifier)
    {
        modifier = card.GetModifier(modifierId);
        return modifier != null;
    }
}