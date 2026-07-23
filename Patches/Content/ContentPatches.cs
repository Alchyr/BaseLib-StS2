using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.UI;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
public static class CustomContentDictionary
{
    public static readonly HashSet<Type> RegisteredTypes = [];
    private static readonly Dictionary<Type, Type> PoolTypes = [];
    public static readonly List<CustomCharacterModel> CustomCharacters = [];
    public static readonly List<CustomEncounterModel> CustomEncounters = [];
    public static readonly List<CustomAncientModel> CustomAncients = [];
    public static readonly List<Type> CustomBadgeTypes = [];
    
    /// <summary>
    /// Custom events tied to a specific act.
    /// </summary>
    public static readonly List<CustomEventModel> ActCustomEvents = [];
    /// <summary>
    /// Custom events not tied to a specific act.
    /// </summary>
    public static readonly List<CustomEventModel> SharedCustomEvents = [];
    public static readonly List<CustomActModel> CustomActs = [];
    
    static CustomContentDictionary()
    {
        PoolTypes.Add(typeof(CardPoolModel), typeof(CardModel));
        PoolTypes.Add(typeof(RelicPoolModel), typeof(RelicModel));
        PoolTypes.Add(typeof(PotionPoolModel), typeof(PotionModel));
    }

    public static bool RegisterType(Type t)
    {
        return RegisteredTypes.Add(t);
    }

    public static void AddModel(Type modelType)
    {
        if (!RegisterType(modelType)) return;
        
        var poolAttribute = modelType.GetCustomAttribute<PoolAttribute>()
            ?? throw new Exception($"Model {modelType.FullName} must be marked with a PoolAttribute to determine which pool to add it to.");

        if (!IsValidPool(modelType, poolAttribute.PoolType))
        {
            throw new Exception($"Model {modelType.FullName} is assigned to incorrect type of pool {poolAttribute.PoolType.FullName}.");
        }
        
        ModHelper.AddModelToPool(poolAttribute.PoolType, modelType);
    }

    public static void AddEncounter(CustomEncounterModel encounter)
    {
        if (!RegisterType(encounter.GetType())) return;

        CustomEncounters.InsertSorted(encounter);
    }

    public static void AddAncient(CustomAncientModel ancient)
    {
        if (!RegisterType(ancient.GetType())) return;
        
        CustomAncients.InsertSorted(ancient);
    }
    
    public static void AddEvent(CustomEventModel eventModel)
    {
        if (!RegisterType(eventModel.GetType())) return;

        if (eventModel.Acts.Length == 0)
        {
            SharedCustomEvents.InsertSorted(eventModel);
        }
        else
        {
            ActCustomEvents.InsertSorted(eventModel);
        }
    }

    
    public static bool AddBadge(Type badgeType)
    {
        if (!RegisterType(badgeType)) return false;
        CustomBadgeTypes.Add(badgeType);
        return true;
    }

  
    public static void AddAct(CustomActModel actModel)
    {
        if (!RegisterType(actModel.GetType())) return;
        
        CustomActs.InsertSorted(actModel);
    }

    public static void AddCharacter(CustomCharacterModel character)
    {
        if (!RegisterType(character.GetType())) return;
        
        CustomCharacters.InsertSorted(character);
        var cookie = character.CustomYummyCookie;
        if (cookie != null)
        {
            RelicImageOverridePatch.AddOverride<YummyCookie>(cookie, (relic) => relic.IsMutable && character.Id.Equals(relic.Owner?.Character.Id));
        }
    }
    
    private static bool IsValidPool(Type modelType, Type poolType)
    {
        var basePoolType = poolType.BaseType;
        while (basePoolType != null)
        {
            if (PoolTypes.TryGetValue(basePoolType, out var poolValueType))
            {
                return modelType.IsAssignableTo(poolValueType);
            }
            basePoolType = basePoolType.BaseType;
        }
        throw new Exception($"Model {modelType.FullName} is assigned to {poolType.FullName} which is not a valid pool type.");
    }
    
