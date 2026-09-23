using BaseLib.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.ConsoleCommands;

/// <summary>
///     Debug console command for the Temporary HP mechanic. Modeled on the vanilla "heal" command.
/// </summary>
public class TempHpConsoleCmd : AbstractConsoleCmd
{
    /// <inheritdoc />
    public override string CmdName => "temphp";

    /// <inheritdoc />
    public override string Args => "<amount:int> [index:int]";

    /// <inheritdoc />
    public override string Description => "Grant the player (or the ally at index) Temporary HP. A negative amount removes all of it.";

    /// <inheritdoc />
    public override bool IsNetworked => true;

    /// <inheritdoc />
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length < 1)
            return new CmdResult(success: false, "An amount is required");
        if (!int.TryParse(args[0], out var amount))
            return new CmdResult(success: false, "First argument (the Temporary HP amount) must be an int.");
        if (!RunManager.Instance.IsInProgress)
            return new CmdResult(success: false, "A run does not appear to be in progress");
        if (!CombatManager.Instance.IsInProgress)
            return new CmdResult(success: false, "Temporary HP can only be granted during combat.");
        Creature creature;
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out var index))
                return new CmdResult(success: false, "Arg 2 must be the target index (int), got '" + args[1] + "'.");
            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null)
                return new CmdResult(success: false, "No combat state available.");
            IReadOnlyList<Creature> allies = combatState.Allies;
            if (index < 0 || index >= allies.Count)
                return new CmdResult(success: false, $"Invalid target index {index}. Valid range: 0-{allies.Count - 1}");
            creature = allies[index];
        }
        else
        {
            if (issuingPlayer == null)
                return new CmdResult(success: false, "No issuing player; pass an ally index instead.");
            creature = issuingPlayer.Creature;
        }
        if (amount < 0)
        {
            Task removeTask = TempHpCmd.RemoveAll(creature);
            return new CmdResult(removeTask, success: true, $"Removed all Temporary HP from {creature}.");
        }
        Task addTask = TempHpCmd.Add(new ThrowingPlayerChoiceContext(), creature, amount);
        return new CmdResult(addTask, success: true, $"Granted '{amount}' Temporary HP to {creature}.");
    }
}
