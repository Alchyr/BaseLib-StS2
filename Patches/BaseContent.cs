using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace BaseLib.Patches;

/// <summary>
/// Some helper methods when getting only basegame content is desired.
/// </summary>
[HarmonyPatch]
public static class BaseContent
{
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
    [HarmonyReversePatch]
    public static IEnumerable<CharacterModel> BaseCharacters() =>
    [   //Default main-branch compatible implementation in case of reverse patch somehow failing
        ModelDb.Character<Ironclad>(),
        ModelDb.Character<Silent>(),
        ModelDb.Character<Regent>(),
        ModelDb.Character<Necrobinder>(),
        ModelDb.Character<Defect>()
    ];
}