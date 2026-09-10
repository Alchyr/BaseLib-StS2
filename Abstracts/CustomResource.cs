using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using BaseLib.BaseLibScenes;
using BaseLib.Extensions;
using BaseLib.Hooks;
using BaseLib.Patches.UI;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Helpers.Models;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.Abstracts;

#region patches

/// <summary>
/// <seealso cref="ExtraCombatUi"/>
/// </summary>
[HarmonyPatch]
internal static class CustomResourcePatches
{
    internal static readonly List<ResourceHandler> RegisteredResources = [];
    
    // Patches to prep and clean up custom resources alongside PlayerCombatState.
    [HarmonyPatch(typeof(PlayerCombatState), MethodType.Constructor, typeof(Player))]
    [HarmonyPostfix]
    static void Setup(PlayerCombatState __instance)
    {
        BaseLibMain.Logger.Debug($"Initializing custom resources ({RegisteredResources.Count}) at start of combat");
        foreach (var resource in RegisteredResources)
        {
            resource.Prep(__instance);
        }
    }
    
    [HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.AfterCombatEnd))]
    [HarmonyPostfix]
    static void Cleanup(PlayerCombatState __instance)
    {
        BaseLibMain.Logger.Debug($"Cleaning up custom resources ({RegisteredResources.Count}) at end of combat");
        foreach (var resource in RegisteredResources)
        {
            resource.Cleanup(__instance);
        }
    }
    
    // Patches for card cost
    [HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.HasEnoughResourcesFor))]
    [HarmonyPostfix]
    static void CheckAdditionalCosts(PlayerCombatState __instance, CardModel card, ref bool __result, ref UnplayableReason reason)
    {
        //Already false, then skip checking custom costs.
        if (!__result) return;

        foreach (var resource in RegisteredResources)
        {
            var result = resource.ResourceCheck(__instance, card);
            if (result == UnplayableReason.None) continue;
            
            reason = result;
            __result = false;
            return;
        }
    }

    // Spend resources
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.SpendResources), MethodType.Async)]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> AddSpendAdditionalCosts(ILGenerator generator, IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        return AsyncMethodCall.Create(generator, instructions, original, 
            AccessTools.Method(typeof(CustomResourcePatches), nameof(SpendAdditionalCosts)), afterState: original);
    }
    static async Task SpendAdditionalCosts(CardModel __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            await resource.Spend(__instance);
        }
    }
    
    // Record spent resources
    [HarmonyPatch(typeof(CardPlay), nameof(CardPlay.Card), MethodType.Setter)]
    [HarmonyPostfix]
    static void RecordAdditionalCosts(CardPlay __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.RecordSpend(__instance);
        }
    }

    // Cost modification cleanup
    [HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.AfterCardPlayedCleanup))]
    [HarmonyPostfix]
    static void CleanupAdditionalCosts(CardEnergyCost __instance)
    {
        var card = __instance._card;
        foreach (var resource in RegisteredResources)
        {
            resource.AfterCardPlayedCleanup(card);
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.EndOfTurnCleanup))]
    [HarmonyPostfix]
    static void CleanupEndOfTurn(CardModel __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.EndOfTurnCleanup(__instance);
        }
    }
    
    // Shared cost modification
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisCombat))]
    [HarmonyPostfix]
    static void SetToFreeThisCombat(CardModel __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.SetToFreeThisCombat(__instance);
        }
    }
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.SetToFreeThisTurn))]
    [HarmonyPostfix]
    static void SetToFreeThisTurn(CardModel __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.SetToFreeThisTurn(__instance);
        }
    }
    
    // Upgrade finalize
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal))]
    [HarmonyPostfix]
    static void FinalizeAdditionalResourceUpgrades(CardModel __instance)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.FinalizeUpgrade(__instance);
        }
    }
    
    // Downgrade
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.DowngradeInternal))]
    [HarmonyTranspiler]
    static List<CodeInstruction> DowngradeAdditionalResourcesTranspiler(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new CallMatcher(typeof(CardEnergyCost).Method(nameof(CardEnergyCost.ResetForDowngrade))))
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(CustomResourcePatches), nameof(DowngradeAdditionalResources))
            ]);
    }

    static void DowngradeAdditionalResources(CardModel card)
    {
        foreach (var resource in RegisteredResources)
        {
            resource.ResetForDowngrade(card);
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.CostsEnergyOrStars))]
    [HarmonyPostfix]
    static void OrAnotherResource(CardModel __instance, ref bool __result, bool includeGlobalModifiers)
    {
        if (__result) return;
        
        foreach (var resource in RegisteredResources)
        {
            if (resource.CostsMoreThanZero(__instance, includeGlobalModifiers))
            {
                __result = true;
            }
        }
    }
    
    // Captured X Value for Autoplay
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay), MethodType.Async)]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> CaptureXTranspiler(IEnumerable<CodeInstruction> code, MethodBase original)
    {
        var stateMachineType = original.DeclaringType;
        if (stateMachineType == null)
        {
            BaseLibMain.Logger.Info("Failed to patch CardCmd.AutoPlay; DeclaringType null");
            return code;
        }

        var skipXCaptureField = stateMachineType.FindStateMachineField("skipXCapture");

        return (List<CodeInstruction>)new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldfld()
                .ldfld()
                .call_any(typeof(CardModel).PropertyGetter(nameof(CardModel.Owner))))
            .CopyMatch(0, 3, out var loadCard)
            .Match(new InstructionMatcher()
                .call_any(typeof(Player).PropertyGetter(nameof(Player.PlayerCombatState)))
                .stloc_any())
            .Step(-1).GetIndexOperand(out var playerCombatStateLocal).Step(1)
            .Insert([
                ..loadCard,
                CodeInstruction.LoadLocal(playerCombatStateLocal),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, skipXCaptureField),
                CodeInstruction.Call(typeof(CustomResourcePatches), nameof(CaptureX))
            ]);
    }

    static void CaptureX(CardModel card, PlayerCombatState? playerCombatState, bool skipXCapture)
    {
        if (skipXCapture || playerCombatState == null) return;
        
        foreach (var resource in RegisteredResources)
        {
            var cost = resource.GetCost(card);
            if (cost?.CostsX == true)
            {
                cost.CapturedXValue = resource.GetResource(playerCombatState).Amount;
            }
        }
    }
    
    // Energy Reset
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset), MethodType.Async)]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> AfterPlay(ILGenerator generator, IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        return AsyncMethodCall.Create(generator, instructions, original, 
            AccessTools.Method(typeof(CustomResourcePatches), nameof(BeforeEnergyResetHook)), beforeState: original);
    }

    private static async Task BeforeEnergyResetHook(ICombatState combatState, Player player)
    {
        var playerCombatState = player?.PlayerCombatState;
        if (playerCombatState == null) return;
        
        foreach (var resource in RegisteredResources)
        {
            resource.GetResource(playerCombatState).StartOfTurnReset(playerCombatState, combatState);
        }
    }
    
    // Visual updates
    [HarmonyPatch(typeof(NCard), nameof(NCard.UpdateEnergyCostVisuals))]
    [HarmonyPostfix]
    static void UpdateCustomCostVisuals(NCard __instance, PileType pileType)
    {
        var card = __instance.Model;
        if (card == null) return;
        
        foreach (var resourceHandler in RegisteredResources)
        {
            var cost = resourceHandler.GetCost(card);
            if (cost == null) continue;
            resourceHandler.UpdateCostVisuals(__instance, cost, pileType);
        }
    }
}



