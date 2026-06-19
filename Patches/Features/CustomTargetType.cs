using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Patches.Content;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Features;

/// <summary>
/// Provides extended <see cref="TargetType"/> definitions and a registry of custom
/// single-target types, each paired with a predicate that determines which creatures
/// are valid targets.
/// </summary>
public static class CustomTargetType
{
  
    /// <summary>Targets all living creatures.</summary>
    [CustomEnum] public static TargetType Everyone;

    /// <summary>Targets any single living creature.</summary>
    [CustomEnum] public static TargetType Anyone;

    /// <summary>Targets all enemies currently intending to attack.</summary>
    [CustomEnum] public static TargetType AllAttackingEnemies;

    /// <summary>Targets any single enemy currently intending to attack.</summary>
    [CustomEnum] public static TargetType AnyAttackingEnemy;

    /// <summary>Targets all enemies with block.</summary>
    [CustomEnum] public static TargetType AllBlockingEnemies;

    /// <summary>Targets any single enemy with block.</summary>
    [CustomEnum] public static TargetType AnyBlockingEnemy;

    /// <summary>Targets all enemies with no block.</summary>
    [CustomEnum] public static TargetType AllNonBlockingEnemies;

    /// <summary>Targets any single enemy with no block.</summary>
    [CustomEnum] public static TargetType AnyNonBlockingEnemy;

    /// <summary>Targets all enemies tied for the highest current HP.</summary>
    [CustomEnum] public static TargetType AllHighestHpEnemies;

    /// <summary>Targets all enemies tied for the lowest current HP.</summary>
    [CustomEnum] public static TargetType AllLowestHpEnemies;

    /// <summary>Targets any single enemy at full HP.</summary>
    [CustomEnum] public static TargetType AnyFullLifeEnemy;

    /// <summary>Targets all enemies at full HP.</summary>
    [CustomEnum] public static TargetType AllFullLifeEnemies;
    
    /// <summary>Targets any of YOUR pets or yourself.</summary>
    [CustomEnum] public static TargetType PetOrSelf;
    
    /// <summary>Targets any of YOUR pets.</summary>
    [CustomEnum] public static TargetType Pet;
    
    internal static readonly Dictionary<TargetType, Func<Creature, Player, bool>> SingleTargeting = new();
    internal static readonly Dictionary<TargetType, Func<Creature, Player, bool>> MultiTargeting = new();



    /// <summary>
    /// Evaluates the registered filter predicate for <paramref name="targetType"/> against
    /// <paramref name="creature"/>, returning <see langword="true"/> if the creature should
    /// receive a targeting reticle or be included in the attack.
    /// </summary>
    /// <param name="targetType">A <see cref="TargetType"/> registered via <see cref="RegisterMultiTargetType"/>.</param>
    /// <param name="creature">The <see cref="Creature"/> to evaluate.</param>
    /// <param name="player">The <see cref="Player"/> doing the targeting.</param>
    public static bool CanMultiTarget(TargetType targetType, Creature creature, Player player)
    {
        MultiTargeting.TryGetValue(targetType, out var canTarget);
        return canTarget != null && canTarget(creature, player);
    }
    
    
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="targetType"/> has been registered
    /// as a custom single-target type via <see cref="RegisterSingleTargetType"/>.
    /// </summary>
    public static bool IsCustomSingleTargetType(TargetType targetType)
        => SingleTargeting.ContainsKey(targetType);
    
    
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="targetType"/> has been registered
    /// as a custom multi-target type via <see cref="RegisterMultiTargetType"/>.
    /// </summary>
    public static bool IsCustomMultiTargetType(TargetType targetType)
        => MultiTargeting.ContainsKey(targetType);

    /// <summary>
    /// Registers <paramref name="customType"/> as a custom single-target type whose valid
    /// targets are defined by <paramref name="canTarget"/>.
    /// </summary>
    /// <param name="customType">The custom <see cref="TargetType"/> to register.</param>
    /// <param name="canTarget">
    /// A predicate evaluated against each candidate <see cref="Creature"/> to determine
    /// whether it may be targeted.
    /// </param>
    public static void RegisterSingleTargetType(TargetType customType, Func<Creature, bool> canTarget)
    {
        RegisterSingleTargetType(customType, (creature, _) => canTarget(creature));
    }
    
