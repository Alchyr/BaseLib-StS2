using System.Collections.ObjectModel;
using BaseLib.Extensions;
using BaseLib.Patches.Localization;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Abstracts;

/// <summary>
/// A model that is attached to a card to modify its behavior.
/// Receives all combat hooks, and is capable of modifying the card's description.
/// More features to be added in the future.
/// TODO - in progress - base value modification like enchants/afflictions
/// Passive cost modification? Without calling cost modification methods; works as a "modifier" in EnergyCost
/// </summary>
public abstract class CardModifier : AbstractModel, IComparable<CardModifier>
{
    private static readonly NotNullSpireField<CardModel, List<CardModifier>> _modifiers = 
            new NotNullSpireField<CardModel, List<CardModifier>>(() => [])
                .CopyOnClone((src, dst, modifiers) =>
                {
                    foreach (var modifier in modifiers)
                    {
                        var cloneModifier = (CardModifier) modifier.MutableClone();
                        dst.AddModifier(cloneModifier);
                        cloneModifier.AfterClonedOnCard(dst);
                    }
                });
    
    /// <summary>
    /// Obtains a new instance of a CardModifier from ModelDb using <see cref="CardModifier"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T Get<T>() where T : CardModifier
    {
        return ModelDb.CardModifier<T>();
    }
    
    internal static void RegisterSave()
    {
        ExtendedSaveTypes.RegisterListSaveType<ModifierSave>();
        ExtendedSaveTypes.RegisterDictionarySaveType<string, int>();
        ExtendedSaveTypes.RegisterObjectSaveType<ModifierSave>(
            ExtendedSaveTypes.PropertyFunc<ModifierSave, ModelId>("Id"), 
            ExtendedSaveTypes.PropertyFunc<ModifierSave, Dictionary<string, int>>("IntProperties"),
        ExtendedSaveTypes.PropertyFunc<ModifierSave, Dictionary<string, string>>("AdditionalProperties"));
        
        
        ExtendedSaveHandlers<CardModel, SerializableCard>.RegisterSave("BaseLibCardModifiers", 
            card => DirectModifiers(card).Select(ModifierSave.FromModifier).ToList(),
            LoadModifierSaves,
            (saves, writer) =>
            {
                writer.WriteInt(saves.Count);
                foreach (var save in saves)
                {
                    save.Serialize(writer);
                }
            },
            (reader) =>
            {
                List<ModifierSave> saves = [];
                int count = reader.ReadInt();
                for (int i = 0; i < count; ++i)
                {
                    ModifierSave save = new();
                    save.Deserialize(reader);
                    saves.Add(save);
                }

                return saves;
            });
    }
    
    private static void LoadModifierSaves(CardModel card, List<ModifierSave>? modifiers)
    {
        _modifiers[card] = modifiers?.Select(mod => mod.ToRealMod(card)).ToList() ?? [];
    }

    public sealed class ModifierSave : IPacketSerializable
    {
        public static ModifierSave FromModifier(CardModifier modifier)
        {
            var save = new ModifierSave()
            {
                Id = modifier.Id,
                Amount = modifier.Amount
            };
            modifier.StoreSaveData(save);
            return save;
        }

        public CardModifier ToRealMod(CardModel owner)
        {
            var mod = (CardModifier) ModelDb.GetById<CardModifier>(Id!).MutableClone();
            mod.Owner = owner;
            mod.Amount = Amount;
            mod.LoadSaveData(this);
            return mod;
        }
        
        public ModelId? Id { get; set; }
        public int Amount { get; set; }
        public Dictionary<string, int> IntProperties { get; set; } = [];
        public Dictionary<string, string> AdditionalProperties { get; set; } = [];

        /// <inheritdoc />
        public void Serialize(PacketWriter writer)
        {
            writer.WriteModelEntry(Id!);
            
            writer.WriteInt(Amount);
            
            writer.WriteInt(IntProperties.Count);
            foreach (var entry in IntProperties)
            {
                writer.WriteString(entry.Key);
                writer.WriteInt(entry.Value);
            }
            
            writer.WriteInt(AdditionalProperties.Count);
            foreach (var entry in AdditionalProperties)
            {
                writer.WriteString(entry.Key);
                writer.WriteString(entry.Value);
            }
        }