#endregion

internal class ResourceHandler(string id, 
    Func<PlayerCombatState, CustomResource> getResource,
    Func<CardModel, ICustomResourceCost?> getCost,
    Action<PlayerCombatState> prep, Action<PlayerCombatState> cleanup,
    Func<PlayerCombatState, CardModel, UnplayableReason> resourceCheck,
    Func<CardModel, Task> spend,
    Action<CardPlay> recordSpend,
    Action<CardModel> afterCardPlayedCleanup,
    Action<CardModel> endOfTurnCleanup,
    Action<CardModel> setToFreeThisCombat,
    Action<CardModel> setToFreeThisTurn,
    Action<CardModel> finalizeUpgrade,
    Action<CardModel> resetForDowngrade,
    Func<CardModel, bool, bool> costsMoreThanZero,
    Action<NCard, ICustomResourceCost, PileType> updateCostVisuals) : IComparable<ResourceHandler>
{
    public string Id { get; } = id;

    public Func<PlayerCombatState, CustomResource> GetResource { get; } = getResource;
    public Func<CardModel, ICustomResourceCost?> GetCost { get; } = getCost;
    public Action<PlayerCombatState> Prep { get; } = prep;
    public Action<PlayerCombatState> Cleanup { get; } = cleanup;
    
    public Func<PlayerCombatState, CardModel, UnplayableReason> ResourceCheck { get; } = resourceCheck;

    public Func<CardModel, Task> Spend { get; } = spend;
    public Action<CardPlay> RecordSpend { get; } = recordSpend;

    public Action<CardModel> AfterCardPlayedCleanup { get; } = afterCardPlayedCleanup;
    public Action<CardModel> EndOfTurnCleanup { get; } = endOfTurnCleanup;

    public Action<CardModel> SetToFreeThisCombat { get; } = setToFreeThisCombat;
    public Action<CardModel> SetToFreeThisTurn { get; } = setToFreeThisTurn;
    
    public Action<CardModel> FinalizeUpgrade { get; } = finalizeUpgrade;
    
    public Action<CardModel> ResetForDowngrade { get; } = resetForDowngrade;

    public Func<CardModel, bool, bool> CostsMoreThanZero { get; } = costsMoreThanZero;
    
    public Action<NCard, ICustomResourceCost, PileType> UpdateCostVisuals { get; } = updateCostVisuals;

    public int CompareTo(ResourceHandler? other)
    {
        return string.Compare(Id, other?.Id, StringComparison.Ordinal);
    }
}

/// <summary>
/// Helper class for storing and accessing information regarding custom costs.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
public static class CustomResources<T> where T : CustomResource, new()
{
    private static bool _registered;
    // Called through reflection in PostModInitPatch for each defined resource type.
    internal static void Register(T resourceInstance)
    {
        if (_registered) return;
        _registered = true;
        
        CustomResourcePatches.RegisteredResources.InsertSorted(
            new(resourceInstance.Id,
                Get, Cost, PrepForCombat, CleanupAfterCombat, ResourceCheck,
                Spend, RecordSpend, 
                AfterCardPlayedCleanup, EndOfTurnCleanup,
                SetToFreeThisCombat, SetToFreeThisTurn,
                FinalizeUpgrade, ResetForDowngrade,
                CostsMoreThanZero,
                (nCard, cost, pile) => UpdateCostVisuals?.Invoke(nCard, cost, pile)));
        
        resourceInstance.RegisterResourceVisuals<T>();
    }
    
    private static NotNullSpireField<PlayerCombatState, T>? _resource;
    private static SpireField<CardModel, int>? _canonicalCost;
    private static SpireField<CardModel, CustomResourceCost<T>?>? _cost;
    private static SpireField<CardModel, int>? _lastSpend;
    private static SpireField<CardPlay, int>? _recordedSpend;

    private static SpireField<CardModel, int> CanonicalCostField =>
        _canonicalCost ??= new SpireField<CardModel, int>(() => -1)
            .CopyOnClone();

    private static NotNullSpireField<PlayerCombatState, T> Resource =>
        _resource ??= new NotNullSpireField<PlayerCombatState, T>((playerCombatState) =>
        {
            BaseLibMain.Logger.Debug($"Initializing resource {typeof(T).Name} for combat");
            var res = new T();
            res.Setup<T>(playerCombatState._player);
            res.PrepForCombat<T>(playerCombatState);
            res.AmountChanged += CombatManager.Instance.StateTracker.OnPlayerCombatStateValueChanged;
            return res;
        });

    private static SpireField<CardModel, CustomResourceCost<T>?> CostField =>
        _cost ??= new SpireField<CardModel, CustomResourceCost<T>?>(() => null)
            .CopyOnClone((source, dest, original) =>
            {
                CostField[dest] = original?.Clone(dest);
            });

