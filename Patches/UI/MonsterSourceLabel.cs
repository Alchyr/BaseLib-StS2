using BaseLib.Config;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.UI;

/// <summary>
/// Monsters have no own text hover tip, so for modded monsters the source mod is shown directly
/// under the monster's name on its nameplate.
/// </summary>
[HarmonyPatch(typeof(NCreatureStateDisplay), "RefreshValues")]
static class MonsterSourceLabel
{
    [HarmonyPostfix]
    static void Postfix(NCreatureStateDisplay __instance)
    {
        if (!BaseLibConfig.ShowMonsterModSource) return;

        var creature = __instance._creature;
        var monster = __instance._creature?.Monster;
        if (creature == null || monster == null) return;

        var name = WhatMod.FindModName(monster.GetType());
        if (name == null) return;

        __instance._nameplateLabel?.SetTextAutoSize($"{creature.Name}\n{name}");
    }
}
