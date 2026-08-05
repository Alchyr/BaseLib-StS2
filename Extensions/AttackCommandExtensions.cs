using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Extensions;

public static class AttackCommandExtensions
{
    public static AttackCommand WithValueProp(this AttackCommand attackCommand, ValueProp valueProp)
    {
        attackCommand.DamageProps = valueProp;
        return attackCommand;
    }

    /// <summary>
    /// Calls Execute on an attack command and returns targets that were killed and valid for Fatal (ignoring minions
    /// and similar).
    /// </summary>
    public static async Task<List<Creature>> ExecuteAndCheckFatal(this AttackCommand attackCommand, PlayerChoiceContext choiceContext)
    {
        List<Creature> fatalKills = [];
        var canBeFatal = attackCommand.GetPossibleTargets()
            .Where(e => e.Powers.All(p => p.ShouldOwnerDeathTriggerFatal()))
            .ToHashSet();
        
        await attackCommand.Execute(choiceContext);

        foreach (var result in attackCommand.Results.SelectMany(r => r))
        {
            if (result.WasTargetKilled && canBeFatal.Contains(result.Receiver))
            {
                if (!fatalKills.Contains(result.Receiver))
                {
                    fatalKills.Add(result.Receiver);
                }
            }
        }

        return fatalKills;
    }
}