using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.Patches.Content;


public class CustomPiles
{
    public static readonly Dictionary<PileType, Func<CustomPile>> CustomPileProviders = [];
    
    public static readonly SpireField<PlayerCombatState, Dictionary<PileType, CustomPile>> Piles = new(() =>
    {
        Dictionary<PileType, CustomPile> customPiles = [];
        foreach (var pair in CustomPileProviders)
        {
            customPiles.Add(pair.Key, pair.Value());
        }
        return customPiles;
    });

    public static void RegisterCustomPile(PileType pileType, Func<CustomPile> constructor)
    {
        CustomPileProviders.Add(pileType, constructor);
    }

    public static CardPile[] AddCustomPiles(CardPile[] original, PlayerCombatState combatState)
    {
        var customPiles = Piles.Get(combatState)?.Values;
        return customPiles == null ? original : [.. original, .. customPiles];
    }

    public static CustomPile? GetCustomPile(PlayerCombatState? state, PileType type)
    {
        if (state == null) //not in combat, this may occur when game attempts to get Deck
        {
            return null;
        }
        return Piles.Get(state)?.GetValueOrDefault(type);
    }

    public static bool IsCustomPile(PileType pileType)
    {
        return CustomPileProviders.ContainsKey(pileType);
    }

    public static Vector2 GetPosition(PileType pileType, NCard? card, Vector2 size)
    {
        if (!CustomPileProviders.ContainsKey(pileType)) return Vector2.Zero;

        if (card == null || card.Model == null) return Vector2.Zero;

        var pile = GetCustomPile(card.Model.Owner.PlayerCombatState, pileType);
        if (pile == null) throw new Exception($"CustomPile {pileType} does not exist");

        return pile.GetTargetPosition(card.Model, size);
    }

    public static NCard? FindOnTable(CardModel card, PileType pileType)
    {
        if (!CustomPileProviders.ContainsKey(pileType)) return null;

        MainFile.Logger.Info("Looking for NCard in Custom Pile!");
        var pile = GetCustomPile(card.Owner.PlayerCombatState, pileType);

        return pile?.GetNCard(card);
    }

    public static bool IsCardVisible(CardModel card)
    {
        return false;
    }
}


[HarmonyPatch(typeof(CardPile), nameof(CardPile.Get))]
class GetCombatPile
{
    [HarmonyPrefix]
    static bool CheckCustomPile(PileType type, Player player, ref CardPile? __result)
    {
        __result = CustomPiles.GetCustomPile(player.PlayerCombatState, type);
        return __result == null;
    }
}

[HarmonyPatch(typeof(PileTypeExtensions), nameof(PileTypeExtensions.IsCombatPile))]
class IsCombatPile
{
    [HarmonyPrefix]
    static bool CustomIsCombat(PileType pileType, ref bool __result)
    {
        if (CustomPiles.CustomPileProviders.ContainsKey(pileType))
        {
            __result = true;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.FindOnTable))]
class GetNCardPile
{
    //FindOnTable will check card.Pile first, which will end up at PlayerCombatState.AllPiles, which is already patched.
    //Then it will switch based on the pile's type, with any non-visible piles returning a null value.
    //Contrary to the name, overridePile is used only if the card is not found in a pile.
    //Cards in the Special pile should also have a supplier of some kind if they are visible or not?
    [HarmonyTranspiler]
    static List<CodeInstruction> CheckCustomPiles(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldloc_1()
                .ret()
            )
            .Step(-2)
            .GetLabels(out var labels) //find label of ending load local 1 + return
            .ResetPosition()
            .Match(new InstructionMatcher()
                .stloc_3()
                .ldloc_3()
            )
            .Step(-1)
            .Insert([
                CodeInstruction.LoadLocal(3), //Load piletype from loc 3
                CodeInstruction.Call(typeof(CustomPiles), nameof(CustomPiles.IsCustomPile)), //have bool on stack
                CodeInstruction.LoadArgument(0), //Load card
                CodeInstruction.LoadLocal(3), //Load piletype from loc 3
                CodeInstruction.Call(typeof(CustomPiles), nameof(CustomPiles.FindOnTable)),
                CodeInstruction.StoreLocal(1), //Store in localvar 1
                new CodeInstruction(OpCodes.Brtrue_S, labels[0]) //If IsCustomPile returned true, branch to return
            ]);
    }
}

[HarmonyPatch(typeof(PileTypeExtensions), nameof(PileTypeExtensions.GetTargetPosition))]
class GetPilePosition
{
    //CHANGE - 
    //transpile after array is set up, pass entire array to a method, return another array
    [HarmonyTranspiler]
    static List<CodeInstruction> CustomPilePosition(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldloc_2()
                .ret()
            )
            .Step(-2)
            .GetLabels(out var labels) //find label of return
            .ResetPosition()
            .Match(new InstructionMatcher()
                .call(AccessTools.PropertyGetter(typeof(Rect2), "Size"))
                .stloc_0()
                .ldarg_0()
            )
            .Step(-1)
            .Insert([
                CodeInstruction.LoadArgument(0), //Load piletype from arg 0
                CodeInstruction.Call(()=>CustomPiles.IsCustomPile(default)), //have bool on stack
                CodeInstruction.LoadArgument(0), //Load piletype
                CodeInstruction.LoadArgument(1), //Load ncard
                CodeInstruction.LoadLocal(0), //Load size rect
                CodeInstruction.Call(()=>CustomPiles.GetPosition(default, default, default)),
                CodeInstruction.StoreLocal(2),
                new CodeInstruction(OpCodes.Brtrue_S, labels[0]) //If IsSpecialPile returned true, branch to return
            ]);
    }
}