        /// <inheritdoc />
        public void Deserialize(PacketReader reader)
        {
            Id = reader.ReadModelIdAssumingType<CardModifier>();
            
            Amount = reader.ReadInt();
            
            int capacity = reader.ReadInt();
            IntProperties = new(capacity);
            for (int index = 0; index < capacity; ++index)
            {
                var key = reader.ReadString();
                IntProperties[key] = reader.ReadInt();
            }

            capacity = reader.ReadInt();
            AdditionalProperties = new(capacity);
            for (int index = 0; index < capacity; ++index)
            {
                var key = reader.ReadString();
                AdditionalProperties[key] = reader.ReadString();
            }
        }
    }


    /// <summary>
    /// Store values that must be saved in IntProperties or AdditionalProperties.
    /// Override this and <see cref="LoadSaveData"/> if you need to save additional information besides <see cref="Amount"/>.
    /// </summary>
    public virtual void StoreSaveData(ModifierSave save)
    {
        
    }
    /// <summary>
    /// Loads saved values into a new instance of this modifier.
    /// Override this and <see cref="StoreSaveData"/> if you need to save additional information besides <see cref="Amount"/>.
    /// </summary>
    public virtual void LoadSaveData(ModifierSave save)
    {
        
    }

    /// <summary>
    /// Gets the list of modifiers on a card.
    /// This list is read-only and cannot be modified.
    /// </summary>
    public static ReadOnlyCollection<CardModifier> Modifiers(CardModel card) => _modifiers[card]?.AsReadOnly() ?? throw new Exception("Card modifiers not found");
    /// <summary>
    /// Gets the list of modifiers on a card.
    /// Modifying this list will affect the modifiers on the card.
    /// It is not recommended to modify this list directly.
    /// </summary>
    public static List<CardModifier> DirectModifiers(CardModel card) => _modifiers[card] ?? throw new Exception("Card modifiers not found");

    /// <summary>
    /// Adds a card modifier to a card.
    /// </summary>
    public static void AddModifier<T>(CardModel card) where T : CardModifier
    {
        AddModifier(card, ModelDb.CardModifier<T>(true));
    }
    
    /// <summary>
    /// Adds a card modifier to a card with a specified amount.
    /// </summary>
    public static void AddModifier<T>(CardModel card, int amount) where T : CardModifier
    {
        var mod = ModelDb.CardModifier<T>(true);
        mod.Amount = amount;
        AddModifier(card, mod);
    }
    
    /// <summary>
    /// Adds a card modifier to a card. The modifier being applied should be a mutable instance.
    /// Use the overload with a generic parameter if you don't need to set up the modifier before application.
    /// Otherwise, obtain a mutable instance using the ModelDb.CardModifier extension methods.
    /// </summary>
    public static void AddModifier(CardModel card, CardModifier modifier)
    {
        modifier.ApplyInternal(card);
    }

    /// <summary>
    /// Remove a specific CardModifier instance from a card.
    /// </summary>
    public static bool RemoveModifier(CardModel card, CardModifier modifier)
    {
        return modifier.RemoveInternal(card);
    }

    static CardModifier()
    {
        ModHelper.SubscribeForCombatStateHooks("BaseLibCardModifiers",
            combatState =>
            {
                List<CardModifier> modifiers = [];
                foreach (var piles in combatState.Players.Select(p => p.PlayerCombatState?.AllPiles))
                {
                    if (piles == null) continue;
                    foreach (var pile in piles)
                    {
                        foreach (var card in pile.Cards)
                        {
                            modifiers.AddRange(DirectModifiers(card));
                        }
                    }
                }

                return modifiers;
            });

        DescriptionOverrides.CustomizeDescription += (CardModel card, Creature? target, ref string description) =>
        {
            foreach (var modifier in DirectModifiers(card))
            {
                modifier.ModifyDescription(target, ref description);
            }
        };
        DescriptionOverrides.CustomizeDescriptionPost += (CardModel card, Creature? target, ref string description) =>
        {
            foreach (var modifier in DirectModifiers(card))
            {
                modifier.ModifyDescriptionPost(target, ref description);
            }
        };
    }

    /// <summary>
    /// Mostly unused; overridden just in case.
    /// </summary>
    public override bool ShouldReceiveCombatHooks => Owner?.ShouldReceiveCombatHooks ?? false;
    
    private DynamicVarSet? _dynamicVars;

    /// <summary>
    /// An integer value attached to enchantments that is saved.
    /// </summary>
    public int Amount
    {
        get;
        set
        {
            AssertMutable();
            field = value;
        }
    }

    public CardModel? Owner
    {
        get; 
        private set;
    }
    
