using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Extensions;

public static class AttackCommandPatch
{
    public static AttackCommand WithValueProp(this AttackCommand attackCommand, ValueProp valueProp)
    {
        attackCommand.DamageProps = valueProp;
        return attackCommand;
    }
}