    /// <summary>
    /// Registers <paramref name="customType"/> as a custom single-target type whose valid
    /// targets are defined by <paramref name="canTarget"/>.
    /// </summary>
    /// <param name="customType">The custom <see cref="TargetType"/> to register.</param>
    /// <param name="canTarget">
    /// A predicate evaluated against each candidate <see cref="Creature"/> to determine
    /// whether it may be targeted.
    /// </param>
    public static void RegisterSingleTargetType(TargetType customType, Func<Creature, Player, bool> canTarget)
    {
        BaseLibMain.Logger.VeryDebug($"Registered single target type {customType}");
        SingleTargeting.Add(customType, canTarget);
    }
    
    /// <summary>
    /// Registers <paramref name="customType"/> as a custom multi-target type. The card is
    /// played with a <see langword="null"/> target; <paramref name="showReticleFor"/>
    /// controls which creatures receive a targeting reticle during the preview.
    /// </summary>
    /// <param name="customType">The custom <see cref="TargetType"/> to register.</param>
    /// <param name="showReticleFor">
    /// A predicate evaluated against each <see cref="Creature"/> in the room to determine
    /// whether it should receive a targeting reticle. Pass <see langword="null"/> to show
    /// reticles on all living creatures.
    /// </param>
    public static void RegisterMultiTargetType(TargetType customType, Func<Creature, bool>? showReticleFor = null)
    {
        RegisterMultiTargetType(customType, showReticleFor != null ? (creature, _) => showReticleFor(creature) : null);
    }
    
    /// <summary>
    /// Registers <paramref name="customType"/> as a custom multi-target type. The card is
    /// played with a <see langword="null"/> target; <paramref name="showReticleFor"/>
    /// controls which creatures receive a targeting reticle during the preview.
    /// </summary>
    /// <param name="customType">The custom <see cref="TargetType"/> to register.</param>
    /// <param name="showReticleFor">
    /// A predicate evaluated against each <see cref="Creature"/> in the room to determine
    /// whether it should receive a targeting reticle. Pass <see langword="null"/> to show
    /// reticles on all living creatures.
    /// </param>
    public static void RegisterMultiTargetType(TargetType customType, Func<Creature, Player, bool>? showReticleFor = null)
    {
        BaseLibMain.Logger.VeryDebug($"Registered multi target type {customType}");
        MultiTargeting.Add(customType, showReticleFor ?? ((_, _) => true));
    }
}

/// <summary>
/// Registers all built-in custom target types after <see cref="ModelDb"/> has finished
/// assigning <see cref="CustomEnumAttribute"/> values, ensuring the enum integers are
/// stable before they are used as dictionary keys.
/// </summary>
[HarmonyPatch(typeof(ModelDb), "Init")]
internal static class ModelDbTargetTypeInitPatch
{
    [HarmonyPostfix]
    private static void RegisterTargetTypes()
    {
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.Anyone,
            (target) => target is { IsAlive: true, IsPet: false });
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.Everyone,  
            (target) => target is { IsAlive: true, IsPet: false }); 
        
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.AnyAttackingEnemy,
            (target) => target is { IsAlive: true, IsEnemy: true, Monster.IntendsToAttack: true });
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllAttackingEnemies,
            (target) => target is { IsAlive: true, IsEnemy: true, Monster.IntendsToAttack: true }); 
        
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.AnyBlockingEnemy,
            (target) => target is { IsAlive: true, IsEnemy: true, Block: > 0 });
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllBlockingEnemies,
            (target) => target is { IsAlive: true, IsEnemy: true, Block: > 0}); 
        
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.AnyNonBlockingEnemy,
            (target) => target is { IsAlive: true, IsEnemy: true,   Block: 0 });
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllNonBlockingEnemies,
            (target) => target is { IsAlive: true, IsEnemy: true, Block: 0}); 
        
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllLowestHpEnemies,
            (target) => target is { IsAlive: true,  IsEnemy: true } 
                           && target.CurrentHp == BetaMainCompatibility.Creature_.WrappedCombatState(target)!.Enemies
                               .Where(e => e.IsAlive)
                               .Min(e => e.CurrentHp));
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllHighestHpEnemies,
            (target) => target is { IsAlive: true, IsEnemy: true} &&
                           target.CurrentHp == BetaMainCompatibility.Creature_.WrappedCombatState(target)!.Enemies
                               .Where(e => e.IsAlive)
                               .Max(e => e.CurrentHp));
        
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.AnyFullLifeEnemy,
            (target) => target is { IsAlive: true, IsEnemy: true}  && target.CurrentHp == target.MaxHp);
        CustomTargetType.RegisterMultiTargetType(CustomTargetType.AllFullLifeEnemies,
            (target) => target is { IsAlive: true, IsEnemy: true} && target.CurrentHp == target.MaxHp);
        
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.PetOrSelf,
            (target, player) => (target.IsAlive && target.IsPet && target.PetOwner == player) || target == player.Creature);
        CustomTargetType.RegisterSingleTargetType(CustomTargetType.Pet,
            (target, player) => target.IsAlive && target.IsPet && target.PetOwner == player);

    }
}

