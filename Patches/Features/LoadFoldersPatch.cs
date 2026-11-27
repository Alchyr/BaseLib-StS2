using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Platform;

namespace BaseLib.Patches.Features;

[HarmonyPatch(typeof(ModManager), "TryLoadMod")]
public static class LoadFoldersPatch
{
    [HarmonyPrefix]
    private static void Prefix(Mod mod)
    {
        var loadFoldersPath = Path.Combine(mod.path, "loadFolders");
        if (!File.Exists(loadFoldersPath))
            return;

        try
        {
            var json = File.ReadAllText(loadFoldersPath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (map == null)
            {
                BaseLibMain.Logger.Warn($"[LoadFolders] {mod.manifest?.id}: loadFolders deserialized to null");
                return;
            }

            var branch = PlatformUtil.GetPlatformBranch();
            var branchName = branch.ToName();

            if (!map.TryGetValue(branchName, out var subdir))
            {
                BaseLibMain.Logger
                    .Info($"[LoadFolders] {mod.manifest?.id}: no entry for branch '{branchName}', loading from root");
                return;
            }

            subdir = subdir.TrimStart('/', '\\');
            var resolvedPath = Path.Combine(mod.path, subdir);

            BaseLibMain.Logger
                .Info($"[LoadFolders] {mod.manifest?.id}: loading folder '{resolvedPath}' for branch '{branchName}'");

            mod.path = resolvedPath;
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Warn($"[LoadFolders] {mod.manifest?.id}: failed to process loadFolders: {e}");
        }
    }
}