    private static SpireField<CardModel, int> LastSpend =>
        _lastSpend ??= new SpireField<CardModel, int>(() => 0);

    private static SpireField<CardPlay, int> RecordedSpend =>
        _recordedSpend ??= new SpireField<CardPlay, int>(() => 0);

    #region helper methods
    
    private static void PrepForCombat(PlayerCombatState combatState)
    {
        Resource.Get(combatState); //Initializes default value of field
    }

    private static void CleanupAfterCombat(PlayerCombatState combatState)
    {
        Resource.Get(combatState).AmountChanged -= CombatManager.Instance.StateTracker.OnPlayerCombatStateValueChanged;
    }

    private static UnplayableReason ResourceCheck(PlayerCombatState combatState, CardModel card)
    {
        var cost = Cost(card);
        return cost?.ResourceCheck(combatState, card) ?? UnplayableReason.None;
    }

    private static async Task Spend(CardModel card)
    {
        var cost = Cost(card);
        LastSpend[card] = 0;
        if (cost == null)
            return;

        var spend = cost.GetAmountToSpend();
        if (cost.CostsX)
            cost.CapturedXValue = spend;
        if (await Resource.Get(card.Owner.PlayerCombatState!)
                .Spend<T>(card.CombatState!, card, spend, cost.IsOptional(card.Owner)))
        {
            LastSpend[card] = spend;
        }
    }

    private static void RecordSpend(CardPlay cardPlay)
    {
        RecordedSpend[cardPlay] = LastSpend[cardPlay.Card];
        BaseLibMain.Logger.Debug($"Recorded spend: {LastSpend[cardPlay.Card]}");
    }
    
    private static void AfterCardPlayedCleanup(CardModel card)
    {
        Cost(card)?.AfterCardPlayedCleanup();
    }

    private static void EndOfTurnCleanup(CardModel card)
    {
        if (Cost(card)?.EndOfTurnCleanup() == true)
        {
            card.InvokeEnergyCostChanged();
        }
    }
    
    private static void SetToFreeThisCombat(CardModel card)
    {
        var cost = Cost(card);
        if (cost != null && Resource.Get(card.Owner.PlayerCombatState!).ApplySharedModification)
        {
            cost.SetThisCombat(0);
        }
    }
    
    private static void SetToFreeThisTurn(CardModel card)
    {
        var cost = Cost(card);
        if (cost != null && Resource.Get(card.Owner.PlayerCombatState!).ApplySharedModification)
        {
            cost.SetThisTurnOrUntilPlayed(0);
        }
    }

    private static void FinalizeUpgrade(CardModel card)
    {
        Cost(card)?.FinalizeUpgrade();
    }

    private static void ResetForDowngrade(CardModel card)
    {
        Cost(card)?.ResetForDowngrade();
    }

    private static bool CostsMoreThanZero(CardModel card, bool includeGlobalModifiers)
    {
        var cost = Cost(card);
        if (cost == null || cost.CostsX) return false;
        
        return cost.GetWithModifiers(includeGlobalModifiers ? CostModifiers.All : CostModifiers.Local) > 0;
    }

    /// <summary>
    /// Event triggered when an NCard with a custom resource cost has its UpdateEnergyCostVisuals method called.
    /// </summary>
    public static event Action<NCard, ICustomResourceCost, PileType>? UpdateCostVisuals;
    
    #endregion

    /// <summary>
    /// Retrieve the current resource info for a player's combat state.
    /// </summary>
    public static T Get(PlayerCombatState combatState)
    {
        return Resource[combatState];
    }

    /// <summary>
    /// Attempt to retrieve the current resource info for a player's combat state.
    /// Returns false if combat state is null.
    /// </summary>
    public static bool TryGet(PlayerCombatState? combatState, [NotNullWhen(true)] out T? result)
    {
        result = null;
        if (combatState == null) return false;
        result = Resource[combatState];
        return true;
    }

    /// <summary>
    /// Sets a card's canonical cost for this resource.
    /// -1 is the default meaning no cost, and <see cref="int.MinValue"/> is used for X costs.
    /// </summary>
    public static void SetCanonicalCost(CardModel card, int canonicalCost)
    {
        CanonicalCostField[card] = canonicalCost;
    }
    /// <summary>
    /// Sets a card's canonical cost for this resource to be an X cost, represented by <see cref="int.MinValue"/>.
    /// </summary>
    public static void SetXCost(CardModel card)
    {
        CanonicalCostField[card] = int.MinValue;
    }

    /// <summary>
    /// The card's canonical cost of this resource. -1 if not set.
    /// </summary>
    public static int CanonicalCost(CardModel card)
    {
        return CanonicalCostField[card];
    }

    /// <summary>
    /// Retrieves the object containing the resource's related properties and methods,
    /// equivalent to <see cref="CardEnergyCost"/>.
    /// Null for cards without a canonical cost of this type.
    /// </summary>
    public static CustomResourceCost<T>? Cost(CardModel card)
    {
        var result = CostField[card];
        if (result != null) return result;

        var canonicalCost = CanonicalCostField[card];
        if (canonicalCost == -1) return null;
        
        var isXCost = canonicalCost == int.MinValue;
        return CostField[card] = new CustomResourceCost<T>(card, isXCost ? 0 : canonicalCost, isXCost);
    }

    /// <summary>
    /// Retrieves the amount of this resource spent on this card play.
    /// Default value of -1 if this resource was not spent on this play.
    /// </summary>
    public static int AmountSpent(CardPlay play)
    {
        return RecordedSpend[play];
    }

    /// <summary>
    /// Checks if the amount of this resource spent on this card play is 0 or more (<seealso cref="AmountSpent"/>)
    /// </summary>
    public static bool WasSpent(CardPlay play)
    {
        return RecordedSpend[play] >= 0;
    }
}

/// <summary>
/// Base interface for a custom resource for non-generic access
/// </summary>
public interface ICustomResourceCost
{
    /// <summary>
    /// This card's "official" starting resource cost.
    /// This is what would appear on the card if it was printed out on paper.
    /// </summary>
    int Canonical { get; }

