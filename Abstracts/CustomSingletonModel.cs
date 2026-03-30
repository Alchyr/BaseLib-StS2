using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomSingletonModel : SingletonModel {

    public virtual SingletonSettings registerSettings { get; }
    public virtual string modId { get; }
    
    protected CustomSingletonModel() {
        if (modelsToRegister.ContainsKey(modId)) {
            modelsToRegister[modId].Add(this);
        } else {
            modelsToRegister.Add(modId,[this]);
        }
    }

    private static Dictionary<string, List<CustomSingletonModel>> modelsToRegister = []; 
    public struct SingletonSettings {
        
    }
    public static void Subscribe(string ModID)
    {
        var ourModels = modelsToRegister[ModID];
        ModHelper.SubscribeForRunStateHooks(ModID,(_) => ourModels);
    }
}