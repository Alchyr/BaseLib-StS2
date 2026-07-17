using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Common.Rewards.LinkedRewardSet;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;

namespace BaseLib.BaseLibScenes;

// Our own linked reward node. The scene is identical to the base games linked rewards scene.
// But having our own ensures base game changes to linked rewards will not cause issues.
[GlobalClass]
public partial class NCustomLinkedRewardSet : Control
{
    public static readonly StringName RewardClaimedSignalName = "RewardClaimed";

    [Signal]
    public delegate void RewardClaimedEventHandler(NCustomLinkedRewardSet customLinkedRewardSet);

    /// <summary>
    /// Similar case to <see cref="NRewardButtonEvents"/> only this class uses this. But technically anyone can now make use of the highlighting.
    /// Including other modders. <br/>
    /// Every NRewardButton gets the highlight node added, not just those used by CustomLinkedRewards.
    /// </summary>
    public static AddedNode<NRewardButton, NRewardHighlight> NHighlights = new((rewardButton) =>
    {
        var textureRec = new NRewardHighlight();
        // I am using the card glow texture and morph it into something that fits.
        // If need be, we can add our own glow-reward-button hdr texture.
        textureRec.Texture = PreloadManager.Cache.GetCompressedTexture2D("res://images/packed/card_template/card_frame_sdf.exr");
        rewardButton.AddChildSafely(textureRec);
        rewardButton.MoveChild(textureRec, rewardButton.GetNode("%Background").GetIndex());
        textureRec.Modulate = new Color("00f4fcfa");
        textureRec.MouseFilter = MouseFilterEnum.Ignore;
        textureRec.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        textureRec.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        textureRec.Scale = new Vector2(1.8f, 0.3f);
        textureRec.Position = new Vector2(-270f, -33f);
        return textureRec;
    });


    // We are using our own scene because we don't know how/when MegaCrit modifies theirs
    private static string ScenePath => $"res://BaseLib/scenes/linked_reward_set.tscn";
    private static string ChainImagePath => ImageHelper.GetImagePath("/ui/reward_screen/reward_chain.png");
    
    // The game groups some assets in AssetSets which exists to not clutter PreloadManager. We aren't using it yet.
    // public static IEnumerable<string> AssetPaths => [ScenePath, ChainImagePath];
    
    private NRewardsScreen _rewardsScreen;
    private Control _rewardContainer;
    private Control _chainsContainer;
    private bool _signalAlreadyReceived = false;
    private readonly List<NRewardButton> _rewardButtons = [];
    
    public CustomLinkedRewardSet CustomLinkedRewardSet { get; private set; }


    public static NCustomLinkedRewardSet Create(CustomLinkedRewardSet linkedReward, NRewardsScreen screen)
    {
        var nCustomLinkedRewardSet = PreloadManager.Cache.GetScene(ScenePath).Instantiate<NCustomLinkedRewardSet>(PackedScene.GenEditState.Disabled);
        nCustomLinkedRewardSet._rewardsScreen = screen;
        nCustomLinkedRewardSet.SetReward(linkedReward);
        return nCustomLinkedRewardSet;
    }
    
    public override void _Ready()
    {
        _rewardContainer = GetNode<Control>("%RewardContainer");
        _chainsContainer = GetNode<Control>("%ChainContainer");
        NRewardButtonEvents.Focused += OnFocused;
        NRewardButtonEvents.Unfocused += OnUnfocused;
        Reload();
    }

    public override void _ExitTree()
    {
        NRewardButtonEvents.Focused -= OnFocused;
        NRewardButtonEvents.Unfocused -= OnUnfocused;
    }
    

    private void SetReward(CustomLinkedRewardSet linkedReward)
    {
        CustomLinkedRewardSet = linkedReward;
        if (IsNodeReady())
        {
            Reload();
        }
    }