    /// <summary>
    /// Whether this card has an resource cost of X.
    /// X-costs automatically spend all of the player's remaining resource when played, and their effect should be
    /// based on the amount spent.
    /// </summary>
    bool CostsX { get; }

    /// <summary>
    /// Whether this cost is required to play the card.
    /// You can check if a cost was paid for in a card's use method
    /// by calling <see cref="CustomResources{T}.WasSpent"/>.
    /// </summary>
    public bool IsOptional(Player? p);

    /// <summary>
    /// Was this card's resource cost just recently upgraded?
    /// This is mainly used to show upgrade preview values in green.
    /// This should be cleared after the upgrade is complete.
    /// </summary>
    bool WasJustUpgraded { get; set; }

    /// <summary>
    /// Does this resource cost have any local modifiers?
    /// See <see cref="F:MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.Local" /> for details.
    /// </summary>
    bool HasLocalModifiers { get; }

    /// <summary>
    /// Get this card's resource cost, including the specified modifier types.
    /// See <see cref="T:MegaCrit.Sts2.Core.Entities.Cards.CostModifiers" /> for details on what types are available.
    /// </summary>
    int GetWithModifiers(CostModifiers modifiers);
    
    /// <summary>
    /// The amount of this resource most recently spent to play this X-cost card.
    /// Used when duplicating X-cost cards, to make sure the duplicates are played with the same value.
    ///
    /// Set in <see cref="CustomResources&lt;T&gt;.Spend"/>
    /// 
    /// WARNING: Only use this for calculations related to resources spent. If you're using this to calculate a
    /// cost-X card's effect, use <see cref="ResolveXValue" /> instead, as it will take X-value
    /// modifications (like <see cref="T:MegaCrit.Sts2.Core.Models.Relics.ChemicalX" />) into account.
    /// </summary>
    public int CapturedXValue { get; set; }

    /// <summary>
    /// Resolve this cost's X value. Should only be used in on-play logic.
    /// Takes modifications to X values (like <see cref="T:MegaCrit.Sts2.Core.Models.Relics.ChemicalX" />) into account.
    /// </summary>
    int ResolveXValue();

    /// <summary>
    /// Get the amount of this resource that should be spent to play this card.
    /// 
    /// * For X-cost cards, this is the amount of the resource that its owner has.
    /// * For normal cards, this is the current cost including all modifiers
    ///   (see <see cref="GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers)" /> with <see cref="F:MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All" />) clamped to 0.
    /// 
    /// The game uses this value when actually spending the resource to play the card.
    /// Additionally, this is useful for effects that need to know how much WOULD be spent to play the card without
    /// actually playing it, such as <see cref="T:MegaCrit.Sts2.Core.Models.Cards.Scavenge" />.
    /// </summary>
    int GetAmountToSpend();

    /// <summary>
    /// Get the "resolved" cost of this card. This can mean one of two things:
    /// 
    /// * For X-cost cards, this is the captured X-cost value (see <see cref="CapturedXValue" />).
    /// * For normal cards, this is the current cost including all modifiers
    ///   (see <see cref="GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers)" /> with <see cref="F:MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All" />) clamped to 0.
    /// 
    /// This is useful for effects that need to know the card's cost AFTER it was played, such as
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Relics.IntimidatingHelmet" />. For normal cards, these effects just care about the card's current cost
    /// (including all modifiers). For X-cost cards, these effects care about the X-value that was set for the card when
    /// it was played.
    /// </summary>
    int GetResolved();

    /// <summary>
    /// Checks if a card should be playable based on this cost.
    /// </summary>
    /// <returns><see cref="UnplayableReason.None"/> if the card is playable, a different reason otherwise.</returns>
    UnplayableReason ResourceCheck(PlayerCombatState combatState, CardModel card);

    /// <summary>
    /// Determines highlighting of cost color based on card and resource's current state.
    /// </summary>
    CardCostColor GetCostColor(CardModel model, ICombatState? combatState);

    /// <summary>
    /// Set this cost to the specified amount until the card is played.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.Eidolon" /> says "Reduce the cost of all cards in your Discard Pile to 0 until played."
    /// </example>
    /// <param name="cost">New cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void SetUntilPlayed(int cost, bool reduceOnly = false);

    /// <summary>
    /// Set this cost to the specified amount until the end of the current turn OR until the card is played, whichever
    /// comes first.
    /// Note that the text of these effects will just say "this turn"; the "or until played" part is left implicit
    /// because it's wordy and rarely relevant.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.BulletTime" /> says "Reduce the cost of ALL cards in your Hand to 0 this turn."
    /// </example>
    /// <param name="cost">New cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void SetThisTurnOrUntilPlayed(int cost, bool reduceOnly = false);

    /// <summary>
    /// BE CAREFUL USING THIS! You usually want <see cref="SetThisTurnOrUntilPlayed(System.Int32,System.Boolean)" /> instead.
    /// Set this cost to the specified amount until the end of the current turn.
    /// Note that most effects that say "this turn" really mean "this turn or until played".
    /// This method should only be used for the few effects that should last for multiple plays in the same turn.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.Invoke" /> says "This card costs 0 if Osty has attacked this turn."
    /// </example>
    /// <param name="cost">New cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void SetThisTurn(int cost, bool reduceOnly = false);

    /// <summary>
    /// Set this cost to the specified amount for the rest of the combat.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.Enlightenment" />+ says "Reduce the cost of ALL cards in your Hand to 1 this combat."
    /// </example>
    /// <param name="cost">New cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void SetThisCombat(int cost, bool reduceOnly = false);

    /// <summary>
    /// Add the specified amount to this cost until the card is played.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Enchantments.SlumberingEssence" /> says "If this card is in your hand at the end of turn, reduce its cost by 1
    /// until it is played."
    /// </example>
    /// <param name="amount">Amount to add to the cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void AddUntilPlayed(int amount, bool reduceOnly = false);

    /// <summary>
    /// Add the specified amount to this cost until the end of the current turn OR until the card is played, whichever
    /// comes first.
    /// Note that the text of these effects will just say "this turn"; the "or until played" part is left implicit
    /// because it's wordy and rarely relevant.
    /// </summary>
    /// <example>None yet. Update this if we add one!</example>
    /// <param name="amount">Amount to add to the cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void AddThisTurnOrUntilPlayed(int amount, bool reduceOnly = false);

