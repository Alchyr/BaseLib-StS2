using Godot;

namespace BaseLib.Config;

public static class Registry
{
    private static readonly Dictionary<string, Control> ModConfigs = new();

    public static void Register(string modId, Control configPanel)
    {
        ModConfigs[modId] = configPanel;
    }

    public static Control? Get(string modId)
    {
        return ModConfigs.GetValueOrDefault(modId);
    }
}