/// <summary>
/// Triggers the multi-select visual state for any <see cref="TargetType"/> registered
/// in <see cref="CustomTargetType.MultiTargeting"/>, displaying targeting reticles over
/// every creature that satisfies the registered filter predicate.
/// </summary>
[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
internal class ShowMultiCreatureTargetingVisualsPatch
{
    public static void Postfix(NCardPlay __instance)
    {
        if (__instance.Card == null ||
            !CustomTargetType.MultiTargeting.TryGetValue(__instance.Card.TargetType, out var filter))
            return;

        __instance.CardNode?.UpdateVisuals(
            __instance.Card.Pile!.Type,
            CardPreviewMode.MultiCreatureTargeting
        );

        var room = NCombatRoom.Instance;
        if (room == null) return;

        foreach (var creatureNode in room.CreatureNodes)
            if (filter(creatureNode.Entity, __instance.Card.Owner))
                creatureNode.ShowMultiselectReticle();
    }
}

/// <summary>
/// Extends <see cref="AttackCommand"/> with support for a custom filtered target list,
/// used by custom multi-target types that do not map to vanilla opponent sets.
/// </summary>
[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.GetPossibleTargets))]
internal class AttackCommandGetPossibleTargetsPatch
{
    // ConditionalWeakTable so we don't leak AttackCommand instances
    internal static readonly ConditionalWeakTable<AttackCommand, StrongBox<IReadOnlyList<Creature>>>
        CustomTargets = new();

    /// <summary>
    /// If a custom target list has been registered for this <see cref="AttackCommand"/>
    /// via <see cref="AttackCommandExtensions.TargetingFiltered"/>, return it instead of running the vanilla logic.
    /// </summary>
    [HarmonyPrefix]
    static bool GetCustomTargets(AttackCommand __instance, ref IReadOnlyList<Creature> __result)
    {
        if (!CustomTargets.TryGetValue(__instance, out var box) ||  box.Value == null) return true;
        __result = box.Value;
        return false;
    }
}

/// <summary>
/// Extension methods that add filtered-targeting support to <see cref="AttackCommand"/>.
/// </summary>
public static class AttackCommandExtensions
{
    private static readonly FieldInfo AttackCommandCombatState = typeof(AttackCommand).DeclaredField("_combatState");
    
    /// <summary>
    /// Configures this <see cref="AttackCommand"/> to hit only the creatures in
    /// <paramref name="targets"/>, bypassing the vanilla opponent-set logic.
    /// </summary>
    /// <param name="cmd">The command to configure.</param>
    /// <param name="targets">The explicit set of creatures to attack.</param>
    public static AttackCommand TargetingFiltered(this AttackCommand cmd, IEnumerable<Creature> targets)
    {
        var list = targets.ToList();
        AttackCommandGetPossibleTargetsPatch.CustomTargets.Add(
            cmd, new StrongBox<IReadOnlyList<Creature>>(list));
        if (cmd.Attacker == null) return cmd;
        
        AttackCommandCombatState.SetValue(cmd, BetaMainCompatibility.Creature_.CombatState.Get(cmd.Attacker));
        return cmd;
    }
}

/// <summary>
/// Redirects mouse-based card targeting to <see>
///     <cref>NCardPlay.SingleCreatureTargeting</cref>
/// </see>
/// when the card's <see cref="TargetType"/> is registered in <see cref="CustomTargetType.SingleTargeting"/>.
/// </summary>
[HarmonyPatch(typeof(NMouseCardPlay), "TargetSelection")]
internal class TargetSelectionPatch
{
    [HarmonyPrefix]
    static bool CustomMouseTargetSelection(NMouseCardPlay __instance, TargetMode targetMode, ref Task __result)
    {
        if (__instance.Card == null || !CustomTargetType.SingleTargeting.ContainsKey(__instance.Card.TargetType)) return true;
        __result = AnyoneTargetSelectionAsync(__instance, targetMode, __instance.Card);
        return false;
    }

    private static async Task AnyoneTargetSelectionAsync(NMouseCardPlay __instance, TargetMode targetMode, CardModel type)
    {
        __instance.TryShowEvokingOrbs();
        __instance.CardNode?.CardHighlight.AnimFlash();
        await __instance.SingleCreatureTargeting(targetMode, type.TargetType);
    }
}