    private void ApplyInternal(CardModel card)
    {
        if (card.TryGetModifier(Id, out var modifier))
        {
            if (modifier.ApplyStacked(this))
            {
                return;
            }
        }
        
        DirectModifiers(card).InsertSorted(this);
        Owner = card;
        OnInitialApplication();
    }

    private bool RemoveInternal(CardModel card)
    {
        if (Owner == card)
            Owner = null;
        return DirectModifiers(card).Remove(this);
    }

    /// <summary>
    /// This method is called when a modifier is applied to a card that already has the same modifier.
    /// Return true to cancel the original application.
    /// </summary>
    /// <param name="newApplied">The modifier being applied.</param>
    public virtual bool ApplyStacked(CardModifier newApplied)
    {
        return false;
    }

    /// <summary>
    /// Affects the ordering of card modifiers when they are added to a card.
    /// Lower priority means the card modifier will be inserted before card modifiers of higher priority.
    /// </summary>
    public int Priority { get; set; } = 0;

    public DynamicVarSet DynamicVars
    {
        get
        {
            if (_dynamicVars != null)
                return _dynamicVars;
            
            _dynamicVars = new DynamicVarSet(CanonicalVars);
            _dynamicVars.InitializeWithOwner(this);
            return _dynamicVars;
        }
    }

    /// <summary>
    /// Dynamic variables attached to each instance of the card modifier.
    /// Will automatically be attached to LocStrings retrieved using the <see cref="GetLoc"/> method.
    /// </summary>
    protected virtual IEnumerable<DynamicVar> CanonicalVars => [];

    /// <summary>
    /// Retrieves a <see cref="LocString"/> from a card_modifiers.json table using this modifier's ID.
    /// Adds this modifier's dynamic variables, <see cref="Amount"/>, and attached card's TargetType to the loc.
    /// </summary>
    public virtual LocString GetLoc(string subKey = "description")
    {
        var loc = new LocString("card_modifiers", $"{Id.Entry}.{subKey}");
        loc.Add("Amount", Amount);
        DynamicVars.AddTo(loc);
        loc.Add("TargetType", Owner == null ? "None" : Owner.TargetType.ToString());
        return loc;
    }

    /// <summary>
    /// Modifies a card's description before the game processes it.
    /// Receives target passed into CardModel.GetDescriptionForPile.
    /// </summary>
    public virtual void ModifyDescription(Creature? target, ref string description)
    {
        
    }

    /// <summary>
    /// Modifies a card's description after the game processes it.
    /// Receives target passed into CardModel.GetDescriptionForPile.
    /// </summary>
    public virtual void ModifyDescriptionPost(Creature? target, ref string description)
    {
        
    }

    /// <summary>
    /// Receives the card's list of tips to add to.
    /// </summary>
    public virtual void AddTips(List<IHoverTip> tips)
    {
        
    }

    /// <summary>
    /// Called after the modifier is applied to a card, including when a card is copied.
    /// Due to nature of when this occurs, async combat effects should not occur here.
    /// </summary>
    public virtual void OnInitialApplication()
    {
        
    }

    /// <summary>
    /// Called after the card's OnUpgrade method is called.
    /// </summary>
    public virtual void OnUpgrade()
    {
        
    }
    
    /// <summary>
    /// Called after the card's OnDowngrade method is called.
    /// </summary>
    public virtual void OnDowngrade()
    {
        _dynamicVars = new DynamicVarSet(CanonicalVars);
        _dynamicVars.InitializeWithOwner(Owner!);
    }
    
    /// <summary>
    /// Called whenever the attached card updates its dynamic variable previews to update the modifier's dynamic vars.
    /// Can be overridden if some custom behavior to update display information is needed.
    /// </summary>
    public virtual void UpdateDynamicVarPreview(CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        foreach (var dynVar in DynamicVars.Values)
        {
            dynVar.UpdateCardPreview(Owner!, previewMode, target, runGlobalHooks);
        }
    }

    /// <summary>
    /// Called after the card modifier is cloned and added to a clone of a card.
    /// Called whenever a MutableClone is created.
    /// </summary>
    public virtual void AfterClonedOnCard(CardModel card)
    {
        
    }

    /// <summary>
    /// Functions like an EnchantmentModel's EnchantDamageAdditive.
    /// Add to the amount of damage that this modifier's card does.
    /// This hook runs BEFORE all other damage modification hooks.
    /// NOT YET FULLY FUNCTIONAL.
    /// </summary>
    /// <param name="originalDamage">The amount of damage that would be dealt.</param>
    /// <param name="props">ValueProp for damage.</param>
    /// <returns>Amount of damage to be added.</returns>
    public virtual decimal ModifyBaseDamageAdditive(decimal originalDamage, ValueProp props) => 0;
    
