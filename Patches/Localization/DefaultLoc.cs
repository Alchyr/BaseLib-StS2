using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(ModManager), nameof(ModManager.GetModdedLocTables))]
public static class DefaultLoc
{
    //Languages supported by basegame other than english
    private static readonly string[] LanguagePreference = [
        "zhs",
        "jpn",
        "deu",
        "kor",
        "rus",
        "spa",
        "esp",
        "fra",
        "tur",
        "ita",
        "pol",
        "ptb",
        "tha"
    ];

    private static readonly Dictionary<string, string> _defaultLoc = [];
    
    /// <summary>
    /// Call this in your mod's initializer to load this type of localization first when loading any other language.
    /// This will result in using this language's localization for anything missing from the current set language.
    /// Note, basegame will always load english localization first, so if using english this does nothing.
    /// </summary>
    public static void Set(string modId, string defaultLoc)
    {
        if (defaultLoc == "eng")
        {
            BaseLibMain.Logger.Warn($"Mod {modId} sets English as default loc; this does nothing, as game already will use English as default");
            return;
        }
        if (_defaultLoc.Remove(modId, out var old))
        {
            BaseLibMain.Logger.Warn($"Default localization is set multiple times for {modId}; previous value {old}, new value {defaultLoc}");
        }
        _defaultLoc.Add(modId, defaultLoc);
    }

    [HarmonyPostfix]
    static void LoadDefaultTablesFirst(string language, string file, ref IEnumerable<string> __result)
    {
        List<string> defaultLocFirst = [];
        
        foreach (var mod in ModManager.GetLoadedMods())
        {
            if (mod.manifest?.id == null) continue;
            if (!_defaultLoc.TryGetValue(mod.manifest.id, out var defaultLoc))
            {
                //Check for loc files for mod if default path doesn't exist
                var origPath = $"res://{mod.manifest.id}/localization/{language}/{file}";
                if (ResourceLoader.Exists(origPath)) continue;
                BaseLibMain.Logger.VeryDebug($"\"{origPath}\" not found and DefaultLoc not set; looking for existing loc file");

                //Language directories live directly under localization/; ListDirectory marks subdirectories with a trailing slash.
                HashSet<string> existingLang = [];
                var localizationRoot = $"res://{mod.manifest.id}/localization";
                if (DirAccess.DirExistsAbsolute(localizationRoot))
                {
                    foreach (var entry in ResourceLoader.ListDirectory(localizationRoot))
                    {
                        if (entry == null || !entry.EndsWith('/')) continue;

                        var lang = entry.TrimEnd('/');
                        if (lang.Length == 0) continue;
                        existingLang.Add(lang);
                    }
                }
                
                if (existingLang.Count == 0) continue;
                if (existingLang.Contains("eng")) continue;

                foreach (var preferredLang in LanguagePreference)
                {
                    if (!existingLang.Contains(preferredLang)) continue;
                    
                    defaultLoc = preferredLang;
                    break;
                }

                if (defaultLoc == null) continue;
            }
            if (defaultLoc == language) continue;
            
            var path = $"res://{mod.manifest.id}/localization/{defaultLoc}/{file}";
            if (ResourceLoader.Exists(path))
            {
                defaultLocFirst.Add(path);
            }
        }

        BaseLibMain.Logger.Debug($"Found {defaultLocFirst.Count} default loc files; [{string.Join(", ", defaultLocFirst)}]");
        defaultLocFirst.AddRange(__result);
        __result = defaultLocFirst;
    }
}