namespace BaseLib.Config;

public static class ModConfigRegistry
{
    private static readonly Dictionary<string, ModConfig> ModConfigs = new();

    public static void Register(string modId, ModConfig config)
    {
        if (!config.HasSettings() && !config.VisibleInModList()) return;
        config.ModId = modId;
        ModConfigs[modId] = config;
        
        BaseLibMain.Logger.Info($"Registered config for mod {modId}");
    }

    public static ModConfig? Get(string? modId)
    {
        if (modId == null) return null;
        return ModConfigs.GetValueOrDefault(modId);
    }

    public static T? Get<T>() where T : ModConfig
    {
        return ModConfigs.Values.OfType<T>().FirstOrDefault();
    }

    public static List<ModConfig> GetAll() =>
        ModConfigs.Values
        .OrderBy(m => m.ModId)
        .ToList();
}