//Maybe also of note: NCard includes many checks for PileType.Hand to determine if costs/playability checks should display


[HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.AllPiles), MethodType.Getter)]
class SpecialPileInCombat
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddPile(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .stfld(AccessTools.Field(typeof(PlayerCombatState), "_piles"))
            )
            .Step(-1)
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(CustomPiles), "AddCustomPiles")
            ]);
    }
    /*{
        return new Locator(instructions)
           .AddMatcher(new InstructionMatcher()
               .ldarg_0()
               .ldc_i4_5()
               .newarr(typeof(CardPile))
           )
           .MatchAllInOrder()
           .Step(-2)
           .IncrementIntPush(out var newSize) //increase array size by 1
           .ClearMatchers()
           .AddMatcher(new InstructionMatcher()
                .ldc_i4_4()
                .ldarg_0()
                .call(AccessTools.PropertyGetter(typeof(PlayerCombatState), "PlayPile"))
                .stelem_ref()
                .stfld(typeof(PlayerCombatState), "_piles")
           )
           .MatchAllInOrder()
           .Step(-1)
           .Insert([
               new CodeInstruction(OpCodes.Dup),
               newSize,
               CodeInstruction.LoadArgument(0),
               new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CustomPiles), "Get")),
               new CodeInstruction(OpCodes.Stelem_Ref)
            ]);
    }*/
}

public class TheBigPatchToCardPileCmdAdd
{
    private static Type? stateMachineType;