    /// <summary>
    /// BE CAREFUL USING THIS! You usually want <see cref="AddThisTurnOrUntilPlayed(System.Int32,System.Boolean)" /> instead.
    /// Add the specified amount to this cost until the end of the current turn.
    /// Note that most effects that say "this turn" really mean "this turn or until played".
    /// This method should only be used for the few effects that should last for multiple plays in the same turn.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.Pinpoint" /> says "Costs 1 less for each Skill played this turn."
    /// </example>
    /// <param name="amount">Amount to add to the cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void AddThisTurn(int amount, bool reduceOnly = false);

    /// <summary>
    /// Add the specified amount to this cost for the rest of the combat.
    /// </summary>
    /// <example>
    /// <see cref="T:MegaCrit.Sts2.Core.Models.Cards.KinglyKick" /> says "Whenever you draw this card, lower its cost by 1 this combat."
    /// </example>
    /// <param name="amount">Amount to add to the cost.</param>
    /// <param name="reduceOnly">
    /// Whether this modifier should only be included in the cost calculation if it would lower the current cost.
    /// See <see cref="P:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier.IsReduceOnly" /> for details.
    /// </param>
    void AddThisCombat(int amount, bool reduceOnly = false);

    /// <summary>
    /// Clear local cost modifiers that should last until the end of the turn.
    /// </summary>
    /// <returns>True if any modifiers were cleared and EnergyCostChanged should be invoked.</returns>
    bool EndOfTurnCleanup();

    /// <summary>
    /// Clear local cost modifiers that should last until the card is played.
    /// </summary>
    /// <returns>True if any modifiers were cleared and EnergyCostChanged should be invoked.</returns>
    bool AfterCardPlayedCleanup();

    /// <summary>
    /// Upgrade the cost of this card by the specified amount.
    /// </summary>
    /// <param name="addend">Amount to add to the current cost (usually negative).</param>
    void UpgradeCostBy(int addend);

    /// <summary>
    /// Finalize an upgrade after calling UpgradeCostBy.
    /// This clears out state that is used for displaying an upgrade preview.
    /// </summary>
    void FinalizeUpgrade();

    /// <summary>Reset cost to base values during downgrade.</summary>
    void ResetForDowngrade();

    /// <summary>
    /// This is mainly meant for internal usage.
    /// The base game uses this externally only for <see cref="T:MegaCrit.Sts2.Core.Models.Cards.MadScience" />.
    /// </summary>
    void SetCustomBaseCost(int newBaseCost);
}

/// <summary>
/// A cost for a custom resource, equivalent to <see cref="CardEnergyCost"/>.
/// </summary>
public class CustomResourceCost<T> : ICustomResourceCost where T : CustomResource, new()
{
    private readonly CardModel _card;
    private int _base;
    private int _capturedXValue;

    /// <summary>
    /// This card's local cost modifiers.
    /// See <see cref="T:MegaCrit.Sts2.Core.Entities.Cards.LocalCostModifier" /> for details on how this works.
    /// </summary>
    private List<LocalCostModifier> _localModifiers = [];

    /// <inheritdoc />
    public int Canonical { get; }

    /// <summary>
    /// Usually equivalent to canonical cost, unless modified separately.
    /// </summary>
    public int Base => _base;

    /// <inheritdoc />
    public bool CostsX { get; }

    /// <inheritdoc />
    public bool WasJustUpgraded { get; set; }

    /// <inheritdoc />
    public bool HasLocalModifiers => _localModifiers.Count > 0;

    private bool? _forceOptional = null;
    /// <inheritdoc />
    public virtual bool IsOptional(Player? p)
    {
        var combatState = p?.PlayerCombatState;
        if (combatState == null) return false; //Out of combat
        return _forceOptional ?? CustomResources<T>.Get(combatState).IsDefaultOptional;
    }

    public CustomResourceCost(CardModel card, int canonicalCost, bool costsX = false)
    {
        _card = card;
        CostsX = costsX;
        Canonical = CostsX ? 0 : canonicalCost;
        _base = Canonical;
    }

    /// <summary>
    /// Makes this cost always optional (or always required), ignoring the resource's default optional property.
    /// </summary>
    public void MakeOptional(bool optional = true)
    {
        _forceOptional = optional;
    }

    /// <inheritdoc />
    public int GetWithModifiers(CostModifiers modifiers)
    {
        var withModifiers = _base;
        
        if (_card.IsCanonical || _base < 0 || CostsX)
            return withModifiers;
        
        if (modifiers.HasFlag(CostModifiers.Local))
        {
            foreach (var localModifier in _localModifiers)
                withModifiers = localModifier.Modify(withModifiers);
        }

        var playerCombatState = _card.Owner?.PlayerCombatState;
        if (modifiers.HasFlag(CostModifiers.Global) && _card.CombatState != null && playerCombatState != null)
            withModifiers = (int)BaseLibHooks.
                ModifyResourceCostInCombat(_card.CombatState, CustomResources<T>.Get(playerCombatState), _card, withModifiers);
        return Math.Max(0, withModifiers);
    }

    /// <inheritdoc />
    public int CapturedXValue
    {
        get
        {
            if (!CostsX)
                throw new InvalidOperationException("Only X-cost cards have a captured value.");
            return _capturedXValue;
        }
        set
        {
            _card.AssertMutable();
            if (!CostsX)
                throw new InvalidOperationException("Only X-cost cards have a captured value.");
            _capturedXValue = value;
        }
    }
    
    /// <inheritdoc />
    public int ResolveXValue()
    {
        if (!CostsX)
            throw new InvalidOperationException($"This cost of type {GetType()} is not an X-cost.");
        if (_card.CombatState == null)
            throw new InvalidOperationException($"Attempted to resolve X value of cost {GetType()} outside of combat.");
        return Hook.ModifyXValue(_card.CombatState, _card, CapturedXValue);
    }

