using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Extensions;
using BaseLib.Patches;
using BaseLib.Patches.Content;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;

namespace BaseLib.Abstracts;

/// <summary>
/// Despite the name these are not stored in <see cref="ModelDb"/>
/// </summary>
public abstract class CustomEpochModel : EpochModel, ICustomModel
{
    
    // TODO: Prefixing Mod Id.
    // No ModelDb entry so the ModelDb.GetEntry prefix patch won't work
    /// <summary>
    /// If you override this, add your mod prefix to it!
    /// </summary>
    public override string Id => $"{GetType().GetPrefix()}{StringHelper.Slugify(GetType().Name).ToUpperInvariant()}"; 

    /// <summary>
    /// The large image displayed when clicking the Epoch.
    /// </summary>
    public abstract string CustomRealPortraitPath { get; } // png
    
    /// <summary>
    /// The small image displayed on the Timeline.
    /// </summary>
    public abstract string CustomPackedPortraitPath { get; } // tres
    
    /// <summary>
    /// Override only if this Epoch is part of a <see cref="CustomEpochEra">Custom Era</see>.
    /// </summary>
    public virtual CustomEpochEra? CustomEra => null;

    /// <summary>
    /// Set to true if your epoch unlocks cards so it can be added to <seealso cref="SaveManager.GetCardUnlockEpochIds"/>
    /// </summary>
    protected virtual bool UnlocksCards => false;