    public static void Patch(Harmony harmony)
    {
        harmony.PatchAsyncMoveNext(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add), 
            [typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
            out stateMachineType,
            transpiler: AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), nameof(BigPatch)));
    }

    static List<CodeInstruction> BigPatch(IEnumerable<CodeInstruction> instructions)
    {
        if (stateMachineType == null) throw new Exception("Failed to get state machine type for async CardPileCmd.Add");

        FieldInfo fullHandAdd = stateMachineType.FindStateMachineField("isFullHandAdd");
        FieldInfo oldPile = stateMachineType.FindStateMachineField("oldPile");
        FieldInfo targetPile = stateMachineType.FindStateMachineField("targetPile");
        FieldInfo newPile = AccessTools.Field(stateMachineType, "newPile");
        FieldInfo card = stateMachineType.FindStateMachineField("card");
        MethodInfo pileTypeGetter = AccessTools.PropertyGetter(typeof(CardPile), "Type");
        
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher() //patch createCardNode
                .ldfld(fullHandAdd)
                .brtrue_s()
                .ldarg_0()
                .ldfld(oldPile)
                .brtrue_s()
            )
            .Step(-1).GetOperandLabel(out var createCardNodeLabel)
            .Step(1)
            .Insert([ // || IsPileCustomPileWithCardsVisible(targetPile), createCardNode = true
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, targetPile),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, card),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWhereCardShouldBeVisible")),
                new CodeInstruction(OpCodes.Brtrue_S, createCardNodeLabel)
            ])
            .Match(new InstructionMatcher() //patch isChangingPileWithoutNode, checking oldPile type
                .callvirt(pileTypeGetter) //Start match here as rest of match is very generic
                .stloc_s() //no variable index to reduce issues with different index usage
                .ldloc_s()
                .ldc_i4_1()
                .sub()
                .switch_()
                .br_s()
                .ldc_i4_1()
            )
            .Step(-1).GetLabels(out var isChangingPileWithoutNodeLabelA) //This is jump location if oldPile is a valid type
            .Step(-1) //This is the default branch of the switch
            .Insert([ // || IsPileCustomPileWithCardsNotVisible(oldPile)
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, oldPile),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, card),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWithCardNotVisible")),
                new CodeInstruction(OpCodes.Brtrue_S, isChangingPileWithoutNodeLabelA[0])
            ])
            .Match(new InstructionMatcher() //Checking targetPile type for isChangingPileWithoutNode
                .ldfld(targetPile)
                .callvirt(pileTypeGetter)
                .stloc_s().StoreOperand("index")
                .ldloc_s().OperandFromStore("index")
                .ldc_i4_1()
                .beq_s()
            )
            .Step(-1).GetOperandLabel(out var isChangingPileWithoutNodeLabelB) //jump location if targetPile is a valid type
            .Step(1)
            .Insert([ // || targetPile
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, targetPile),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, card),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileWithoutCustomTransition")),
                new CodeInstruction(OpCodes.Brtrue_S, isChangingPileWithoutNodeLabelB)
            ])
            .Match(new InstructionMatcher() //patch for cardNode?.UpdateVisuals condition
                .ldarg_0()
                .ldfld(newPile)
                .callvirt(pileTypeGetter)
                .ldc_i4_2()
                .beq_s()
            )
            .Step(-1).GetOperandLabel(out var updateVisualsLabel)
            .Step(1)
            .Insert([ // || IsPileCustomPileWithCardsVisible(newPile)
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, newPile),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, card),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWhereCardShouldBeVisible")),
                new CodeInstruction(OpCodes.Brtrue_S, updateVisualsLabel)
            ])
            .Match(new InstructionMatcher() //get local index of tween
                .callvirt(typeof(Node), nameof(Node.CreateTween))
                .ldc_i4_1()
                .callvirt(typeof(Tween), nameof(Tween.SetParallel))
                .stloc_any()
            )
            .Step(-1).GetIndexOperand(out var tweenIndex)
            .Match(new InstructionMatcher() //get fields of generated DisplayClass
                .newobj(null) //long match due to using entirely generic matches
                .stloc_s().StoreOperand("index")
                .ldloc_s().OperandFromStore("index")
                .ldloc_s()
                .stfld(null)
                .ldloc_s().OperandFromStore("index")
                .ldloc_s().OperandFromStore("index")
                .ldfld(null)
                .ldfld(null)
            )
            .Step(-1).GetOperand(out var cardNodeField)
            .Step(-1).GetOperand(out var cardNodeDisplayClassField)
            .Step(-1).GetIndexOperand(out var displayClassLocIndex)
            .Match(new InstructionMatcher() //patch for generic goaway tween
                .ldloc_s(displayClassLocIndex)
                .ldfld(null)
                .callvirt(AccessTools.PropertyGetter(typeof(CardModel), "Pile"))
                .callvirt(AccessTools.PropertyGetter(typeof(CardPile), "Type"))
                .stloc_s()
                .ldloc_s()
                .ldc_i4_1()
                .sub()
                .ldc_i4_2()
                .ble_un_s()
            )
            .Step(-1).GetOperandLabel(out var genericGoAwayTweenLabel)
            .Step(1)
            .InsertCopy(-10, 2) //copy card
            .Insert([
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileUseGenericTweenForOtherPlayers")),
                new CodeInstruction(OpCodes.Brtrue_S, genericGoAwayTweenLabel)
            ])
            .Match(new InstructionMatcher()
                .callvirt(AccessTools.Method(typeof(Tween), "TweenCallback"))
                .pop()
                .br()
            )
            .Step(-1).GetOperandLabel(out var tweenLoopEnd)
            .Match(new InstructionMatcher()
                .ldloc_s(displayClassLocIndex)
                .ldfld(null).PredicateMatch(obj => obj is FieldInfo field && field.Name.Equals("card"))
                .callvirt(AccessTools.PropertyGetter(typeof(CardModel), "Pile"))
                .callvirt(AccessTools.PropertyGetter(typeof(CardPile), "Type"))
                .stloc_s()
                .ldloc_s()
                .ldc_i4_2()
                .sub()
                .switch_()
            )
            .InsertCopy(-9, 2) //load card
            .Insert([
                CodeInstruction.LoadLocal(displayClassLocIndex),
                new CodeInstruction(OpCodes.Ldfld, cardNodeDisplayClassField),
                new CodeInstruction(OpCodes.Ldfld, cardNodeField), //three instructions to load cardnode
                CodeInstruction.LoadLocal(displayClassLocIndex + 2), //oldpile
                CodeInstruction.LoadLocal(tweenIndex), //tween
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileUseCustomTween")),
                new CodeInstruction(OpCodes.Brtrue_S, tweenLoopEnd)
            ]);
    }

    public static bool IsPileCustomPileWhereCardShouldBeVisible(CardPile pile, CardModel card)
    {
        return pile is CustomPile customPile && customPile.CardShouldBeVisible(card);
    }
    public static bool IsPileCustomPileWithCardNotVisible(CardPile pile, CardModel card)
    {
        return pile is CustomPile customPile && NCard.FindOnTable(card, pile.Type) == null;
    }
    public static bool CustomPileWithoutCustomTransition(CardPile pile, CardModel card) //for cards moving from non-visible pile to non-visible pile
    {
        return pile is CustomPile customPile && !customPile.CardShouldBeVisible(card) && !customPile.NeedsCustomTransitionVisual;
    }
    public static bool CustomPileUseGenericTweenForOtherPlayers(CardModel card)
    {
        var pile = card.Pile;
        return pile is CustomPile customPile && (customPile.CardShouldBeVisible(card) || !customPile.NeedsCustomTransitionVisual);
    }
    public static bool CustomPileUseCustomTween(CardModel card, NCard cardNode, CardPile oldPile, Tween tween)
    {
        var pile = card.Pile;
        if (pile is not CustomPile customPile) return false;

        return customPile.CustomTween(tween, card, cardNode, oldPile);
    }
}