    /// <inheritdoc />
    public int GetAmountToSpend()
    {
        if (!CostsX)
            return Math.Max(0, GetWithModifiers(CostModifiers.All));
        
        var playerCombatState = _card.Owner.PlayerCombatState;
        if (playerCombatState == null) return 0;
        
        return CustomResources<T>.Get(playerCombatState).Amount;
    }

    /// <inheritdoc />
    public int GetResolved()
    {
        return CostsX ? CapturedXValue : Math.Max(0, GetWithModifiers(CostModifiers.All));
    }

    /// <inheritdoc />
    public UnplayableReason ResourceCheck(PlayerCombatState combatState, CardModel card)
    {
        if (IsOptional(combatState._player)) return UnplayableReason.None;
        
        var resource = CustomResources<T>.Get(combatState);
        var required = GetWithModifiers(CostModifiers.All);
        return resource.CanAfford(card, required) ? UnplayableReason.None : resource.UnplayableReason;
    }

    //TODO - Optional costs in grey when can't afford them
    /// <inheritdoc />
    public CardCostColor GetCostColor(CardModel card, ICombatState? combatState)
    {
        var playerCombatState = card.Owner?.PlayerCombatState;
        if (combatState == null || playerCombatState == null) return CardCostColor.Unmodified;
        var resource = CustomResources<T>.Get(playerCombatState);
        var resourceUnplayableReason = resource.UnplayableReason;

        if (!card.CanPlay(out var reason, out _) && reason.HasFlag(resourceUnplayableReason))
            return CardCostColor.InsufficientResources;
        if (CostsX)
            return CardCostColor.Unmodified;
        if (BaseLibHooks.TryModifyResourceCostWithHooks(card, resource, combatState, out var hookModifiedCost))
            return CardCostHelper.GetColorForHookModifiedCost(hookModifiedCost, Base);
        return HasLocalModifiers ? 
            CardCostHelper.GetColorForLocalCost(GetWithModifiers(CostModifiers.Local), GetWithModifiers(CostModifiers.None))
            : CardCostColor.Unmodified;
    }

    /// <inheritdoc />
    public void SetUntilPlayed(int cost, bool reduceOnly = false)
    {
        if (cost == 0 && Canonical < 0)
            return;
        _localModifiers.Add(new LocalCostModifier(cost, LocalCostType.Absolute,
            LocalCostModifierExpiration.WhenPlayed, reduceOnly));
    }

    /// <inheritdoc />
    public void SetThisTurnOrUntilPlayed(int cost, bool reduceOnly = false)
    {
        if (cost == 0 && Canonical < 0)
            return;
        _localModifiers.Add(new LocalCostModifier(cost, LocalCostType.Absolute,
            LocalCostModifierExpiration.EndOfTurn | LocalCostModifierExpiration.WhenPlayed, reduceOnly));
    }

    /// <inheritdoc />
    public void SetThisTurn(int cost, bool reduceOnly = false)
    {
        if (cost == 0 && Canonical < 0)
            return;
        _localModifiers.Add(new LocalCostModifier(cost, LocalCostType.Absolute,
            LocalCostModifierExpiration.EndOfTurn, reduceOnly));
    }

    /// <inheritdoc />
    public void SetThisCombat(int cost, bool reduceOnly = false)
    {
        if (cost == 0 && Canonical < 0)
            return;
        _localModifiers.Add(new LocalCostModifier(cost, LocalCostType.Absolute,
            LocalCostModifierExpiration.EndOfCombat, reduceOnly));
    }

    /// <inheritdoc />
    public void AddUntilPlayed(int amount, bool reduceOnly = false)
    {
        if (amount == 0)
            return;
        _localModifiers.Add(new LocalCostModifier(amount, LocalCostType.Relative,
            LocalCostModifierExpiration.WhenPlayed, reduceOnly));
    }

    /// <inheritdoc />
    public void AddThisTurnOrUntilPlayed(int amount, bool reduceOnly = false)
    {
        if (amount == 0)
            return;
        _localModifiers.Add(new LocalCostModifier(amount, LocalCostType.Relative,
            LocalCostModifierExpiration.EndOfTurn | LocalCostModifierExpiration.WhenPlayed, reduceOnly));
    }

    /// <inheritdoc />
    public void AddThisTurn(int amount, bool reduceOnly = false)
    {
        if (amount == 0)
            return;
        _localModifiers.Add(new LocalCostModifier(amount, LocalCostType.Relative,
            LocalCostModifierExpiration.EndOfTurn, reduceOnly));
    }

    /// <inheritdoc />
    public void AddThisCombat(int amount, bool reduceOnly = false)
    {
        if (amount == 0)
            return;
        _localModifiers.Add(new LocalCostModifier(amount, LocalCostType.Relative,
            LocalCostModifierExpiration.EndOfCombat, reduceOnly));
    }

    /// <inheritdoc />
    public bool EndOfTurnCleanup()
    {
        _card.AssertMutable();
        return _localModifiers.RemoveAll(m =>
            m.Expiration.HasFlag(LocalCostModifierExpiration.EndOfTurn)) > 0;
    }

    /// <inheritdoc />
    public bool AfterCardPlayedCleanup()
    {
        _card.AssertMutable();
        return _localModifiers.RemoveAll(m =>
            m.Expiration.HasFlag(LocalCostModifierExpiration.WhenPlayed)) > 0;
    }

    /// <inheritdoc />
    public void UpgradeCostBy(int addend)
    {
        _card.AssertMutable();
        if (CostsX || addend == 0)
            return;
        int num = _base;
        int newBaseCost = Math.Max(_base + addend, 0);
        WasJustUpgraded = true;
        if (newBaseCost < num)
        {
            foreach (var localModifier in _localModifiers)
            {
                if (localModifier.Type == LocalCostType.Absolute && localModifier.Amount > newBaseCost)
                    localModifier.Amount = newBaseCost;
            }
        }

        SetCustomBaseCost(newBaseCost);
    }

    /// <inheritdoc />
    public void FinalizeUpgrade()
    {
        _card.AssertMutable();
        WasJustUpgraded = false;
    }

    /// <summary>Reset cost to base values during downgrade.</summary>
    public void ResetForDowngrade()
    {
        _card.AssertMutable();
        _base = Canonical;
        _card.InvokeEnergyCostChanged(); // Flashes NCard if it exists and updates combat state
    }

