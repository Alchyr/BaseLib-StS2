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
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// A model that is attached to a card to modify its behavior.
/// Receives all combat hooks, and is capable of modifying the card's description.
/// More features to be added in the future.
/// TODO - base value modification like enchants/afflictions
/// </summary>
public abstract class CardModifier : AbstractModel, IComparable<CardModifier>
{
    /// <summary>
    /// Obtains a new instance of a CardModifier from ModelDb using <see cref="ModelDbExtensions.CardModifier"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T Get<T>() where T : CardModifier
    {
        return ModelDb.CardModifier<T>();
    }
    
    public static void RegisterSave()
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

    public sealed class ModifierSave : IPacketSerializable
    {
        public static ModifierSave FromModifier(CardModifier modifier)
        {
            var save = new ModifierSave()
            {
                Id = modifier.Id
            };
            modifier.StoreSaveData(save);
            return save;
        }

        public CardModifier ToRealMod(CardModel owner)
        {
            var mod = (CardModifier) ModelDb.GetById<CardModifier>(Id!).MutableClone();
            mod.Owner = owner;
            mod.LoadSaveData(this);
            return mod;
        }
        
        public ModelId? Id { get; set; }
        public Dictionary<string, int> IntProperties { get; set; } = [];
        public Dictionary<string, string> AdditionalProperties { get; set; } = [];

        /// <inheritdoc />
        public void Serialize(PacketWriter writer)
        {
            writer.WriteModelEntry(Id!);
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
    /// </summary>
    public virtual void StoreSaveData(ModifierSave save)
    {
        
    }
    /// <summary>
    /// Loads saved values into a new instance of this modifier.
    /// </summary>
    public virtual void LoadSaveData(ModifierSave save)
    {
        
    }

    private static void LoadModifierSaves(CardModel card, List<ModifierSave>? modifiers)
    {
        _modifiers[card] = modifiers?.Select(mod => mod.ToRealMod(card)).ToList() ?? [];
    }
    
    
    private static readonly SpireField<CardModel, List<CardModifier>> _modifiers = new(() => []);

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
    public static void AddModifier(CardModel card, CardModifier modifier)
    {
        modifier.ApplyInternal(card);
    }

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
            
            if (Owner == null)
                throw new InvalidOperationException("Attempted to access a card modifier's dynamic vars before it has an owner");
            
            _dynamicVars = new DynamicVarSet(CanonicalVars);
            _dynamicVars.InitializeWithOwner(Owner);
            return _dynamicVars;
        }
    }

    /// <summary>
    /// Dynamic variables attached to each instance of the card modifier.
    /// Will automatically be attached to LocStrings retrieved using the <see cref="GetLoc"/> method.
    /// </summary>
    protected virtual IEnumerable<DynamicVar> CanonicalVars => [];

    /// <summary>
    /// Retrieves a <see cref="LocString"/> from a card_modifiers.json table using this modifier's ID
    /// and adds this modifier's dynamic variables to it.
    /// </summary>
    public virtual LocString GetLoc(string subKey = "description")
    {
        var loc = new LocString("card_modifiers", $"{Id.Entry}.{subKey}");
        DynamicVars.AddTo(loc);
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
    /// Called after the card's OnPlay method is called. Occurs before normal AfterCardPlayed hook.
    /// </summary>
    public virtual Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        return Task.CompletedTask;
    }

    int IComparable<CardModifier>.CompareTo(CardModifier? other)
    {
        return Priority.CompareTo(other?.Priority ?? 0);
    }
}

[HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
static class CloneModifiers {
    [HarmonyPostfix]
    static void ModifyResult(AbstractModel __instance, AbstractModel __result)
    {
        if (__instance is CardModel card && __result is CardModel resultCard)
        {
            foreach (var modifier in CardModifier.Modifiers(card))
            {
                var cloneModifier = (CardModifier) modifier.MutableClone();
                resultCard.AddModifier(cloneModifier);
                cloneModifier.AfterClonedOnCard(resultCard);
            }
        }
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