    [HarmonyPostfix]
    static void ScanCustomBadges()
    {
        foreach (var type in ReflectionHelper.GetSubtypesInMods<CustomBadge>()
                     .Where(t => !t.IsAbstract))
        {
            var added = AddBadge(type);
        }
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
class AddCustomCharacters
{
    [HarmonyPostfix]
    static IEnumerable<CharacterModel> Patch(IEnumerable<CharacterModel> __result)
    {
        return [.. __result, ..CustomContentDictionary.CustomCharacters];
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedAncients), MethodType.Getter)]
class CustomAncientExistence
{
    [HarmonyPostfix]
    static IEnumerable<AncientEventModel> AddCustomAncientForCompendium(IEnumerable<AncientEventModel> __result)
    {
        return [.. __result, .. CustomContentDictionary.CustomAncients];
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
public class CurrentGeneratingRunState
{
    public static RunState? State { get; private set; }
    private static readonly MethodInfo StateGetter = AccessTools.PropertyGetter(typeof(RunManager), "State");
    
    [HarmonyPrefix]
    static void GetState(RunManager __instance)
    {
        State = (RunState?) StateGetter.Invoke(__instance, []);
    }

    [HarmonyPostfix]
    static void ClearState()
    {
        State = null;
    }
}
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
class AddCustomAncientsToPool
{
    private static readonly FieldInfo RoomSet = AccessTools.Field(typeof(ActModel), "_rooms");
    
    [HarmonyPrefix]
    static void AddToModelPool(ActModel __instance, List<AncientEventModel>? ____sharedAncientSubset)
    {
        if (____sharedAncientSubset == null) return; //Act 1 or other act with no shared ancients

        //Not a fan of this, but having them in shared ancients rather than all ancients is the easiest way to have them
        //appear in compendium.
        ____sharedAncientSubset.RemoveAll(CustomContentDictionary.CustomAncients.Contains);
        
        List<CustomAncientModel> toAdd = [..CustomContentDictionary.CustomAncients];
        toAdd.Sort((a, b) =>  string.Compare(a.Id.Entry, b.Id.Entry, StringComparison.Ordinal));
        
        toAdd.RemoveAll(ancient => !ancient.IsValidForAct(__instance) || ____sharedAncientSubset.Contains(ancient));
        foreach (var act in CurrentGeneratingRunState.State?.Acts ?? [])
        {
            if (RoomSet.GetValue(act) is not RoomSet { HasAncient: true }) continue;
            if (act == __instance) continue;
            if (act.Ancient is CustomAncientModel customAncient) toAdd.Remove(customAncient);
        }
        ____sharedAncientSubset.AddRange(toAdd);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedCardPools), MethodType.Getter)]
class ModelDbSharedCardPoolsPatch
{
    private static readonly List<CardPoolModel> CustomSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<CardPoolModel> AddCustomPools(IEnumerable<CardPoolModel> __result)
    {
        return [.. __result, .. CustomSharedPools];
    }

    public static void Register(CustomCardPoolModel pool)
    {
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        CustomSharedPools.Add(pool);
    }
}

[HarmonyPatch(typeof(ModelDb), "AllSharedRelicPools", MethodType.Getter)]
class ModelDbSharedRelicPoolsPatch
{
    private static readonly List<RelicPoolModel> customSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<RelicPoolModel> AddCustomPools(IEnumerable<RelicPoolModel> __result)
    {
        return [.. __result, .. customSharedPools];
    }

    public static void Register(CustomRelicPoolModel pool)
    {
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        customSharedPools.Add(pool);
    }
}

[HarmonyPatch(typeof(ModelDb), "AllSharedPotionPools", MethodType.Getter)]
class ModelDbSharedPotionPoolsPatch
{
    private static readonly List<PotionPoolModel> customSharedPools = [];

    [HarmonyPostfix]
    static IEnumerable<PotionPoolModel> AddCustomPools(IEnumerable<PotionPoolModel> __result)
    {
        return [.. __result, .. customSharedPools];
    }

