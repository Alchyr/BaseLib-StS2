using System.Reflection;
using BaseLib.Config;
using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Utils;

/// <summary>
/// Maps modded content back to the mod that added it, by the assembly its type was defined in,
/// so tooltips can show "which mod is this from?".
/// </summary>
public static class WhatMod
{
    private static readonly FieldInfo? AssemblyField = AccessTools.DeclaredField(typeof(Mod), "assembly");
    private static readonly FieldInfo? AssembliesField = AccessTools.DeclaredField(typeof(Mod), "assemblies");
    
    private static IReadOnlyList<Mod> _loadedMods = [];
    private static bool _built;

    private static readonly Assembly BasegameAssembly = typeof(Really).Assembly;
    private static readonly Dictionary<Mod, List<Assembly>> AssembliesByMod = [];
    private static readonly Dictionary<Assembly, Mod> ModByAssembly = [];
    private static readonly Dictionary<Type, Mod?> ModByType = [];

    public static List<Assembly> AssembliesForMod(Mod mod) => AssembliesByMod.GetValueOrDefault(mod, []);

    internal static void BuildAfterInit()
    {
        if (_built) return;
        _built = true;
        
        _loadedMods = ModManager.GetLoadedMods().ToList();
        foreach (var mod in _loadedMods)
        {
            CheckAssembly(mod);
        }
    }

    private static void CheckAssembly(Mod mod)
    {
        List<Assembly>? modAssemblies = null;
        if (AssemblyField != null)
        {
            var assembly = (Assembly?) AssemblyField.GetValue(mod);
            if (assembly != null)
            {
                modAssemblies = [assembly];
            }
        }
        else if (AssembliesField != null)
        {
            var assemblies = (List<Assembly>?) AssembliesField.GetValue(mod);
            if (assemblies != null)
            {
                AssembliesByMod[mod] = [..assemblies];
            }
        }
        else
        {
            BaseLibMain.Logger.Warn("Unable to find assemblies tied to mods.");
        }

        if (modAssemblies == null) return;
        
        AssembliesByMod[mod] = modAssemblies;
        foreach (var modAssembly in modAssemblies)
        {
            ModByAssembly[modAssembly] = mod;
        }
    }


    /// <summary>
    /// The mod that defined <paramref name="type"/>, or null if it belongs to the base game or mod map has not been built.
    /// </summary>
    public static Mod? FindMod(Type type)
    {
        if (!_built) return null;
        if (type.Assembly.Equals(BasegameAssembly)) return null;
        if (ModByType.TryGetValue(type, out var cached)) return cached;

        if (!ModByAssembly.TryGetValue(type.Assembly, out var mod))
        {
            // Assembly not registered directly (e.g. bundled); fall back to matching the content's
            // root namespace against a loaded mod's id.
            var root = type.GetRootNamespace();
            mod = _loadedMods.FirstOrDefault(m =>
                m.manifest?.id != null && m.manifest.id.Equals(root, StringComparison.OrdinalIgnoreCase));
        }

        ModByType[type] = mod;
        return mod;
    }

    /// <summary>
    /// Display name of the mod that defined type T, or null if it is base-game content.
    /// Matches the installed-mods list: the manifest name, with the id in parentheses when it differs.
    /// </summary>
    public static string? FindModName<T>()
    {
        return FindModName(typeof(T));
    }
    
    /// <summary>
    /// Display name of the mod that defined <paramref name="type"/>, or null if it is base-game content.
    /// Matches the installed-mods list: the manifest name, with the id in parentheses when it differs.
    /// </summary>
    public static string? FindModName(Type type)
    {
        if (type.Assembly.Equals(BasegameAssembly)) return null;

        var mod = FindMod(type);
        var name = mod?.manifest?.name;
        var id = mod?.manifest?.id ??  type.GetRootNamespace();

        if (string.IsNullOrWhiteSpace(name))
            return id;
        if (string.IsNullOrWhiteSpace(id) || id.Equals(name, StringComparison.OrdinalIgnoreCase))
            return name;
        return BaseLibConfig.IncludeModId ? $"{name} ({id})" : name;
    }
}
