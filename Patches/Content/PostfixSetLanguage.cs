using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace BaseLib.Patches.Content
{
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
    public static class PostfixSetLanguage
    {
        private static readonly FieldInfo TablesField =
            AccessTools.Field(typeof(LocManager), "_tables");

        [HarmonyPostfix]
        private static void Postfix(LocManager __instance, string language)
        {
            if (TablesField.GetValue(__instance) is not Dictionary<string, LocTable> tables)
                return;

            var engDefinitions = LoadDefinitions("eng");
            var langDefinitions = language == "eng"
                ? []
                : LoadDefinitions(language);

            var mergedByTable = new Dictionary<string, Dictionary<string, string>>();

            foreach (var ownerType in engDefinitions.Keys.Union(langDefinitions.Keys))
            {
                engDefinitions.TryGetValue(ownerType, out var engDef);
                langDefinitions.TryGetValue(ownerType, out var langDef);

                var tableName = langDef?.Table
                    ?? engDef?.Table
                    ?? LocalizationScanner.GetLocTable(ownerType);

                var entry = ModelDb.GetId(ownerType).Entry;

                var mergedEntries = new Dictionary<string, string>();

                if (engDef is not null)
                {
                    foreach (var kv in engDef.Entries)
                        mergedEntries[kv.Key] = kv.Value;
                }

                if (langDef is not null)
                {
                    foreach (var kv in langDef.Entries)
                        mergedEntries[kv.Key] = kv.Value;
                }

                if (!mergedByTable.TryGetValue(tableName, out var tableEntries))
                {
                    tableEntries = [];
                    mergedByTable[tableName] = tableEntries;
                }

                foreach (var kv in mergedEntries)
                    tableEntries[$"{entry}.{kv.Key}"] = kv.Value;
            }

            foreach (var (tableName, entries) in mergedByTable)
            {
                if (!tables.TryGetValue(tableName, out var table))
                {
                    table = new LocTable(tableName, []);
                    tables[tableName] = table;
                }

                table.MergeWith(entries);
            }
        }

        private static Dictionary<Type, LocalizationDefinition> LoadDefinitions(string language)
        {
            var result = new Dictionary<Type, LocalizationDefinition>();

            foreach (var (ownerType, method) in LocalizationScanner.FindProviders(language))
            {
                try
                {
                    if (method.Invoke(null, null) is not LocalizationDefinition entity)
                        continue;

                    result[ownerType] = entity;
                }
                catch
                {

                }
            }

            return result;
        }
    }
}
