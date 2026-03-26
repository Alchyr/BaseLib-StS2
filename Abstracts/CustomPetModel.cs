using BaseLib.Utils;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Abstracts;

public class CustomPetModel: CustomMonsterModel ,ICustomModel{
    /// <summary>
    /// Override this or place your scene at res://scenes/creature_visuals/class_name.tscn
    /// </summary>
    public new virtual string? CustomVisualPath => null;
    
    public override int MinInitialHp => 9999;

    public override int MaxInitialHp => 9999;

    public override bool IsHealthBarVisible => false;
    
    /// <summary>
    /// By default, will convert a scene containing the necessary nodes into a NCreatureVisuals even if it is not one.
    /// </summary>
    /// <returns></returns>
    public override NCreatureVisuals? CreateCustomVisuals()
    {
        string? path = (CustomVisualPath ?? VisualsPath);
        if (path == null) return null;
        return GodotUtils.CreatureVisualsFromScene(path);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState nothingState = new MoveState("NOTHING_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
        nothingState.FollowUpState = nothingState;
        return new MonsterMoveStateMachine([nothingState], nothingState);
    }
}