using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Hooks;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Hooks;

/// <summary>
/// Patches all references to the max hand size constant of 10 to instead call GetMaxHandSize(player) which can be modified by IMaxHandSizeModifier implementations.
/// </summary>
public static class MaxHandSizePatch
{
    static MethodInfo? MaxCardsInHandProperty = AccessTools.PropertyGetter(typeof(CardPile), "MaxCardsInHand");
    
    /// <summary>
    /// The default max hand size constant used in the base game.
    /// </summary>
    public const int DefaultMaxHandSize = 10;

    internal static readonly MethodInfo GetMaxHandSizeFromBaseMethod = AccessTools.Method(typeof(MaxHandSizePatch), nameof(GetMaxHandSize), [typeof(Player), typeof(int)]);

    internal static bool IsDefaultMaxHandSizeConst(CodeInstruction ins)
    {
        return (ins.opcode == OpCodes.Ldc_I4_S && ins.operand is sbyte sb && sb == DefaultMaxHandSize)
               || (ins.opcode == OpCodes.Ldc_I4 && ins.operand is int i && i == DefaultMaxHandSize);
    }
    internal static bool IsBetaMaxHandSize(CodeInstruction ins)
    {
        return MaxCardsInHandProperty != null &&
               ins.Calls(MaxCardsInHandProperty);
    }

    /// <summary>
    /// Calculates the max hand size for a given player by invoking all IMaxHandSizeModifier implementations.
    /// </summary>
    /// <param name="player">The player to calculate the max hand size for.</param>
    /// <returns>The calculated max hand size.</returns>
    [Obsolete("Prefer to use GetMaxHandSize(player, CardPile.MaxCardsInHand) instead")]
    public static int GetMaxHandSize(Player player)
    {
        //Preparation for possibly more useful MaxCardsInHand property
        return GetMaxHandSize(player, DefaultMaxHandSize);
    }

    /// <summary>
    /// Calculates the max hand size for a given player by invoking all IMaxHandSizeModifier implementations.
    /// </summary>
    /// <param name="player">The player to calculate the max hand size for.</param>
    /// <param name="baseLimit">The limit before max hand size modifiers are applied.</param>
    /// <returns>The calculated max hand size.</returns>
    public static int GetMaxHandSize(Player player, int baseLimit)
    {
        var runState = player.RunState ?? NullRunState.Instance;
        var combatState = BetaMainCompatibility.Creature_.CombatState.Get(player.Creature);

        var amount = baseLimit;
        var list = new List<IMaxHandSizeModifier>();

        foreach (var modifier in BetaMainCompatibility.RunState.IterateHookListeners.Invoke<IEnumerable<AbstractModel>>(runState, combatState)
                                 ?? throw new InvalidOperationException("Failed to invoke IterateHookListeners properly"))
        {
            if (modifier is IMaxHandSizeModifier maxHandSizeModifier)
            {
                list.Add(maxHandSizeModifier);
                amount = maxHandSizeModifier.ModifyMaxHandSize(player, amount);
            }
        }

        foreach (var modifier in list)
        {
            amount = modifier.ModifyMaxHandSizeLate(player, amount);
        }

        return Math.Max(0, amount);
    }

    internal static int GetMaxHandSizeFromCard(CardModel? card, int baseAmount)
    {
        return card?.Owner is { } player ? GetMaxHandSize(player, baseAmount) : DefaultMaxHandSize;
    }
}

/// <summary>
/// Patches <c>static bool CardPileCmd.CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(Player player)</c>.
/// Changes the max hand size constant to a call to GetMaxHandSize(player).
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot))]
static class CardPileCmd_CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot_MaxHandSizePatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        foreach (var ins in instructions)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0); // player
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, MaxHandSizePatch.GetMaxHandSizeFromBaseMethod);
                continue;
            }
            yield return ins;
        }
    }
}

/// <summary>
/// Patches <c>void CombatManager.SetupPlayerTurn(Player player, HookPlayerChoiceContext choiceContext)</c>.
/// Changes the max hand size constant to a call to GetMaxHandSize(player).
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetupPlayerTurn))]
static class CombatManager_SetupPlayerTurn_MaxHandSizePatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        foreach (var ins in instructions)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1); // player
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, MaxHandSizePatch.GetMaxHandSizeFromBaseMethod);
                continue;
            }
            yield return ins;
        }
    }
}

