using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Utils.Patching;

/// <summary>
/// Utility class for splitting up large loc tables between multiple files.
/// </summary>
public static class LocAliasManager
{
    private static List<LocAliasInfo> LocAliases { get; } = [];

    /// <summary>
    /// Instruct the game to append these loc strings to another loc table.
    /// </summary>
    /// <param name="modId">Your mod ID.</param>
    /// <param name="table">The table to append.</param>
    /// <param name="aliases">The names of any json files you want to append to the table.</param>
    public static void Register(string modId, string table, params IEnumerable<string> aliases)
    {
        if (!table.EndsWith(".json"))
        {
            table = $"{table}.json";
        }

        DirAccess directory = DirAccess.Open(Path.Join($"res://{modId}", "localization"));

        string[] languages = directory.GetDirectories();

        string[] aliasArray = aliases as string[] ?? [.. aliases];

        foreach (string language in languages)
        {
            string basePath = string.Join('/', "res://localization", language, table);
            string[] aliasPaths =
            [
                .. aliasArray
                    .Select(s =>
                    {
                        if (!s.EndsWith(".json"))
                        {
                            s = $"{s}.json";
                        }

                        return string.Join('/', $"res://{modId}", "localization", language, s);
                    })
                    .Where(s => ResourceLoader.Exists(s))
            ];

            LocAliasInfo? existing = LocAliases.SingleOrDefault(lai => lai.BasePath == basePath);

            if (existing is not null)
            {
                existing.AliasPaths.AddRange(aliasPaths);
            }
            else
            {
                LocAliases.Add(new LocAliasInfo(basePath, [.. aliasPaths]));
            }

            BaseLibMain.Logger.Debug($"Added aliases for {modId}: {string.Join(", ", aliasPaths)}");
        }
    }

    internal static IEnumerable<string> MergeAliasesIntoTable(IEnumerable<string> existing, string language, string file)
    {
        foreach (string original in existing)
        {
            yield return original;
        }

        string path = string.Join('/', "res://localization", language, file);

        foreach (LocAliasInfo info in LocAliases.Where(lai => lai.BasePath == path))
        {
            foreach (string alias in info.AliasPaths)
            {
                yield return alias;
            }
        }
    }
}

internal record LocAliasInfo(string BasePath, List<string> AliasPaths);