    /// <inheritdoc />
    public void SetCustomBaseCost(int newBaseCost)
    {
        _card.AssertMutable();
        _base = newBaseCost;
        _card.InvokeEnergyCostChanged(); // Flashes NCard if it exists and updates combat state
    }

    /// <summary>
    /// Create a deep clone of this CustomResourceCost for the specified card.
    /// </summary>
    /// <param name="newCard">The card that will own the cloned CustomResourceCost.</param>
    /// <returns>A deep clone of this CustomResourceCost.</returns>
    public CustomResourceCost<T> Clone(CardModel newCard)
    {
        var list = _localModifiers
            .Select(m => m.Clone())
            .ToList();
        
        return new CustomResourceCost<T>(newCard, CustomResources<T>.CanonicalCost(newCard), CostsX)
        {
            _base = _base,
            _capturedXValue = _capturedXValue,
            WasJustUpgraded = WasJustUpgraded,
            _forceOptional = _forceOptional,
            _localModifiers = list
        };
    }
}

/// <summary>
/// A resource that functions as a cost for cards.
/// Resources do not exist outside of combat; an instance of each resource class is created at the start of each
/// combat attached to each PlayerCombatState.
/// Implementations of this interface should provide a parameterless constructor.
/// An instance of each resource is created during startup for registration using their ID and VisualsHandler properties.
/// </summary>
public abstract class CustomResource(string id)
{
    /// <summary>
    /// Event that should be triggered whenever amount of this resource changes, increase or decrease.
    /// By default, all resources will have <see cref="CombatStateTracker.OnPlayerCombatStateValueChanged"/>
    /// subscribed to this event.
    /// </summary>
    public event Action<int, int>? AmountChanged;

    /// <summary>
    /// A unique ID used to identify and sort this resource type.
    /// </summary>
    public string Id { get; protected set; } = id;
    
    /// <summary>
    /// The player this resource is attached to.
    /// </summary>
    public Player? Owner { get; set; }

    /// <summary>
    /// Called when the resource is registered during startup.
    /// Recommended use is overriding to register UI through <see cref="ExtraCombatUi"/>.
    /// See <see cref="BasicCustomResource"/> for an example.
    /// </summary>
    public virtual void RegisterResourceVisuals<T>() where T : CustomResource, new()
    {
        ValidateType<T>();
    }

    /// <summary>
    /// Whether methods that make a card free to play should also set this cost.
    /// <seealso cref="CardModel.SetToFreeThisTurn"/><seealso cref="CardModel.SetToFreeThisCombat"/>
    /// </summary>
    public virtual bool ApplySharedModification => true;

    /// <summary>
    /// Whether cards that cost this resource default to having this cost be optional.
    /// Costs can individually be made optional.
    /// </summary>
    public virtual bool IsDefaultOptional => false;

    /// <summary>
    /// The path to a small icon image that can be used in card/tooltip text.
    /// </summary>
    public virtual string? IconPath => null;

    /// <summary>
    /// A color used for font outlines.
    /// </summary>
    public virtual Color MainColor => StsColors.defaultEnergyCostOutline;

    /// <summary>
    /// Generate a hovertip for this resource, defaulting to localization
    /// entries Id.title and Id.description in the table static_hover_tips.
    /// If IconPath is set, it will be usable in the tooltip with {resourceIcon}.
    /// </summary>
    public virtual HoverTip? MakeTip() {
        var title = new LocString("static_hover_tips", $"{Id}.title");
        var description = new LocString("static_hover_tips", $"{Id}.description");
        if (IconPath != null && ResourceLoader.Exists(IconPath))
        {
            title.Add("resourceIcon", $"[img]{IconPath}[/img]");
            description.Add("resourceIcon", $"[img]{IconPath}[/img]");
        }
            
        return new HoverTip(title, description);
    }

    /// <summary>
    /// Called when the resource is initialized at the start of combat, before <see cref="PrepForCombat"/>.
    /// Sets the <see cref="Owner"/> property and receives this resource's type as a parameter to set up hooks.
    /// </summary>
    public void Setup<T>(Player player) where T : CustomResource, new()
    {
        ValidateType<T>();
        
        Owner = player;
    }

    /// <summary>
    /// Called when the resource is initialized at the start of each combat, if preparation is necessary.
    /// Note that this occurs when the PlayerCombatState is initialized.
    /// </summary>
    public virtual void PrepForCombat<T>(PlayerCombatState playerCombatState) where T : CustomResource, new()
    {
        ValidateType<T>();
    }

    /// <summary>
    /// The quantity of this resource available to spend.
    /// Can be overridden if custom behavior is necessary (spending something that isn't just a tracked number)
    /// </summary>
    public virtual int Amount
    {
        get;
        set
        {
            if (value == field) return;
        
            var oldAmount = field;
            field = value;
            AmountChanged?.Invoke(oldAmount, field);
        }
    }

    /// <summary>
    /// Called at the start of each turn, after the player's energy is reset, before the AfterEnergyReset hook occurs.
    /// </summary>
    public virtual void StartOfTurnReset(PlayerCombatState playerCombatState, ICombatState combatState)
    {
        
    }

    /// <summary>
    /// Called to spend this resource. Defaults to behaving the same as calling <see cref="ModifyAmount"/> with negative amount,
    /// then triggers <seealso cref="BaseLibHooks.AfterSpendCustomResource{T}"/>.
    /// Used for effects that spend this resource other than a card cost.
    /// If not optional and amount is insufficient, will spend all remaining amount and return true.
    /// </summary>
    /// <param name="spender">The model these resources are being spent on.</param>
    /// <param name="amount">The amount of this resource to spend.</param>
    /// <param name="optional">Whether this cost is expected to be optional.</param>
    /// <returns>Whether the resource was actually spent.</returns>
    public virtual async Task<bool> Spend<T>(ICombatState combatState, AbstractModel? spender, int amount, bool optional) where T : CustomResource, new()
    {
        var thisT = ValidateType<T>();

        if (amount > Amount)
        {
            if (!optional)
            {
                BaseLibMain.Logger.Warn($"Attempted to spend secondary resource {typeof(T).Name} with insufficient amount;" +
                                        $" Current: {Amount} | Required: {amount} ");
                amount = Amount;
            }
            else
            {
                return false;
            }
        }
        
        ModifyAmount(-amount);
        await BaseLibHooks.AfterSpendCustomResource(combatState, thisT, spender, amount);
        
        return true;
    }

