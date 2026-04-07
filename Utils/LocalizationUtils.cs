using BaseLib.CustomAttribute;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace BaseLib.Utils
{
    public class LocalizationDefinition
    {
        public string? Table { get; set; }

        public virtual Dictionary<string, string> Entries { get; init; } = [];
    }

    public class LocalizationScanner
    {
        private static readonly Dictionary<string, IReadOnlyList<(Type ownerType, MethodInfo method)>> _cache = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<(Type ownerType, MethodInfo method)> FindProviders(string language)
        {
            language = language.ToLowerInvariant();

            if (_cache.TryGetValue(language, out var cached))
                return cached;

            List<(Type ownerType, MethodInfo method)> result = [];

            foreach (var type in ReflectionHelper.GetSubtypesInMods<AbstractModel>())
            {
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<LocalizationInfoAttribute>();
                    if (attr == null)
                        continue;

                    if (!string.Equals(attr.Language, language, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!typeof(LocalizationDefinition).IsAssignableFrom(method.ReturnType))
                        continue;

                    if (method.GetParameters().Length != 0)
                        continue;

                    result.Add((type, method));
                }
            }

            _cache[language] = result;
            return result;
        }

        public static string GetLocTable(Type type)
        {
            if (typeof(AncientEventModel).IsAssignableFrom(type))
                return "ancients";

            var categoryType = ModelDb.GetCategoryType(type);

            if (categoryType == typeof(CardModel)) return "cards";
            if (categoryType == typeof(CharacterModel)) return "characters";
            if (categoryType == typeof(RelicModel)) return "relics";
            if (categoryType == typeof(PowerModel)) return "powers";
            if (categoryType == typeof(PotionModel)) return "potions";
            if (categoryType == typeof(OrbModel)) return "orbs";
            if (categoryType == typeof(MonsterModel)) return "monsters";
            if (categoryType == typeof(EncounterModel)) return "encounters";
            if (categoryType == typeof(ModifierModel)) return "modifiers";
            if (categoryType == typeof(AfflictionModel)) return "afflictions";
            if (categoryType == typeof(EnchantmentModel)) return "enchantments";
            if (categoryType == typeof(ActModel)) return "acts";

            if (categoryType == typeof(EventModel))
                throw new InvalidOperationException($"Event type {type.FullName} must set Table explicitly in I18NDefinition, because EventModel can override LocTable.");

            throw new InvalidOperationException($"No loc table mapping for type {type.FullName}");
        }
    }

    public static class LocalizationCodes
    {
        public const string Eng = "eng";
        public const string Deu = "deu";
        public const string Spa = "spa";
        public const string Esp = "esp";
        public const string Fra = "fra";
        public const string Ita = "ita";
        public const string Jpn = "jpn";
        public const string Kor = "kor";
        public const string Pol = "pol";
        public const string Ptb = "ptb";
        public const string Rus = "rus";
        public const string Tha = "tha";
        public const string Tur = "tur";
        public const string Zhs = "zhs";
        public const string Zht = "zht";
    }
}