    [HarmonyPatch]
    private static class PropertyRedirections
    {
        [HarmonyPatch(typeof(EpochModel), "RealPortraitPath", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool CustomEpochRealPortraitPath(EpochModel __instance, ref string? __result)
        {
            if (__instance is not CustomEpochModel customEpoch) return true;
            __result = customEpoch.CustomRealPortraitPath;
            return false;
        }
        
        [HarmonyPatch(typeof(EpochModel), "PackedPortraitPath", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool CustomEpochPackedPortraitPath(EpochModel __instance, ref string? __result)
        {
            if (__instance is not CustomEpochModel customEpoch) return true;
            __result = customEpoch.CustomPackedPortraitPath;
            return false;
        }
    }
    
    /// <summary>
    /// Should only be called once from <seealso cref="PostModInitPatch"/>
    /// </summary>
    internal static void FillEpochDictionaries(List<CustomEpochModel> models)
    {
        BaseLibMain.Logger.Info("Inserting CustomEpochs into dictionaries");
        var epochModelType = typeof(EpochModel);
        var epochTypeDictionary = (AccessTools.Field(epochModelType, "_epochTypeDictionary").GetValue(null) as Dictionary<string, Type>)!;
        var typeToIdDictionary = (AccessTools.Field(epochModelType, "_typeToIdDictionary").GetValue(null) as Dictionary<Type, string>)!;
        foreach (var customEpochModel in models)
        {
            CustomEpochHandler.InsertIntoAllEpochs(customEpochModel);
            var type = customEpochModel.GetType();
            BaseLibMain.Logger.Debug($"CustomEpoch Type: {type.Name} | Id: {customEpochModel.Id} ");
            CustomContentDictionary.AddEpoch(customEpochModel);
            epochTypeDictionary[customEpochModel.Id] = type;
            typeToIdDictionary[type] = customEpochModel.Id;
        }
    }


    [HarmonyPatch]
    public static class CustomEpochPatches
    {
        // We manipulate EraPositions only in the live Timeline, not anywhere where it would be permanent.
        [HarmonyPatch]
        public static class DuplicateEraPositionHandler
        {
            private static readonly SpireField<NEpochSlot, bool> OverwrittenEraPositions = new(() => true);
            
            [HarmonyTranspiler]
            [HarmonyPatch(typeof(NEraColumn), nameof(NEraColumn.AddSlot))]
            private static List<CodeInstruction> AdjustNEpochSlotEraPosition(IEnumerable<CodeInstruction> instructions)
            {
                var enforceUniqueEraPosition = typeof(DuplicateEraPositionHandler).Method(nameof(EnforceUniqueEraPosition));
                var matcher = new CodeMatcher(instructions)
                            .MatchStartForward([
                                        new CodeMatch(OpCodes.Stloc_0),
                            ])
                            .ThrowIfInvalid("Could not find position to insert EraPosition duplicate fix")
                            .InsertAfter([
                                        new CodeInstruction(OpCodes.Ldarg_0),
                                        new CodeInstruction(OpCodes.Ldloc_0),
                                        new CodeInstruction(OpCodes.Call, enforceUniqueEraPosition),
                            ]);
                return matcher.InstructionEnumeration().ToList();
            }
    
            private static void EnforceUniqueEraPosition(NEraColumn nEraColumn, NEpochSlot nEpochSlot)
            {
                var allCurrentNEpochSlots = nEraColumn.GetChildren().OfType<NEpochSlot>().ToList();
                var oldEraPosition = nEpochSlot.eraPosition;
                while (allCurrentNEpochSlots.Any(o => o.eraPosition == nEpochSlot.eraPosition))
                    nEpochSlot.eraPosition++;
                if (oldEraPosition == nEpochSlot.eraPosition) return;
                OverwrittenEraPositions.Set(nEpochSlot, true);
                BaseLibMain.Logger.Info($"Moved EpochSlot position for Epoch {nEpochSlot.model.Id} from {oldEraPosition} to {nEpochSlot.eraPosition}");
            }
            
            
            // The game checks for a match with the position, however we manipulated (some of) them above.
            // Replaces the ".position == .position" check with an ".Id == .Id" check.
            [HarmonyPatch(typeof(NTimelineScreen), nameof(NTimelineScreen.InitScreen), MethodType.Async)]
            [HarmonyTranspiler]
            private static List<CodeInstruction> ResolveSetStateForNewEraPositions(IEnumerable<CodeInstruction> instructions)
            {
                var getActualEraPosition = typeof(DuplicateEraPositionHandler).Method(nameof(GetActualEraPosition));
                 var matcher = new CodeMatcher(instructions)
                             .MatchStartForward([
                                         new CodeMatch(OpCodes.Isinst),
                             ])
                             .ThrowIfInvalid("Could not find correct position to begin replacing EraPosition comparision check")
                             .Advance(2);
                 var nEpochSlot = matcher.Instruction.operand;
                 matcher.Advance(1);
                 var target = (Label)matcher.Instruction.operand;
                 matcher.Advance(1)
                             .RemoveInstructions(2);
                 var epochModel = matcher.Instruction.operand;
                 matcher.RemoveInstructions(3)
                             .Insert([
                                         new CodeInstruction(OpCodes.Ldloc, nEpochSlot),
                                         new CodeInstruction(OpCodes.Ldloc, epochModel),
                                         new CodeInstruction(OpCodes.Call, getActualEraPosition),
                                         new CodeInstruction(OpCodes.Brfalse_S, target),
                             ]);
                 return matcher.InstructionEnumeration().ToList();
            }
            private static bool GetActualEraPosition(NEpochSlot nEpochSlot, EpochModel epochModel)
            {
                return nEpochSlot.model.Id == epochModel.Id;
            }
        }
        
        
        [HarmonyPatch(typeof(SaveManager), "GetCardUnlockEpochIds")]
        [HarmonyPostfix]
        private static void InsertCardUnlockCustomEpochs(ref string[] __result)
        {
            __result = [.. __result, .. CustomContentDictionary.CustomEpochs.Where(e => e.UnlocksCards).Select(e => e.Id)];
        }
    
        [HarmonyPatch(typeof(UnlockState), nameof(UnlockState.Characters), MethodType.Getter)]
        [HarmonyPostfix]
        private static void LockCharactersWithUnlockEpoch(UnlockState __instance, ref IEnumerable<CharacterModel> __result)
        {
            var unlockedEpochIds = (AccessTools.Field(typeof(UnlockState), "_unlockedEpochIds").GetValue(__instance) as HashSet<string>)!;
            var characterModels = __result as CharacterModel[] ?? __result.ToArray();
            __result = characterModels.Except(characterModels.OfType<CustomCharacterModel>()
                        .Where(c => c.UnlockEpoch is not null && !unlockedEpochIds.Contains(c.UnlockEpoch.Id)));
        }
    
    
        private static readonly MethodInfo? TryObtainEpochMidRunMethod = typeof(ProgressSaveManager)
                .GetMethod("TryObtainEpochMidRun", BindingFlags.Instance | BindingFlags.NonPublic);
        // used once in CustomCharacterModel
        internal static readonly MethodInfo? TryObtainEpochPostRunMethod = typeof(ProgressSaveManager)
                    .GetMethod("TryObtainEpochPostRun", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? GetEliteEncountersMethod = typeof(ProgressSaveManager)
                    .GetMethod("GetEliteEncounters", BindingFlags.Static | BindingFlags.NonPublic);


        [HarmonyPatch]
        public static class CharacterEpochsUnlockPatches
        {
            // CharUnlock is actually the beat Act1-3 epoch unlocks, not unlocking a character
            // The UnlockCharacter Epoch is patched in CustomCharacterModel as it requires access to a private property.
            [HarmonyPatch(typeof(ProgressSaveManager), "ObtainCharUnlockEpoch")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool SkipCharUnlockEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer, int act)
            {
                if (localPlayer.Character is not CustomCharacterModel ccm)
                    return true;
                switch (act)
                {
                    case 0:
                        if (ccm.Act1Epoch is not null)
                            TryObtainEpochMidRunMethod?.Invoke(__instance, [ccm.Act1Epoch, localPlayer]);
                        break;
                    case 1:
                        if (ccm.Act2Epoch is not null)
                            TryObtainEpochMidRunMethod?.Invoke(__instance, [ccm.Act2Epoch, localPlayer]);
                        break;
                    case 2:
                        if (ccm.Act3Epoch is not null)
                            TryObtainEpochMidRunMethod?.Invoke(__instance, [ccm.Act3Epoch, localPlayer]);
                        break;
                    case 3:
                        BaseLibMain.Logger.Warn("BaseLib does not support Act 4 yet.");
                        break;
                    default:
                        BaseLibMain.Logger.Warn($"Unsupported Act: {act}");
                        break;
                }

                return false;
            }


            // This and the two following patches currently copy the entire methods logic into the prefix.
            // In the case of 15 bosses and elites it might be better to just transpile at the "throw" to
            // check for a custom character with an epoch. Since these two methods store the epoch object
            // in a variable, unlike the Act1-3 check which runs a switch on the act number and then builds
            // the Id using strings.

            [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool SkipBossEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer)
            {
                if (localPlayer.Character is not CustomCharacterModel customCharacterModel)
                    return true;
                if (!localPlayer.Character.IsPlayable || customCharacterModel.FifteenBossesEpoch is null)
                    return false;

                var bossEncounters = ModelDb.Acts.SelectMany(a => a.AllBossEncounters.Select(e => e.Id)).ToHashSet();

                var num = 0;
                foreach (var encounterStats in __instance.Progress.EncounterStats.Values)
                {
                    if (!bossEncounters.Contains(encounterStats.Id)) continue;
                    foreach (var fightStat in encounterStats.FightStats.Where(fightStat => fightStat.Character == customCharacterModel.Id))
                    {
                        num += fightStat.Wins;
                        break;
                    }
                }

                if (num < 15)
                    return false;
                var res = TryObtainEpochMidRunMethod?.Invoke(__instance, [customCharacterModel.FifteenBossesEpoch, localPlayer]);
                if (res is true)
                    BaseLibMain.Logger.Info($"Epoch obtained for beating 15 Bosses");
                return false;
            }

            [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenElitesDefeatedEpoch")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool SkipEliteEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer)
            {
                if (localPlayer.Character is not CustomCharacterModel customCharacterModel)
                    return true;
                if (!localPlayer.Character.IsPlayable || customCharacterModel.FifteenElitesEpoch is null)
                    return false;

                var eliteEncountersObject = GetEliteEncountersMethod?.Invoke(null, []) ?? throw new NullReferenceException();
                if (eliteEncountersObject is not HashSet<ModelId> eliteEncounters) return false;

                var num = 0;
                foreach (var encounterStats in __instance.Progress.EncounterStats.Values)
                {
                    if (!eliteEncounters.Contains(encounterStats.Id)) continue;
                    foreach (var fightStat in encounterStats.FightStats.Where(fightStat => fightStat.Character == customCharacterModel.Id))
                    {
                        num += fightStat.Wins;
                        break;
                    }
                }

                if (num < 15)
                    return false;
                var res = TryObtainEpochMidRunMethod?.Invoke(__instance, [customCharacterModel.FifteenElitesEpoch, localPlayer]);
                if (res is true)
                    BaseLibMain.Logger.Info($"Epoch obtained for beating 15 Elites");
                return false;
            }

            [HarmonyPatch(typeof(ProgressSaveManager), "CheckAscensionOneCompleted")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool SkipAscensionOneEpochIfUnsupported(ProgressSaveManager __instance, SerializablePlayer serializablePlayer, SerializableRun serializableRun)
            {
                var characterModel = ModelDb.GetById<CharacterModel>(serializablePlayer.CharacterId!);
                if (characterModel is not CustomCharacterModel customCharacterModel)
                    return true;
                if (serializableRun.Ascension != 1 || customCharacterModel.AscensionOneEpoch is null)
                    return false;
                var res = TryObtainEpochPostRunMethod?.Invoke(__instance, [customCharacterModel.AscensionOneEpoch, serializablePlayer, serializableRun]);
                if (res is true)
                    BaseLibMain.Logger.Info($"Epoch obtained for beating Ascension 1");
                return false;
            }

        }




    }
}