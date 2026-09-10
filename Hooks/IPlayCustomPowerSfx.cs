using System.Reflection.Emit;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Hooks;

/// <summary>
/// Interface for a power that plays a sound when applied other than the default
/// </summary>
public interface IPlayCustomPowerSfx
{
    /// <summary>
    /// Play the desired custom sfx using <see cref="SfxCmd.Play(string, float)"/>
    /// </summary>
    /// <param name="amount">The amount of power applied</param>
    /// <param name="isBuff">Is true if the buff sfx would have played, otherwise the debuff sfx would have played</param>
    /// <returns>Whether or not the sound played (if false, the default power apply sound plays)</returns>
    public bool PlayCustomPowerSfx(int amount, bool isBuff);

    [HarmonyPatch(typeof(NCreature), nameof(NCreature.OnPowerIncreased))]
    private class IPlayCustomPowerSfxPatch
    {
        public static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .callvirt(AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.ShouldPlayVfx)))
            )
            .GetOperandLabel(out var target) // where to go if we want to skip the default power sfx
            .Step(1)
            .Insert([
                CodeInstruction.LoadArgument(1),
                CodeInstruction.LoadArgument(2),
                CodeInstruction.LoadLocal(1),
                CodeInstruction.Call(typeof(IPlayCustomPowerSfxPatch), nameof(PlaySfx)), // check if a custom sound should play and play it if so
                new CodeInstruction(OpCodes.Brtrue_S, target), // if a custom sound played, skip playing the default one
            ]);
        
        private static bool PlaySfx(PowerModel power, int amount, bool isBuff)
        {
            if (power is IPlayCustomPowerSfx custom)
                return custom.PlayCustomPowerSfx(amount, isBuff);
            return false;
        }
    }
}