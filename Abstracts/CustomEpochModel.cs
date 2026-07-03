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
/// These are not stored in <see cref="ModelDb"/>
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

    // TODO: See if this can be foregone by checking the "Era" property for a custom era.
    /// <summary>
    /// Override only if this Epoch is part of a <see cref="CustomEpochEra">Custom Era</see>.
    /// </summary>
    public virtual CustomEpochEra? CustomEra => null;

    /// <summary>
    /// Set to true if your epoch unlocks cards so it can be added to <seealso cref="SaveManager.GetCardUnlockEpochIds"/>
    /// </summary>
    public virtual bool UnlocksCards => false;

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
        
        [HarmonyPatch(typeof(EpochModel), nameof(PackedPortraitPath), MethodType.Getter)]
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
    public static void FillEpochDictionaries(List<CustomEpochModel> models)
    {
        BaseLibMain.Logger.Info("Inserting CustomEpochs into dictionaries");
        var epochModelType = typeof(EpochModel);
        var epochTypeDictionary = (AccessTools.Field(epochModelType, "_epochTypeDictionary").GetValue(null) as Dictionary<string, Type>)!;
        var typeToIdDictionary = (AccessTools.Field(epochModelType, "_typeToIdDictionary").GetValue(null) as Dictionary<Type, string>)!;
        foreach (var customEpochModel in models)
        {
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
                            .ThrowIfInvalid("Could not find correct position")
                            .InsertAfter([
                                        new CodeInstruction(OpCodes.Ldarg_0),
                                        new CodeInstruction(OpCodes.Ldloc_0),
                                        new CodeInstruction(OpCodes.Call, enforceUniqueEraPosition),
                            ]);
                var list = matcher.InstructionEnumeration().ToList();
                for (var i = 0; i <  list.Count; i++)
                {
                    BaseLibMain.Logger.Info($"Instruction {i} => OpCode: {list[i].opcode} | Operand: {list[i].operand}");
                }
                return matcher.InstructionEnumeration().ToList();
            }
    
            private static void EnforceUniqueEraPosition(NEraColumn nEraColumn, NEpochSlot nEpochSlot)
            {
                var allCurrentNEpochSlots = nEraColumn.GetChildren().OfType<NEpochSlot>().ToList();
                var oldEraPosition = nEpochSlot.eraPosition;
                while (allCurrentNEpochSlots.Any(o => o.eraPosition == nEpochSlot.eraPosition))
                {
                    nEpochSlot.eraPosition++;
                }
                if (oldEraPosition == nEpochSlot.eraPosition) return;
                OverwrittenEraPositions.Set(nEpochSlot, true);
                BaseLibMain.Logger.Info($"Moved EpochSlot position for Epoch {nEpochSlot.model.Id} from {oldEraPosition} to {nEpochSlot.eraPosition}");
            }
            
            [HarmonyPatch(typeof(NTimelineScreen), nameof(NTimelineScreen.InitScreen), MethodType.Async)]
            [HarmonyTranspiler]
            private static List<CodeInstruction> ResolveSetStateForNewEraPositions(IEnumerable<CodeInstruction> instructions)
            {
                var insts = instructions.ToList();
                for (var i = 0; i < insts.Count; i++)
                {
                    BaseLibMain.Logger.Info($"Instruction {i} => OpCode: {insts[i].opcode} | Operand: {insts[i].operand}");
                }

               //ldloc 12 9
                var getActualEraPosition = typeof(DuplicateEraPositionHandler).Method(nameof(GetActualEraPosition));
                 var matcher = new CodeMatcher(instructions)
                             .MatchStartForward([
                                         new CodeMatch(OpCodes.Isinst),
                             ])
                             .ThrowIfInvalid("Could not find correct position")
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
                             ])
                             ;

                 var list = matcher.InstructionEnumeration().ToList();
                 for (var i = 0; i <  list.Count; i++)
                 {
                     BaseLibMain.Logger.Info($"Instruction {i} => OpCode: {list[i].opcode} | Operand: {list[i].operand}");
                 }
                 return matcher.InstructionEnumeration().ToList();
            }
            private static bool GetActualEraPosition(NEpochSlot nEpochSlot, EpochModel epochModel)
            {
                return nEpochSlot.model.Id == epochModel.Id;
            }
        }
        
        
        // If the patch above stops working we can also do this.
        // However, the patch above runs BEFORE NEpochSlot._Ready() is called, where as this runs after 
        
        // [HarmonyPatch(typeof(NEraColumn), nameof(NEraColumn.AddSlot))]
        // [HarmonyPostfix]
        // private static void AdjustNEpochSlotEraPosition(NEraColumn __instance)
        // {
        //     var nEpochSlot = __instance.GetChildren().OfType<NEpochSlot>().FirstOrDefault();
        //     if (nEpochSlot is null) return;
        //     var allCurrentNEpochSlots = __instance.GetChildren().OfType<NEpochSlot>().Except([nEpochSlot]).ToList();
        //     var oldEraPosition = nEpochSlot.eraPosition;
        //     while (allCurrentNEpochSlots.Any(o => o.eraPosition == nEpochSlot.eraPosition))
        //         nEpochSlot.eraPosition++;
        //     if (oldEraPosition == nEpochSlot.eraPosition) return;
        //     nEpochSlot.Name = $"Slot{nEpochSlot.eraPosition}"; // We dont need to move the child because the game always moves them to pos 0
        //     BaseLibMain.Logger.Info($"Moved EpochSlot position for Epoch {nEpochSlot.model.Id} from {oldEraPosition} to {nEpochSlot.eraPosition}");
        // }
        
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
        private static readonly MethodInfo? TryObtainEpochPostRunMethod = typeof(ProgressSaveManager)
                    .GetMethod("TryObtainEpochPostRun", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? GetEliteEncountersMethod = typeof(ProgressSaveManager)
                    .GetMethod("GetEliteEncounters", BindingFlags.Static | BindingFlags.NonPublic);
    
        // These skips would always skip, even if the creator intended for their own way to implement them!
        // For now, I will leave it as is because adding booleans to the CustomCharacterModel in case someone might want to use these Epochs
        // but does not want to set them in the characters class, is unnecessary. (it isn't too difficult to counter patch either. The main issue
        // is figuring out this was the reason for them not running which is helped by this comment here. Hello there!)
        [HarmonyPatch(typeof(ProgressSaveManager), "ObtainCharUnlockEpoch")]
        [HarmonyPrefix]
        private static bool SkipCharUnlockEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer, int act)
        {
            if (localPlayer.Character is not CustomCharacterModel ccm)
                return true;
            switch (act)
            {
                case 0:
                    if(ccm.Act1Epoch is not null)
                        TryObtainEpochMidRunMethod?.Invoke(__instance, [ccm.Act1Epoch, localPlayer]);
                    break;
                case 1:
                    if(ccm.Act2Epoch is not null)
                        TryObtainEpochMidRunMethod?.Invoke(__instance, [ccm.Act2Epoch, localPlayer]);
                    break;
                case 2:
                    if(ccm.Act3Epoch is not null)
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
    
    
        // This and the two following patches currently copy the entire methods logic into the prefix
        // In the case of 15 bosses and elites it might be better to just transpile at the "throw" to
        // check for a custom character with an epoch.
        // Since these two methods store the epoch object in a variable unlike the Act1-3
        // check which runs a switch on the act number and then builds the Id using strings. 
        
        [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
        [HarmonyPrefix] 
        private static bool SkipBossEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer)
        {
            if(localPlayer.Character is not CustomCharacterModel customCharacterModel)
                return true;
            if(!localPlayer.Character.IsPlayable || customCharacterModel.FifteenBossesEpoch is null)
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
            if(res is true)
                BaseLibMain.Logger.Info($"Epoch obtained for beating 15 Bosses");
            return false;
        }
        
        [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenElitesDefeatedEpoch")]
        [HarmonyPrefix]
        private static bool SkipEliteEpochIfUnsupported(ProgressSaveManager __instance, Player localPlayer)
        {
            if(localPlayer.Character is not CustomCharacterModel customCharacterModel)
                return true;
            if(!localPlayer.Character.IsPlayable || customCharacterModel.FifteenElitesEpoch is null)
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
            if(res is true)
                BaseLibMain.Logger.Info($"Epoch obtained for beating 15 Elites");
            return false;
        }
    
        [HarmonyPatch(typeof(ProgressSaveManager), "CheckAscensionOneCompleted")]
        [HarmonyPrefix]
        private static bool SkipAscensionOneEpochIfUnsupported(ProgressSaveManager __instance, SerializablePlayer serializablePlayer, SerializableRun serializableRun)
        {
            var characterModel = ModelDb.GetById<CharacterModel>(serializablePlayer.CharacterId!);
            if(characterModel is not CustomCharacterModel customCharacterModel)
                return true;
            if (serializableRun.Ascension != 1 || customCharacterModel.AscensionOneEpoch is null) 
                return false;
            var res = TryObtainEpochPostRunMethod?.Invoke(__instance, [customCharacterModel.AscensionOneEpoch, serializablePlayer, serializableRun]);
            if(res is true)
                BaseLibMain.Logger.Info($"Epoch obtained for beating Ascension 1");
            return false;
        }
        


    
     
    }
}