using BaseLib.Utils;
using BaseLib.Utils.NodeFactories;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace BaseLib.Patches.UI;

[HarmonyPatch]
class MerchantCharacterAnimPatch
{
    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter._Ready))]
    [HarmonyPrefix]
    public static bool SkipInitialAnimIfNotSpine(NMerchantCharacter __instance)
    {
        if (!NodeFactory.CreatedFromFactory(__instance)) return true;
        
        if (CustomAnimation.HasCustomAnimation(__instance)) return false;
        if (__instance.GetChildCount() == 0) return false;
        if (__instance.GetChild(0) is not GodotObject godotObj
            || godotObj.GetClass() != MegaSprite.spineClassName) return false;
        
        return true;
    }
    
    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation))]
    [HarmonyPrefix]
    public static bool PlayAlternateAnimation(NMerchantCharacter __instance, string anim, bool loop)
    {
        if (!NodeFactory.CreatedFromFactory(__instance)) return true;
        
        if (CustomAnimation.PlayCustomAnimation(__instance, GetAnimNames(anim))) return false;
        if (__instance.GetChildCount() == 0) return false;
        if (__instance.GetChild(0) is not GodotObject godotObj
            || godotObj.GetClass() != MegaSprite.spineClassName) return false;
        
        return true;
    }

    private static string[] GetAnimNames(string animName)
    {
        return animName switch
        {
            "relaxed_loop" => ["idle", "Idle", animName],
            "die" => ["Die", animName],
            _ => [animName]
        };
    }
}