/// <summary>
/// Routes controller-based card play to <c>SingleCreatureTargeting</c> when the card's
/// <see cref="TargetType"/> is registered in <see cref="CustomTargetType.SingleTargeting"/>,
/// bypassing the vanilla switch that only handles <see cref="TargetType.AnyEnemy"/> and
/// <see cref="TargetType.AnyAlly"/>.
/// </summary>
[HarmonyPatch(typeof(NControllerCardPlay), nameof(NControllerCardPlay.Start))]
internal class ControllerStartPatch
{
    [HarmonyPrefix]
    static bool CustomControllerPlayStart(NControllerCardPlay __instance)
    {
        var card = __instance.Card;
        var cardNode = __instance.CardNode;
        if (card == null || cardNode == null || !CustomTargetType.SingleTargeting.ContainsKey(card.TargetType))
            return true;

        NDebugAudioManager.Instance?.Play("card_select.mp3");
        NHoverTipSet.Remove(__instance.Holder);

        if (!card.CanPlay(out var reason, out var preventer))
        {
            __instance.CannotPlayThisCardFtueCheck(card);
            __instance.CancelPlayCard();
            var line = reason.GetPlayerDialogueLine(preventer);
            if (line != null)
                NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(
                    NThoughtBubbleVfx.Create(line.GetFormattedText(), card.Owner.Creature, 1.0));
            return false;
        }

        __instance.TryShowEvokingOrbs();
        cardNode.CardHighlight.AnimFlash();
        __instance.CenterCard();
        TaskHelper.RunSafely(__instance.SingleCreatureTargeting(card.TargetType));
        return false;
    }
}

/// <summary>
/// Replaces the vanilla <c>SingleCreatureTargeting</c> flow for any <see cref="TargetType"/>
/// registered in <see cref="CustomTargetType.SingleTargeting"/>, supplying a candidate list built
/// directly from the registered filter predicate instead of the vanilla hard-coded switch.
/// </summary>
[HarmonyPatch(typeof(NControllerCardPlay), "SingleCreatureTargeting", new[] { typeof(TargetType) })]
internal class ControllerSingleCreatureTargetingPatch
{
    [HarmonyPrefix]
    static bool CustomControllerTargeting(NControllerCardPlay __instance, TargetType targetType, ref Task __result)
    {
        if (!CustomTargetType.SingleTargeting.TryGetValue(targetType, out var filter))
            return true;

        __result = FilteredControllerTargeting(__instance, targetType, filter);
        return false;
    }

    private static async Task FilteredControllerTargeting(
        NControllerCardPlay instance, TargetType targetType, Func<Creature, Player, bool> filter)
    {
        var card = instance.Card;
        var cardNode = instance.CardNode;
        if (card == null || BetaMainCompatibility.CardModel_.WrappedCombatState(card) == null || cardNode == null)
        {
            instance.CancelPlayCard();
            return;
        }

        var room = NCombatRoom.Instance;
        if (room == null)
        {
            instance.CancelPlayCard();
            return;
        }

        var nodes = room.CreatureNodes
            .Where(n => filter(n.Entity, card.Owner))
            .ToList();

        if (nodes.Count == 0)
        {
            instance.CancelPlayCard();
            return;
        }

        var targetManager = NTargetManager.Instance;
        var hoverCallable = Callable.From((NCreature c) => instance.OnCreatureHover(c));
        var unhoverCallable = Callable.From((NCreature c) => instance.OnCreatureUnhover(c));

        try
        {
            targetManager.Connect(NTargetManager.SignalName.CreatureHovered, hoverCallable);
            targetManager.Connect(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable);

            targetManager.StartTargeting(
                targetType,
                cardNode,
                TargetMode.Controller,
                () => !GodotObject.IsInstanceValid(instance) || !NControllerManager.Instance!.IsUsingController,
                null);

            room.RestrictControllerNavigation(nodes.Select(n => n.Hitbox));
            nodes.First().Hitbox.TryGrabFocus();

            var selected = (NCreature?)await targetManager.SelectionFinished();

            if (!GodotObject.IsInstanceValid(instance))
                return;

            if (selected != null)
                instance.TryPlayCard(selected.Entity);
            else
                instance.CancelPlayCard();
        }
        finally
        {
            if (targetManager.IsConnected(NTargetManager.SignalName.CreatureHovered, hoverCallable))
                targetManager.Disconnect(NTargetManager.SignalName.CreatureHovered, hoverCallable);
            if (targetManager.IsConnected(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable))
                targetManager.Disconnect(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable);
        }
    }
}