    public static void Register(CustomPotionPoolModel pool)
    {
        if (!CustomContentDictionary.RegisterType(pool.GetType())) return;
        
        customSharedPools.Add(pool);
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
class ActModelGenerateRoomsPatch
{
    [HarmonyPostfix]
    static void ForceAncientToSpawn(ActModel __instance)
    {
        var rooms = Traverse.Create(__instance).Field<RoomSet>("_rooms").Value;
        if (!rooms.HasAncient) return;
        
        var rngChosenAncient = rooms.Ancient;
        var ancientToSpawn = CustomContentDictionary.CustomAncients.Find(a => a.ShouldForceSpawn(__instance, rngChosenAncient));

        if (ancientToSpawn != null)
        {
            rooms.Ancient = ancientToSpawn;
        }
    }
}

[HarmonyPatch]
static class AddAct1AncientsToModelPool
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo abstractMethod = AccessTools.DeclaredMethod(
            typeof(ActModel),
            nameof(ActModel.GetUnlockedAncients),
            [typeof(UnlockState)]);

        return AccessTools.AllTypes()
            .Where(type =>
                type != typeof(ActModel) &&
                typeof(ActModel).IsAssignableFrom(type))
            .Select(type => AccessTools.Method(
                type,
                nameof(ActModel.GetUnlockedAncients),
                [typeof(UnlockState)]))
            .Where(method =>
                method is not null &&
                !method.IsAbstract &&
                method.GetBaseDefinition() == abstractMethod)
            .Distinct();
    }

    [HarmonyPostfix]
    private static IEnumerable<AncientEventModel> Postfix(
        IEnumerable<AncientEventModel> ancients,
        ActModel __instance)
    {
        if (__instance.ActNumber() != 1)
            return ancients;

        List<AncientEventModel> result = ancients.ToList();

        foreach (CustomAncientModel ancient in CustomContentDictionary.CustomAncients)
        {
            if (ancient.IsAct1Ancient() && ancient.IsValidForAct(__instance))
            {
                result.Add(ancient);
            }
        }

        return result;
    }
}

/// <summary>
/// In base game the check for Neow is hardcoded. This patch is needed to properly set up the run.
/// </summary>
[HarmonyPatch]
public static class AddAct1Ancients_NeowChecks
{
    private static readonly MethodInfo IsNeowLikeMethod =
        AccessTools.Method(
            typeof(AddAct1Ancients_NeowChecks),
            nameof(IsNeowLike))
        ?? throw new MissingMethodException(
            nameof(AddAct1Ancients_NeowChecks),
            nameof(IsNeowLike));

    /// <summary>
    ///     BeforeEventStarted is async, so its actual body is inside the
    ///     compiler-generated IAsyncStateMachine.MoveNext method.
    /// </summary>
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        MethodInfo beforeEventStarted =
            AccessTools.Method(
                typeof(AncientEventModel),
                "BeforeEventStarted",
                [typeof(bool)])
            ?? throw new MissingMethodException(
                typeof(AncientEventModel).FullName,
                "BeforeEventStarted");

        AsyncStateMachineAttribute stateMachineAttribute =
            beforeEventStarted.GetCustomAttribute<AsyncStateMachineAttribute>()
            ?? throw new InvalidOperationException(
                "BeforeEventStarted does not have an AsyncStateMachineAttribute.");

        return AccessTools.Method(
                   stateMachineAttribute.StateMachineType,
                   nameof(IAsyncStateMachine.MoveNext))
               ?? throw new MissingMethodException(
                   stateMachineAttribute.StateMachineType.FullName,
                   nameof(IAsyncStateMachine.MoveNext));
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        int replacements = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            // Original:
            // ancientEventModel is Neow
            //
            // IL:
            // isinst Neow
            //
            // Replacement:
            // IsNeowLike(ancientEventModel)
            if (instruction.opcode == OpCodes.Isinst &&
                instruction.operand is Type checkedType &&
                checkedType == typeof(Neow))
            {
                // Mutating the existing instruction preserves its
                // labels and exception-block metadata.
                instruction.opcode = OpCodes.Call;
                instruction.operand = IsNeowLikeMethod;

                replacements++;
            }

            yield return instruction;
        }

        // The supplied game method currently contains exactly two checks:
        //
        // 1. SetCurrentHpInternal(0)
        // 2. TopBar.Hp.LerpAtNeow()
        if (replacements != 2)
        {
            throw new InvalidOperationException(
                $"Expected to replace 2 Neow checks in " +
                $"{nameof(AncientEventModel)}.BeforeEventStarted, " +
                $"but replaced {replacements}. The game method may have changed.");
        }
    }

