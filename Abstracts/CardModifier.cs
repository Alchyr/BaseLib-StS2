using System.Collections.ObjectModel;
using BaseLib.Extensions;
using BaseLib.Patches.Localization;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// A model that is attached to a card to modify its behavior.
/// Receives all combat hooks, and is capable of modifying the card's description.
/// More features to be added in the future.
/// </summary>
public abstract class CardModifier : AbstractModel
{
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
    /// Utilizes the card modifier's Priority to determine where to insert the card modifier in the list of modifiers.
    /// </summary>
    public static void AddModifier(CardModel card, CardModifier modifier)
    {
        var directModifiers = DirectModifiers(card);
        var insertedMod = false;
        for (int i = 0; i < directModifiers.Count; i++)
        {
            if (modifier.Priority < directModifiers[i].Priority)
            {
                directModifiers.Insert(i, modifier);
                insertedMod = true;
                break;
            }
        }

        if (!insertedMod)
        {
            directModifiers.Add(modifier);
        }
        modifier.Owner = card;
    }

    public static bool RemoveModifier(CardModel card, CardModifier modifier)
    {
        if (modifier.Owner == card)
            modifier.Owner = null;
        return DirectModifiers(card).Remove(modifier);
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
    public override bool ShouldReceiveCombatHooks => true;
    
    public CardModel? Owner
    {
        get; 
        private set;
    }

    /// <summary>
    /// Affects the ordering of card modifiers when they are added to a card.
    /// Lower priority means the card modifier will be inserted before card modifiers of higher priority.
    /// </summary>
    public int Priority
    {
        get;
        set;
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
    /// Called after the card modifier is cloned and added to a clone of a card.
    /// </summary>
    public virtual void AfterClonedOnCard(CardModel card)
    {
        
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