/// <summary>
/// Extends the game's target identification logic to recognise any <see cref="TargetType"/>
/// registered in <see cref="CustomTargetType.SingleTargeting"/> as a valid single-target type.
/// </summary>
[HarmonyPatch(typeof(ActionTargetExtensions), nameof(ActionTargetExtensions.IsSingleTarget))]
internal class IsSingleTargetPatch
{
    [HarmonyPostfix]
    static void CustomSingleTargets(TargetType targetType, ref bool __result)
    {
        if (__result) return;
        if (CustomTargetType.SingleTargeting.ContainsKey(targetType)) __result = true;
    }
}

/// <summary>
/// Overrides the validation logic for individual creature targeting, delegating
/// to the registered predicate when the active target type is a custom one.
/// </summary>
[HarmonyPatch(typeof(NTargetManager), nameof(NTargetManager.AllowedToTargetCreature))]
internal class AllowedToTargetCreaturePatch
{
    [HarmonyPrefix]
    static bool CustomTargetingAllowed(NTargetManager __instance, Creature creature, ref bool __result)
    {
        CustomTargetType.SingleTargeting.TryGetValue(__instance._validTargetsType, out var func);
        if (func == null) return true;
        
        var localPlayer = LocalContext.GetMe(RunManager.Instance.State);
        if (localPlayer == null)
        {
            return true;
        }
        __result = func.Invoke(creature, localPlayer);
        return false;
    }
}

/// <summary>
/// Re-implements the card-playing execution loop for custom target types registered in
/// <see cref="CustomTargetType.SingleTargeting"/>, ensuring the selected creature is correctly
/// passed through to the card's play action.
/// </summary>
[HarmonyPatch(typeof(NCardPlay), nameof(NCardPlay.TryPlayCard))]
internal class TryPlayCardPatch
{
    [HarmonyPrefix]
    static bool StopPlayIfCustomTargetInvalid(NCardPlay __instance, Creature? target)
    {
        var card = __instance.Card;
        if (card == null || !CustomTargetType.SingleTargeting.ContainsKey(card.TargetType)) return true;
        if (target == null || __instance.Holder.CardModel == null)
        {
            __instance.CancelPlayCard();
            return false;
        }
        if (!__instance.Holder.CardModel.CanPlayTargeting(target))
        {
            __instance.CannotPlayThisCardFtueCheck(__instance.Holder.CardModel);
            __instance.CancelPlayCard();
            return false;
        }
        __instance._isTryingToPlayCard = true;
        var success = card.TryManualPlay(target);
        __instance._isTryingToPlayCard = false;

        if (success)
        {
            __instance.AutoDisableCannotPlayCardFtueCheck();
            if (__instance.Holder.IsInsideTree())
            {
                var size = __instance.GetViewport().GetVisibleRect().Size;
                __instance.Holder.SetTargetPosition(new Vector2(size.X / 2f, size.Y - __instance.Holder.Size.Y));
            }
            AccessTools.Method(typeof(NCardPlay), "Cleanup").Invoke(__instance, [true]);
            var instance = NCombatRoom.Instance;
            if (instance == null)
                return false;
            instance.Ui.Hand.TryGrabFocus();
        }
        else
        {
            __instance.CancelPlayCard();
        }

        return false;
    }
}

/// <summary>
/// Patches the targeting selection logic to delegate to the registered predicate when
/// the card's <see cref="TargetType"/> is a custom one, replacing the vanilla
/// faction-based check.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.CanPlayTargeting))]
internal class CanPlayTargetingPatch
{
    [HarmonyPrefix]
    static bool CustomTargetValidityChecks(CardModel __instance, Creature? target, ref bool __result)
    {
        if (target == null) return true;
        CustomTargetType.SingleTargeting.TryGetValue(__instance.TargetType, out var func);
        if (func == null) return true;
        __result = func.Invoke(target, __instance.Owner);
        return false;
    }
}

/// <summary>
/// Overrides the card model's internal validation to delegate to the registered predicate
/// when the card's <see cref="TargetType"/> is a custom one, ensuring only creatures that
/// satisfy the filter are recognised as legitimate targets.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
internal class IsValidTargetPatch
{
    [HarmonyPrefix]
    static bool CustomValidTargets(CardModel __instance, Creature? target, ref bool __result)
    {
        if (target == null) return true;
        CustomTargetType.SingleTargeting.TryGetValue(__instance.TargetType, out var func);
        if (func == null) return true;
        __result = func.Invoke(target, __instance.Owner);
        return false;
    }
}