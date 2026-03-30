using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomSingletonModel : SingletonModel {

    public override bool ShouldReceiveCombatHooks => registerSettings.SubscribeToCombatStateHooks;

    public abstract SingletonSettings registerSettings { get; }
    public abstract string modId { get; }

    protected CustomSingletonModel() {
        if (modelsToRegister.ContainsKey(modId)) {
            modelsToRegister[modId].Add(ModelDb.GetById<CustomSingletonModel>(Id));
        } else {
            modelsToRegister.Add(modId,[ModelDb.GetById<CustomSingletonModel>(Id)]);
        }
    }
    
    public static CustomSingletonModel GetSingletonId<T>(ModelId id) where T : CustomSingletonModel{
        if (!ModelDb.Contains(typeof(T)))
            ModelDb.Inject(typeof(T));
        return ModelDb.GetByIdOrNull<T>(id) ?? throw new ModelNotFoundException(id);
    }
    
    private static Dictionary<string, List<CustomSingletonModel>> modelsToRegister = []; 
    
    public struct SingletonSettings {
        public bool SubscribeToRunStateHooks;
        public bool SubscribeToCombatStateHooks;
    }
    public static void Subscribe(string ModID)
    {
        if(!modelsToRegister.ContainsKey(ModID)) return;
        var ourModels = modelsToRegister[ModID];
        ModHelper.SubscribeForRunStateHooks(ModID,(_) => ourModels.FindAll((m)=> m.registerSettings.SubscribeToRunStateHooks));
        ModHelper.SubscribeForCombatStateHooks(ModID,(_) => ourModels.FindAll((m)=> m.registerSettings.SubscribeToCombatStateHooks));
    }
}