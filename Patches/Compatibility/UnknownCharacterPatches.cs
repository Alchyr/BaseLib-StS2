using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace BaseLib.Patches.Compatibility;

public class UnknownCharacterPatches
{
    //Attempting to load run of unknown character
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.HasRunSave), MethodType.Getter)]
    static class IgnoreUnknownRun
    {
        [HarmonyPostfix]
        private static void SkipUnknownCharacter(SaveManager __instance, ref bool __result)
        {
            if (__result)
            {
                var save = __instance.LoadRunSave();
            
                if (!save.Success || save.SaveData == null) return;
            
                foreach (var player in save.SaveData.Players)
                {
                    if (player.CharacterId == null || ModelDb.GetByIdOrNull<CharacterModel>(player.CharacterId) == null)
                    {
                        BaseLibMain.Logger.Info($"Ignoring run with unknown character {player.CharacterId}");
                        __result = false;
                        return;
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.HasMultiplayerRunSave), MethodType.Getter)]
    static class IgnoreUnknownCoopRun
    {
        [HarmonyPostfix]
        private static void SkipUnknownCharacter(SaveManager __instance, ref bool __result)
        {
            if (__result)
            {
                PlatformType platformType = (SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")) ? PlatformType.Steam : PlatformType.None;
                var save = __instance.LoadAndCanonicalizeMultiplayerRunSave(PlatformUtil.GetLocalPlayerId(platformType));
            
                if (!save.Success || save.SaveData == null) return;
            
                foreach (var player in save.SaveData.Players)
                {
                    if (player.CharacterId == null || ModelDb.GetByIdOrNull<CharacterModel>(player.CharacterId) == null)
                    {
                        BaseLibMain.Logger.Info($"Ignoring co-op run with unknown character {player.CharacterId}");
                        __result = false;
                        return;
                    }
                }
            }
        }
    }
    
   
    
}