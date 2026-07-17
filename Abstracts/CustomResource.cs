using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Extensions;
using BaseLib.Hooks;
using BaseLib.Patches.UI;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.Abstracts;

#region patches

/// <summary>
/// <seealso cref="CustomResourceUiPatches"/>
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
    Func<CardModel, bool, bool> costsMoreThanZero) : IComparable<ResourceHandler>
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
            new(resourceInstance.Id, Get, Cost, PrepForCombat, CleanupAfterCombat, ResourceCheck,
                Spend, RecordSpend, 
                AfterCardPlayedCleanup, EndOfTurnCleanup,
                SetToFreeThisCombat, SetToFreeThisTurn,
                FinalizeUpgrade, ResetForDowngrade,
                CostsMoreThanZero));
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
        _resource ??= new NotNullSpireField<PlayerCombatState, T>(() =>
        {
            BaseLibMain.Logger.Debug($"Initializing resource {typeof(T).Name} for combat");
            var res = new T();
            res.PrepForCombat();
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
        if (cost == null) return;
        
        await Resource.Get(card.Owner.PlayerCombatState!).Spend<T>(card.CombatState!, card, cost.GetAmountToSpend());
    }

    private static void RecordSpend(CardPlay cardPlay)
    {
        RecordedSpend[cardPlay] = LastSpend[cardPlay.Card];
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
    
    #endregion

    /// <summary>
    /// Retrieve the current resource info for a player's combat state.
    /// </summary>
    public static T Get(PlayerCombatState combatState)
    {
        return Resource[combatState];
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
    /// Default value of 0.
    /// </summary>
    public static int AmountSpent(CardPlay play)
    {
        return RecordedSpend[play];
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
    
    // Visuals Methods

    void UpdateCostVisuals(NCard nCard, PileType pileType);
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

    /// <inheritdoc />
    public bool CostsX { get; }

    /// <inheritdoc />
    public bool WasJustUpgraded { get; set; }

    /// <inheritdoc />
    public bool HasLocalModifiers => _localModifiers.Count > 0;

    public CustomResourceCost(CardModel card, int canonicalCost, bool costsX)
    {
        _card = card;
        CostsX = costsX;
        Canonical = CostsX ? 0 : canonicalCost;
        _base = Canonical;
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

        if (modifiers.HasFlag(CostModifiers.Global) && _card.CombatState != null)
            withModifiers = (int)Hook.ModifyEnergyCostInCombat(_card.CombatState, _card, withModifiers);
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
        return Hook.ModifyXValue(_card.CombatState, _card, CapturedXValue);
    }

    /// <inheritdoc />
    public int GetAmountToSpend()
    {
        if (!CostsX)
            return Math.Max(0, GetWithModifiers(CostModifiers.All));
        var playerCombatState = _card.Owner.PlayerCombatState;
        return playerCombatState?.Energy ?? 0;
    }

    /// <inheritdoc />
    public int GetResolved()
    {
        return CostsX ? CapturedXValue : Math.Max(0, GetWithModifiers(CostModifiers.All));
    }

    /// <inheritdoc />
    public UnplayableReason ResourceCheck(PlayerCombatState combatState, CardModel card)
    {
        var resource = CustomResources<T>.Get(combatState);
        var required = GetWithModifiers(CostModifiers.All);
        return resource.CanAfford(card, required) ? UnplayableReason.None : resource.UnplayableReason;
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
        
        return new CustomResourceCost<T>(newCard, CustomResources<T>.CanonicalCost(newCard), newCard.EnergyCost.CostsX)
        {
            _base = _base,
            _capturedXValue = _capturedXValue,
            WasJustUpgraded = WasJustUpgraded,
            _localModifiers = list
        };
    }
    
    // Visuals

    public void UpdateCostVisuals(NCard nCard, PileType pileType)
    {
        //TODO
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
    /// Return new instance of class that will be used as a singleton to receive card cost UI update events.
    /// Will be called during startup of game, when the resource is registered.
    /// </summary>
    public abstract ICustomCostVisualsHandler CostVisualsHandler();

    /// <summary>
    /// Return new instance of class that will be used as a singleton to receive resource amount UI update events.
    /// Will be called during startup of game, when the resource is registered.
    /// </summary>
    public abstract ICustomResourceVisualsHandler ResourceVisualsHandler();

    /// <summary>
    /// Whether methods that make a card free to play should also set this cost.
    /// <seealso cref="CardModel.SetToFreeThisTurn"/><seealso cref="CardModel.SetToFreeThisCombat"/>
    /// </summary>
    public virtual bool ApplySharedModification => true;

    /// <summary>
    /// Called when the resource is initialized at the start of each combat, if preparation is necessary.
    /// Note that this occurs when the PlayerCombatState is initialized.
    /// </summary>
    public virtual void PrepForCombat()
    {
        
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
        
            int oldEnergy = field;
            field = value;
            AmountChanged?.Invoke(oldEnergy, field);
        }
    }

    /// <summary>
    /// Called to spend this resource. Defaults to behaving the same as calling <see cref="ModifyAmount"/> with negative amount,
    /// then triggers <seealso cref="BaseLibHooks.AfterSpendCustomResource{T}"/>
    /// </summary>
    /// <param name="spender">The model these resources are being spent on.</param>
    /// <param name="amount">The amount of this resource to spend.</param>
    public virtual async Task Spend<T>(ICombatState combatState, AbstractModel? spender, int amount) where T : CustomResource
    {
        if (this is not T thisT)
            throw new ArgumentException(
                "Attempted to call Spend on a resource with a generic type that does not match the resource.");
        
        ModifyAmount(-amount);

        await BaseLibHooks.AfterSpendCustomResource(combatState, thisT, spender, amount);
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
}

/// <summary>
/// A basic resource that functions similarly to stars, effectively just being a number tracked per player
/// that can be manipulated and spent.
/// </summary>
/// <param name="resourceId"></param>
/// <param name="setEachTurn">If greater than 0, this resource's amount is set to this value at the start of each turn.</param>
public abstract class BasicCustomResource(string resourceId, int setEachTurn = -1) : CustomResource(resourceId)
{
    private readonly int _setEachTurn = setEachTurn;

    /// <inheritdoc />
    public override void PrepForCombat()
    {
        Amount = 0;
    }

    public override ICustomCostVisualsHandler CostVisualsHandler()
    {
        return new BasicCostVisualsHandler(this);
    }

    public override ICustomResourceVisualsHandler ResourceVisualsHandler()
    {
        return new BasicResourceVisualsHandler(this);
    }
}

public class BasicCostVisualsHandler(CustomResource resource) : ICustomCostVisualsHandler
{
    
}

public class BasicResourceVisualsHandler(CustomResource resource) : ICustomResourceVisualsHandler
{
    
}