/// <summary>
/// Patches <c>void CardConsoleCmd.Process(Player issuingPlayer, string[] args)</c>.
/// Changes the max hand size constant to a call to GetMaxHandSize(player).
/// </summary>
[HarmonyPatch(typeof(CardConsoleCmd), nameof(CardConsoleCmd.Process))]
static class CardConsoleCmd_Process_MaxHandSizePatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        foreach (var ins in instructions)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1); // issuingPlayer
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, MaxHandSizePatch.GetMaxHandSizeFromBaseMethod);
                continue;
            }
            yield return ins;
        }
    }
}

/// <summary>
/// Patches <c>static Task&lt;IEnumerable&lt;CardModel&gt;&gt; CardPileCmd.Draw(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw = false)</c>.
/// Changes the max hand size constant to a call to GetMaxHandSize(player).
/// </summary>
[HarmonyPatch]
static class CardPileCmd_Draw_MaxHandSizePatch
{
    static MethodInfo TargetMethod() => AccessTools.AsyncMoveNext(
        AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();

        new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldfld(null).PredicateMatch(op => op is FieldInfo field && field.FieldType == typeof(Player)))
            .CopyMatch(out var loadPlayer);

        foreach (var ins in code)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                foreach (var codeInstruction in loadPlayer.Select(ci => ci.Clone())) yield return codeInstruction; //player
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, MaxHandSizePatch.GetMaxHandSizeFromBaseMethod);
                continue;
            }

            yield return ins;
        }
    }
}

/// <summary>
/// Patches <c>static Task&lt;IReadOnlyList&lt;CardPileAddResult&gt;&gt; CardPileCmd.Add(IEnumerable&lt;CardModel&gt; cards, CardPile? newPile, CardPilePosition position, AbstractModel? source, bool skipVisuals = false)</c>.
/// Changes the max hand size constant to a call to GetMaxHandSize(player).
/// </summary>
[HarmonyPatch]
static class CardPileCmd_Add_MaxHandSizePatch
{
    static MethodInfo TargetMethod() => AccessTools.AsyncMoveNext(
        AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
            [typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool), typeof(bool)])
        ?? AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
            [typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)])
        );

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        var code = instructions.ToList();

        new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldfld(null).PredicateMatch(op => op is FieldInfo field && field.FieldType == typeof(Player)))
            .CopyMatch(out var loadPlayer);

        foreach (var ins in code)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                foreach (var codeInstruction in loadPlayer.Select(ci => ci.Clone())) yield return codeInstruction; //player
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, MaxHandSizePatch.GetMaxHandSizeFromBaseMethod);
                continue;
            }

            yield return ins;
        }
    }
}

/// <summary>
/// Patches Scrawl, Dredge, CrashLanding, and Pillage OnPlay hand-size constants to use GetMaxHandSize(player).
/// </summary>
[HarmonyPatch]
static class CardOnPlay_MaxHandSizePatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.AsyncMoveNext(AccessTools.Method(typeof(Scrawl), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]));
        yield return AccessTools.AsyncMoveNext(AccessTools.Method(typeof(Dredge), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]));
        yield return AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CrashLanding), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]));
        yield return AccessTools.AsyncMoveNext(AccessTools.Method(typeof(Pillage), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]));
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        var code = instructions.ToList();

        new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldfld(null).PredicateMatch(op => op is FieldInfo field && typeof(CardModel).IsAssignableFrom(field.FieldType)))
            .CopyMatch(out var loadCard);

        foreach (var ins in code)
        {
            if (MaxHandSizePatch.IsDefaultMaxHandSizeConst(ins) || MaxHandSizePatch.IsBetaMaxHandSize(ins))
            {
                foreach (var codeInstruction in loadCard.Select(ci => ci.Clone())) yield return codeInstruction; //player
                yield return ins; //base value
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MaxHandSizePatch), nameof(MaxHandSizePatch.GetMaxHandSizeFromCard)));
                continue;
            }

            yield return ins;
        }
    }
}