    /// <summary>
    /// Modifies the quantity of this resource available to spend.
    /// </summary>
    /// <param name="change">The amount to add.</param>
    public void ModifyAmount(int change)
    {
        Amount += change;
    }

    /// <summary>
    /// The UnplayableReason used if you can't afford to play a card due to this resource.
    /// You are suggested to use a custom enum value for your own resource, but not required.
    /// Due to UnplayableReason being a flag type enum, the number of keys it can have is relatively limited.
    /// </summary>
    public virtual UnplayableReason UnplayableReason => UnplayableReason.EnergyCostTooHigh;

    /// <summary>
    /// Return whether you can currently afford to spend this resource on this card.
    /// If false, the card will not be playable and <see cref="UnplayableReason"/> will be used.
    /// </summary>
    public virtual bool CanAfford(CardModel card, int cost)
    {
        return Amount >= cost;
    }

    /// <summary>
    /// Text to display, used by NAdditionalResourceDisplay, or can be used for a custom
    /// resource counter.
    /// </summary>
    /// <param name="displayAmount">The "current" amount of the resource to display.</param>
    public virtual string GetTextForAmount(int displayAmount)
    {
        return $"{displayAmount}";
    }

    /// <summary>
    /// Whether this resource's display should be visible. Note that once a resource display becomes visible,
    /// it is expected to stay visible for the rest of combat.
    /// Used by NAdditionalResourceDisplay, or can be used for a custom resource counter.
    /// The most common check for this will be checking Owner.Character's type.
    /// </summary>
    public virtual bool ShouldShowDisplay() => Amount > 0;

    /// <summary>
    /// Verifies that the type parameter matches the resource's actual type.
    /// </summary>
    protected T ValidateType<T>([System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        if (this is not T thisT)
            throw new ArgumentException(
                $"Attempted to call {callerName} on a resource with a generic type that does not match the resource.");
        return thisT;
    }
}

/// <summary>
/// A basic resource that functions similarly to stars, effectively just being a number tracked per player
/// that can be manipulated and spent.
/// </summary>
/// <param name="resourceId"></param>
/// <param name="setEachTurn">If greater than 0, this resource's amount is set to this value at the start of each turn.</param>
public abstract class BasicCustomResource(string resourceId, int setEachTurn = -1) : CustomResource(resourceId)
{
    /// <summary>
    /// The default amount of this resource that its current amount will be set to at the start of each turn.
    /// For the actual amount with modifiers (<see cref="IModifyMaxResource"/>) use <see cref="Max"/>.
    /// </summary>
    public int DefaultMax { get; set; } = setEachTurn;
    
    /// <summary>
    /// The amount of this resource that its current amount will be set to at the start of each turn.
    /// If <see cref="DefaultMax"/> is less than 0, always returns 0.
    /// </summary>
    public int Max
    {
        get
        {
            if (Owner == null || DefaultMax < 0 || _getModifiedMax == null) return 0;
            return Math.Max(0, _getModifiedMax(Owner.Creature.CombatState, Owner, DefaultMax));
        }
    }

    private Func<ICombatState?, Player, int, int>? _getModifiedMax;

    /// <inheritdoc />
    public override void PrepForCombat<T>(PlayerCombatState playerCombatState)
    {
        var thisT = ValidateType<T>();
        
        Amount = 0;
        _getModifiedMax = (combatState, player, original) => HookUtils.Modify<IModifyMaxResource<T>, int>(combatState, original, 
                (m, a) => m.ModifyResourceAmount(player, thisT, a), out var modifiers);
    }

    /// <inheritdoc />
    public override void StartOfTurnReset(PlayerCombatState playerCombatState, ICombatState combatState)
    {
        if (DefaultMax >= 0)
        {
            Amount = Max;
        }
    }

    /// <summary>
    /// Path used to load a single texture used for all of the resource's visuals.
    /// Override to change the path, or override <see cref="CostVisualsHandler"/>
    /// and/or <see cref="RegisterResourceVisuals"/> to change how the visuals are handled.
    /// </summary>
    public virtual string TexturePath => "";

    /// <inheritdoc />
    public override string GetTextForAmount(int displayAmount)
    {
        return DefaultMax >= 0 ? $"{displayAmount}/{Max}" : $"{displayAmount}";
    }

    /// <inheritdoc />
    public override bool ShouldShowDisplay()
    {
        //Above 0 regardless of character, cannot rely on amount
        if (DefaultMax > 0)
        {
            return false;
        }

        return Amount > 0;
    }

    /// <inheritdoc />
    public override void RegisterResourceVisuals<T>()
    {
        ValidateType<T>();

        ExtraCombatUi.RegisterCombatUiElement(static (ui, player, combatState) =>
        {
            var playerCombatState = player.PlayerCombatState;
            if (playerCombatState == null) return null;

            if (CustomResources<T>.Get(playerCombatState) is not BasicCustomResource resource) return null;
            
            var tex = PreloadManager.Cache.GetTexture2D(resource.TexturePath);
            var display = NAdditionalResourceDisplay.Create<T>(player, resource, tex);
            
            ui.AddChild(display);
            
            return display;
        }, ExtraCombatUi.CombatUiPositioning.AroundEnergy);
        
        var getDisplay = ExtraCardUi.RegisterCreateCardUiElement(ExtraCardUi.CardUiPositioning.AroundCost, 
            _ => new NAdditionalCostDisplay(Id, TexturePath, MainColor),
            (card, model, display) =>
            {
                if (model == null) return false;
                var cost = CustomResources<T>.Cost(model);
                if (cost == null) return false;

                display.UpdateCostVisual(card, cost, PileType.None);
                return true;
            });

        CustomResources<T>.UpdateCostVisuals += (card, cost, pileType) =>
        {
            getDisplay(card).UpdateCostVisual(card, cost, pileType);
        };
    }
}