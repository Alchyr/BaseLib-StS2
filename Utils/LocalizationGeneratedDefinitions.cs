using System.Collections.Concurrent;
using System.Reflection;

namespace BaseLib.Utils
{
    [AttributeUsage(AttributeTargets.Property)]
    public class KeyNameAttribute(string name) : Attribute
    {
        public string Name { get; } = name;
    }

    public abstract class I18NDefinitionBase : LocalizationDefinition
    {
        private Dictionary<string, string>? _entries;

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _typeProperties = new();

        public override Dictionary<string, string> Entries
        {
            get
            {
                if (_entries != null) return _entries;

                var dict = new Dictionary<string, string>();
                var type = GetType();

                var properties = _typeProperties.GetOrAdd(type, t =>
                    [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != nameof(Entries))]
                );

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(this) as string;
                    if (string.IsNullOrEmpty(value)) continue;

                    var keyNameAttr = prop.GetCustomAttribute<KeyNameAttribute>();
                    var key = keyNameAttr != null
                        ? keyNameAttr.Name
                        : char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

                    dict[key] = value;
                }

                _entries = dict;
                return dict;
            }
        }
    }

    public class CardI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public string? SelectionScreenPrompt { get; init; }
    }

    public class RelicI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Flavor { get; init; }
        public required string Description { get; init; }
        public required string SelectionScreenPrompt { get; init; }
        public string? EventDescription { get; init; }
        public string? AdditionalRestSiteHealText { get; init; }
    }

    public class PotionI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string SelectionScreenPrompt { get; init; }
    }

    public class PowerI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string SelectionScreenPrompt { get; init; }
        public string? SmartDescription { get; init; }
        public string? RemoteDescription { get; init; }
    }

    public class OrbI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public string? SmartDescription { get; init; }
    }

    public class EnchantmentI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string ExtraCardText { get; init; }
    }

    public class AfflictionI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string ExtraCardText { get; init; }
    }

    public class EncounterI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Loss { get; init; }
        public string? CustomRewardDescription { get; init; }
    }

    public class CharacterI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string TitleObject { get; init; }
        public required string PronounObject { get; init; }
        public required string PossessiveAdjective { get; init; }
        public required string PronounPossessive { get; init; }
        public required string PronounSubject { get; init; }
        public required string CardsModifierTitle { get; init; }
        public required string CardsModifierDescription { get; init; }
        public required string EventDeathPrevention { get; init; }
        public required string UnlockText { get; init; }
        public string? AromaPrinciple { get; init; }
        public string? GoldMonologue { get; init; }

        [KeyName("alive.endTurnPing")]
        public string? AliveEndTurnPing { get; init; }

        [KeyName("dead.endTurnPing")]
        public string? DeadEndTurnPing { get; init; }
    }

    public class ModifierI18N : I18NDefinitionBase
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public string? AdditionalRestSiteHealText { get; init; }
    }

    public class MonsterI18N : I18NDefinitionBase
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
    }
}