/// <summary>
/// Patches <c>void NPlayerHand.StartCardPlay(NHandCardHolder holder, bool startedViaShortcut)</c>.
/// Let the shortcuts return releaseCard(Arrow Down) input action intead of null when the index is out of bounds.
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.StartCardPlay))]
static class NPlayerHandStartCardPlayShortcutSafePatch
{
    static StringName GetShortcutOrDefault(NPlayerHand hand, int idx)
    {
        var arr = hand._selectCardShortcuts;
        return idx >= 0 && idx < arr.Length ? arr[idx] : MegaInput.releaseCard;
    }

    static readonly FieldInfo _selectCardShortcutsField = AccessTools.Field(typeof(NPlayerHand), "_selectCardShortcuts");
    static readonly FieldInfo _draggedHolderIndexField = AccessTools.Field(typeof(NPlayerHand), "_draggedHolderIndex");
    static readonly MethodInfo _getShortcutOrDefault = AccessTools.Method(typeof(NPlayerHandStartCardPlayShortcutSafePatch), nameof(GetShortcutOrDefault));

    [HarmonyTranspiler]
    static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original)
    {
        return new InstructionPatcher(instructions)
            .Match(
                new InstructionMatcher()
                    .ldarg_0()
                    .opcode(OpCodes.Ldfld).PredicateMatch(op => Equals(op, _selectCardShortcutsField))
                    .ldarg_0()
                    .opcode(OpCodes.Ldfld).PredicateMatch(op => Equals(op, _draggedHolderIndexField))
                    .opcode(OpCodes.Ldelem_Ref)
            )
            .ReplaceLastMatch([
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, _draggedHolderIndexField),
                new CodeInstruction(OpCodes.Call, _getShortcutOrDefault),
            ]);
    }
}

/// <summary>
/// Patches HandPosHelper.GetPosition, GetAngle, and GetScale to infer hand size from the position of the cards when the hand size exceeds 10.
/// </summary>
[HarmonyPatch]
static class HandPosHelperGetPositionPatch
{
    private static float GetInferredHalfSpread(int handSize)
    {
        var capped = Math.Min(handSize, 14);
        var t = (capped - 10) / 4f;
        t = Mathf.Clamp(t, 0f, 1f);
        return Mathf.Lerp(610f, 690f, t);
    }

    [HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetPosition))]
    [HarmonyPrefix]
    static bool GetPosition(int handSize, int cardIndex, ref Vector2 __result)
    {
        if (handSize <= 10)
            return true;

        if (cardIndex < 0 || cardIndex >= handSize)
            throw new ArgumentOutOfRangeException(nameof(cardIndex),
                $"Card index {cardIndex} is outside hand size {handSize}.");

        var halfSpread = GetInferredHalfSpread(handSize);
        var edgeLift = Math.Max(72f, 88f - (handSize - 10) * 1.5f);
        var u = handSize <= 1 ? 0f : (2f * cardIndex / (handSize - 1f)) - 1f;
        var x = halfSpread * u;
        var y = Math.Min(18f, -64f + edgeLift * u * u);
        __result = new Vector2(x, y);

        return false;
    }

    [HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetAngle))]
    [HarmonyPrefix]
    static bool GetAngle(int handSize, int cardIndex, ref float __result)
    {
        if (handSize <= 10)
            return true;

        if (cardIndex < 0 || cardIndex >= handSize)
            throw new ArgumentOutOfRangeException(nameof(cardIndex),
                $"Card index {cardIndex} is outside hand size {handSize}.");

        var halfSpread = GetInferredHalfSpread(handSize);
        var edgeLift = Math.Max(72f, 88f - (handSize - 10) * 1.5f);
        var u = handSize <= 1 ? 0f : (2f * cardIndex / (handSize - 1f)) - 1f;
        var dyDu = 2f * edgeLift * u;
        var dxDu = Math.Max(1f, halfSpread);
        var angle = Mathf.RadToDeg(Mathf.Atan2(dyDu, dxDu));
        __result = Mathf.Clamp(angle, -18f, 18f);

        return false;
    }

    [HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetScale))]
    [HarmonyPrefix]
    static bool GetScale(int handSize, ref Vector2 __result)
    {
        if (handSize <= MaxHandSizePatch.DefaultMaxHandSize)
            return true;

        var scalar = 0.64f * MathF.Pow(0.95f, handSize - 11);
        __result = Vector2.One * Math.Max(0.48f, scalar);

        return false;
    }
}