    /// <summary>
    ///     Behaves like an extended `is Neow` instruction.
    ///     Returning the original object or null preserves the stack behavior
    ///     of `isinst Neow`, which also returns an object reference or null.
    /// </summary>
    private static object? IsNeowLike(object? ancient)
    {
        return ancient is Neow
               ||  CustomContentDictionary.CustomAncients.Any(customAncient =>
                   customAncient.IsAct1Ancient()
                   && customAncient.GetType().IsInstanceOfType(ancient))
            ? ancient
            : null;
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedEvents), MethodType.Getter)]
class CustomSharedEvents
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddCustomShared(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .dup()
                .stsfld(null))
            .Step(-2)
            .Insert(CodeInstruction.Call(typeof(CustomSharedEvents), nameof(ConcatCustom)));
    }

    static IEnumerable<EventModel> ConcatCustom(IEnumerable<EventModel> events)
    {
        var result = new List<EventModel>(events);
        result.AddRange(CustomContentDictionary.SharedCustomEvents);
        return result;
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Acts), MethodType.Getter)]
class ModelDbCustomActsPatch
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddCustomActs(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .stsfld(typeof(ModelDb).DeclaredField("_acts")))
            .InsertBeforeMatch([
                CodeInstruction.Call(typeof(ModelDbCustomActsPatch), nameof(AddCustomActsSorted))
            ]);
    }

    static List<ActModel> AddCustomActsSorted(List<ActModel> original)
    {
        BaseLibMain.Logger.Info($"Adding {CustomContentDictionary.CustomActs.Count} custom acts to act list.");

        original.AddRange(CustomContentDictionary.CustomActs);
        var result = original.OrderBy(act => act.Index)
            .ThenByDescending(act => act.IsDefault)
            .ThenBy(act => act.Id)
            .ToList();
        
        BaseLibMain.Logger.Info($"Result: {result.AsReadable()}");

        return result;
    }
}

/// <summary>
/// Called in PostModInitPatch to catch modded acts
/// </summary>
public static class AddActContent
{
    public static void Patch(Harmony harmony)
    {
        StringBuilder patchedTypes = new("Patching act types for custom encounters and events");
        
        foreach (var t in ReflectionHelper.GetSubtypes<ActModel>()
                     .Chain(ReflectionHelper.GetSubtypesInMods<ActModel>()))
        {
            bool patched = false;
            var method = AccessTools.DeclaredMethod(t, nameof(ActModel.GenerateAllEncounters));
            if (method != null)
            {
                patched = true;
                harmony.Patch(method, postfix: AccessTools.Method(typeof(AddActContent), nameof(AddCustomEncounters)));
            }

            method = AccessTools.DeclaredPropertyGetter(t, nameof(ActModel.AllEvents));
            if (method != null)
            {
                patched = true;
                harmony.Patch(method, postfix: AccessTools.Method(typeof(AddActContent), nameof(AddCustomEvents)));
            }

            if (patched)
            {
                patchedTypes.Append(" | ").Append(t.Name);
            }
        }

        BaseLibMain.Logger.Info(patchedTypes.ToString());
    }

    static IEnumerable<EncounterModel> AddCustomEncounters(IEnumerable<EncounterModel> result, ActModel __instance)
    {
        List<EncounterModel> origResult = result.ToList();
        foreach (var value in origResult)
        {
            yield return value;
        }

        foreach (var encounter in CustomContentDictionary.CustomEncounters)
        {
            if (origResult.Any(existingEncounter => existingEncounter.Id.Equals(encounter.Id))) continue;
            if (encounter.IsValidForAct(__instance)) yield return encounter;
        }
    }

    static IEnumerable<EventModel> AddCustomEvents(IEnumerable<EventModel> result, ActModel __instance)
    {
        List<EventModel> origResult = result.ToList();
        foreach (var value in origResult)
        {
            yield return value;
        }

        foreach (var eventModel in CustomContentDictionary.ActCustomEvents)
        {
            if (origResult.Any(existingEvent => existingEvent.Id.Equals(eventModel.Id))) continue;
            if (eventModel.Acts.Any(act => act.Id.Equals(__instance.Id))) yield return eventModel;
        }
    }
}