    /// <summary>
    /// Functions like an EnchantmentModel's EnchantDamageMultiplicative.
    /// Multiply the amount of damage that this modifier's card does.
    /// This hook runs BEFORE all other damage modification hooks.
    /// NOT YET FULLY FUNCTIONAL.
    /// </summary>
    /// <param name="originalDamage">The amount of damage that would be dealt.</param>
    /// <param name="props">ValueProp for damage.</param>
    /// <returns>Amount that the damage should be multiplied by.</returns>
    public virtual decimal ModifyBaseDamageMultiplicative(decimal originalDamage, ValueProp props) => 1;
    
    /// <summary>
    /// Functions like an EnchantmentModel's EnchantBlockAdditive.
    /// Add to the amount of block that this modifier's card gains.
    /// This hook runs BEFORE all other block modification hooks.
    /// NOT YET FUNCTIONAL.
    /// </summary>
    /// <param name="originalBlock">The original amount of block that would be gained.</param>
    /// <returns>The amount to add to the block gain.</returns>
    public virtual decimal ModifyBaseBlockAdditive(decimal originalBlock) => 0M;

    /// <summary>
    /// Functions like an EnchantmentModel's EnchantBlockMultiplicative.
    /// Modify the amount of block that this modifier's card gains.
    /// This hook runs BEFORE all other block modification hooks.
    /// NOT YET FUNCTIONAL.
    /// </summary>
    /// <param name="originalBlock">The original amount of block that would be gained.</param>
    /// <returns>The amount to multiply the block gain by.</returns>
    public virtual decimal ModifyBaseBlockMultiplicative(decimal originalBlock) => 1M;
    
    /// <summary>
    /// Called after the card's OnPlay method is called. Occurs before normal AfterCardPlayed hook.
    /// </summary>
    public virtual Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    int IComparable<CardModifier>.CompareTo(CardModifier? other)
    {
        return Priority.CompareTo(other?.Priority ?? 0);
    }

    /// Called when a mutable clone is created, after the standard MemberwiseClone creates the instance.
    protected override void DeepCloneFields()
    {
        _dynamicVars = DynamicVars.Clone(this);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.UpgradeInternal))]
static class UpgradeModifiers
{
    [HarmonyPostfix]
    static void UpgradeModifiersOnCard(CardModel __instance)
    {
        foreach (var modifier in CardModifier.Modifiers(__instance))
        {
            modifier.OnUpgrade();
            modifier.DynamicVars.RecalculateForUpgradeOrEnchant();
        }
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.DowngradeInternal))]
static class DowngradeModifiers
{
    [HarmonyTranspiler]
    static List<CodeInstruction> DowngradeModifiersOnCard(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new CallMatcher(typeof(CardModel).DeclaredMethod("AfterDowngraded")))
            .InsertBeforeMatch([
                CodeInstruction.Call(typeof(DowngradeModifiers), nameof(DowngradeModifiers.OnDowngrade))
            ]);
    }

    static CardModel OnDowngrade(CardModel card)
    {
        foreach (var modifier in CardModifier.Modifiers(card))
        {
            modifier.OnDowngrade();
            modifier.DynamicVars.RecalculateForUpgradeOrEnchant();
        }

        return card;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal))]
static class FinalizeModifierUpgrade
{
    [HarmonyPostfix]
    static void FinalizeModifiersOnCard(CardModel __instance)
    {
        foreach (var modifier in CardModifier.Modifiers(__instance))
        {
            modifier.DynamicVars.FinalizeUpgrade();
        }
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.UpdateDynamicVarPreview))]
static class UpdateModifierPreview
{
    [HarmonyTranspiler]
    static List<CodeInstruction> UpdateModifierVars(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldarg_1()
                .ldarg_2()
                .ldloc_any()
                .callvirt(typeof(DynamicVar), nameof(DynamicVar.UpdateCardPreview))
            )
            .CopyMatch(out var match)
            .MatchEnd()
            .Step(-1)
            .Insert([
                ..match.SkipLast(1),
                CodeInstruction.Call(typeof(UpdateModifierPreview),
                    nameof(UpdateModifierPreview.UpdateModifierVarPreview))
            ]);
    }

    static void UpdateModifierVarPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        foreach (var modifier in CardModifier.Modifiers(card))
        {
            modifier.UpdateDynamicVarPreview(previewMode, target, runGlobalHooks);
        }
    }
}