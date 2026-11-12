using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace BaseLib.Abstracts;

public abstract class CustomPetModel(bool interactable) : CustomMonsterModel {
    //public override bool IsHealthBarVisible => visibleHp;
    
    //Visuals are defined the same way as CustomMonsterModel.
    
    //As of latest beta - whether a monster is a pet is determined by its PetOwner being non-null.
    //Uninteractable pets like byrdpip have IsInteractable overriden to false.
    
    //Todo - cleanup trigger on player death? 

    /// <inheritdoc />
    public override bool IsInteractable => interactable;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState nothingState = new MoveState("NOTHING_MOVE",_ => Task.CompletedTask);
        nothingState.FollowUpState = nothingState;
        return new MonsterMoveStateMachine([nothingState], nothingState);
    }
}