using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.BaseLibScenes.Acts;


/// <summary>
/// Scenes that are intended to replace the visuals of a chest in Treasure rooms through <see cref="CustomActModel.CustomChestScene"/> need to have a script attached to the root control which derives from this class.<br></br>
/// Forwards the basic event handlers from the NTreasureRoom node:
/// <list type="bullet">
///   <item>  <description>  <c>OnChestButtonReleased</c> used to start the open animation </description> </item>
///   <item>  <description>  <c>OnMouseEntered</c> used to show the highlight </description> </item>
///   <item>  <description>  <c>OnMouseExited</c> used to hide the highlight </description> </item>
/// </list>
/// </summary>
[GlobalClass]
public partial class NCustomTreasureRoomChest  : Control
{
    public static NCustomTreasureRoomChest? Create(NTreasureRoom nTreasureRoom, IRunState runState, NButton chestButton, string scenePath)
    {
        NCustomTreasureRoomChest nTestChestAnim = PreloadManager.Cache.GetScene(scenePath).Instantiate<NCustomTreasureRoomChest>();
        nTestChestAnim.RunState = runState;
        nTestChestAnim.TreasureRoomNode = nTreasureRoom;
        chestButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(nTestChestAnim.OnChestButtonReleased));
        chestButton.Connect(Control.SignalName.MouseEntered, Callable.From(nTestChestAnim.OnMouseEntered));
        chestButton.Connect(Control.SignalName.MouseExited, Callable.From(nTestChestAnim.OnMouseExited));
        return nTestChestAnim;
    }
    
    protected IRunState? RunState { get; private set; }
    protected NTreasureRoom? TreasureRoomNode { get; private set; }
    
    protected virtual void OnChestButtonReleased(NButton nButton) { }
    protected virtual void OnMouseEntered() { }
    protected virtual void OnMouseExited() { }
}