    private void Reload()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _rewardButtons.Clear();
        for (var i = 0; i < CustomLinkedRewardSet.Rewards.Count; i++)
        {
            var reward = CustomLinkedRewardSet.Rewards[i];
            var nRewardButton = NRewardButton.Create(reward, _rewardsScreen);
            _rewardButtons.Add(nRewardButton);
            nRewardButton.CustomMinimumSize -= Vector2.Right * 20f;
            _rewardContainer.AddChildSafely(nRewardButton);
            nRewardButton.Connect(NRewardButton.SignalName.RewardClaimed, Callable.From<NRewardButton>(GetReward));
            if (i >= CustomLinkedRewardSet.Rewards.Count - 1) continue;
            var textureRect = new TextureRect();
            textureRect.MouseFilter = MouseFilterEnum.Ignore;
            textureRect.Texture = PreloadManager.Cache.GetCompressedTexture2D(ChainImagePath);
            textureRect.Size = Vector2.One * 50f;
            _chainsContainer.AddChildSafely(textureRect);
            textureRect.GlobalPosition = _chainsContainer.GlobalPosition + Vector2.Down * (-8 + i * (5f + nRewardButton.CustomMinimumSize.Y));
        }
    }

    private void OnFocused(NRewardButton button)
    {
        if (!_rewardButtons.Contains(button)) return;
        switch (CustomLinkedRewardSet.LinkedRewardType)
        {
            case LinkedRewardType.Exclusive:
                foreach (var rewardButton in _rewardButtons)
                {
                    var rewardHighlight = NHighlights.Get(rewardButton);
                    rewardHighlight.AnimShow();
                    rewardHighlight.Modulate = rewardButton == button ? NRewardHighlight.gold : NRewardHighlight.red;
                }
                break;
            case LinkedRewardType.Bundled:
                foreach (var rewardHighlight in _rewardButtons.Select(rewardButton => NHighlights.Get(rewardButton)))
                {
                    rewardHighlight.AnimShow();
                    rewardHighlight.Modulate = NRewardHighlight.gold;
                }
               
                break;
            default:
                break;
        }
       
    }

    private void OnUnfocused(NRewardButton button)
    {
        if (!_rewardButtons.Contains(button)) return;
        foreach (var rewardButton in _rewardButtons)
            NHighlights.Get(rewardButton)?.AnimHide();
    }
    
    private void GetReward(NRewardButton button)
    {
        if (CustomLinkedRewardSet.LinkedRewardType == LinkedRewardType.Bundled)
        {
            if (_signalAlreadyReceived) return;
            _signalAlreadyReceived = true;
            _rewardButtons.Remove(button);
            foreach (var rewardButton in _rewardButtons)
            {
                rewardButton.GetReward();
            }
        }
        EmitSignal(RewardClaimedSignalName, this);
        this.QueueFreeSafely();
    }
}

/// <summary>
/// Currently only <see cref="NCustomLinkedRewardSet"/> subscribes to these. But technically anyone could subscribe to them. <br/>
/// If it turns out others will use these, we could also add the "OnPress" and "OnRelease". <br/>
/// Note that these are invoked after the original method fully ran.
/// </summary>
[HarmonyPatch]
public static class NRewardButtonEvents
{
    /// <summary>
    /// Invoked whenever a NRewardButton gets focused by the player
    /// </summary>
    public static event Action<NRewardButton> Focused;
    /// <summary>
    /// Invoked whenever a NRewardButton gets unfocused by the player
    /// </summary>
    public static event Action<NRewardButton> Unfocused;
    
    [HarmonyPatch(typeof(NRewardButton), "OnFocus")]
    [HarmonyPostfix]
    private static void OnFocusEvent(NRewardButton __instance)
    {
        Focused?.Invoke(__instance);
    }
    [HarmonyPatch(typeof(NRewardButton), "OnUnfocus")]
    [HarmonyPostfix]
    private static void OnUnfocusEvent(NRewardButton __instance)
    {
        Unfocused?.Invoke(__instance);
    }
}


[HarmonyPatch]
public static class NCustomLinkedRewardSetPatches
{
    
    // roughly at line 190 - 205 in the decompiled code
    // Add support for NCustomLinkedRewardSet
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NRewardsScreen), "_Ready")]
    private static IEnumerable<CodeInstruction> ReplaceConnectMethodWithCustom(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
    {
        var customLinkedRewardSetCheck = AccessTools.Method(typeof(NCustomLinkedRewardSetPatches), nameof(CustomLinkedRewardSetCheck));
        var nRewardButtonCreate = AccessTools.Method(typeof(NRewardButton), nameof(NRewardButton.Create));

        var matcher = new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(OpCodes.Call, nRewardButtonCreate))
                    .ThrowIfInvalid("Could not find NRewardButton.Create call (else-branch start)");
        matcher.Advance(-3); // Step back to the else-branch's real first instruction
        var optionField = (FieldInfo)matcher.InstructionAt(4).operand;
        var endLabel = matcher.InstructionAt(-1).operand;

        // This instruction is the actual jump target for the `if` check (item is LinkedRewardSet == false).
        // Detach its label so we can move it onto our own first instruction instead.
        var elseEntryLabels = new List<System.Reflection.Emit.Label>(matcher.Labels);
        matcher.Labels.Clear();

        var loadDisplayClass = matcher.Instruction.Clone().WithLabels(elseEntryLabels); // ldloc.s (now the real entry point)
        var loadRewardItem = matcher.InstructionAt(1).Clone(); // ldloc.s reward

        matcher.Insert([
                    loadDisplayClass,
                    new CodeInstruction(OpCodes.Ldflda, optionField),
                    loadRewardItem,
                    new CodeInstruction(OpCodes.Ldarg_0), // this
                    new CodeInstruction(OpCodes.Call, customLinkedRewardSetCheck),
                    new CodeInstruction(OpCodes.Brtrue, endLabel), // reward handled -> skip the rest of the else-branch
        ]); // insert before the (now unlabeled) original else-branch code

        return matcher.InstructionEnumeration();
    }


    private static bool CustomLinkedRewardSetCheck(ref Control option, Reward item, NRewardsScreen screen)
    {
        if (item is not CustomLinkedRewardSet clrs) return false;
        option = NCustomLinkedRewardSet.Create(clrs, screen);
        option.Connect(NCustomLinkedRewardSet.RewardClaimedSignalName, Callable.From<NCustomLinkedRewardSet>(screen.RewardCollectedFrom));
        return true;
    }
}
