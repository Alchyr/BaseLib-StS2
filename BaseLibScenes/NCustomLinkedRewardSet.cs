using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;

namespace BaseLib.BaseLibScenes;

[GlobalClass]
public partial class NCustomLinkedRewardSet : Control
{
    public static readonly StringName RewardClaimedSignalName = "RewardClaimed";

    [Signal]
    public delegate void RewardClaimedEventHandler(NCustomLinkedRewardSet customLinkedRewardSet);

    private NRewardsScreen _rewardsScreen;

    private Control _rewardContainer;

    private Control _chainsContainer;

    public CustomLinkedRewardSet CustomLinkedRewardSet { get; private set; }

    //private static string ScenePath => SceneHelper.GetScenePath("/rewards/linked_reward_set");
    private static string ScenePath => $"res://BaseLib/scenes/linked_reward_set.tscn";

    private static string ChainImagePath => ImageHelper.GetImagePath("/ui/reward_screen/reward_chain.png");

    public static IEnumerable<string> AssetPaths => new string[2] { ScenePath, ChainImagePath };

    private bool _grabbedReward = false;
    private List<NRewardButton> rewardButtons = new();
    
    
    public override void _Ready()
    {
        _rewardContainer = GetNode<Control>("%RewardContainer");
        _chainsContainer = GetNode<Control>("%ChainContainer");
        Reload();
    }

    public static NCustomLinkedRewardSet Create(CustomLinkedRewardSet linkedReward, NRewardsScreen screen)
    {
        var nCustomLinkedRewardSet = PreloadManager.Cache.GetScene(ScenePath).Instantiate<NCustomLinkedRewardSet>(PackedScene.GenEditState.Disabled);
        nCustomLinkedRewardSet._rewardsScreen = screen;
        nCustomLinkedRewardSet.SetReward(linkedReward);
        return nCustomLinkedRewardSet;
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
        rewardButtons.Clear();
        for (var i = 0; i < CustomLinkedRewardSet.Rewards.Count; i++)
        {
            var reward = CustomLinkedRewardSet.Rewards[i];
            var nRewardButton = NRewardButton.Create(reward, _rewardsScreen);
            rewardButtons.Add(nRewardButton);
            nRewardButton.CustomMinimumSize -= Vector2.Right * 20f;
            _rewardContainer.AddChildSafely(nRewardButton);
            nRewardButton.Connect(NRewardButton.SignalName.RewardClaimed, Callable.From<NRewardButton>(GetReward));
            if (i >= CustomLinkedRewardSet.Rewards.Count - 1) continue;
            var textureRect = new TextureRect();
            textureRect.MouseFilter = MouseFilterEnum.Ignore;
            textureRect.Texture = PreloadManager.Cache.GetCompressedTexture2D(ChainImagePath);
            textureRect.Size = Vector2.One * 50f;
            _chainsContainer.AddChildSafely(textureRect);
            textureRect.GlobalPosition = _chainsContainer.GlobalPosition + Vector2.Down * i * (3f + nRewardButton.CustomMinimumSize.Y);
        }
    }

    private void GetReward(NRewardButton button)
    {
        if (CustomLinkedRewardSet.LinkedRewardType == LinkedRewardType.Bundled)
        {
            if (_grabbedReward) return;
            _grabbedReward = true;
            rewardButtons.Remove(button);
            foreach (var rewardButton in rewardButtons)
            {
                rewardButton.GetReward();
            }
        }
        //_rewardsScreen.RewardCollectedFrom(this);
        CustomLinkedRewardSet.OnSkipped();
        EmitSignal(RewardClaimedSignalName, this);
        this.QueueFreeSafely();
    }
}

[HarmonyPatch]
public static class NCustomLinkedRewardSetPatches
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NRewardsScreen), "_Ready")]
    private static IEnumerable<CodeInstruction> ReplaceConnectMethodWithCustom(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
    {
        var customLinkedRewardSetCheck = AccessTools.Method(typeof(NCustomLinkedRewardSetPatches), nameof(CustomLinkedRewardSetCheck));
        var nRewardButtonCreate = AccessTools.Method(typeof(NRewardButton), nameof(NRewardButton.Create));

        var matcher = new CodeMatcher(instructions)
                    // Anchor on the unique call that starts the `else` branch:
                    // option = NRewardButton.Create(item, this);
                    .MatchStartForward(new CodeMatch(OpCodes.Call, nRewardButtonCreate))
                    .ThrowIfInvalid("Could not find NRewardButton.Create call (else-branch start)");

        // Step back to the else-branch's real first instruction: ldloc.s V_7
        matcher.Advance(-3);

        var optionField = (FieldInfo)matcher.InstructionAt(4).operand; // grab the real field, no guessing
        var endLabel = matcher.InstructionAt(-1).operand; // the preceding br.s's target = IL_037f

        // This instruction is the actual jump target for the `if` check (item is LinkedRewardSet == false).
        // Detach its label so we can move it onto our own first instruction instead.
        var elseEntryLabels = new List<System.Reflection.Emit.Label>(matcher.Labels);
        matcher.Labels.Clear();

        var loadDisplayClass = matcher.Instruction.Clone().WithLabels(elseEntryLabels); // ldloc.s V_7 (now the real entry point)
        var loadRewardItem = matcher.InstructionAt(1).Clone(); // ldloc.s reward

        var newInstructions = new List<CodeInstruction>
        {
                    loadDisplayClass, // push V_7
                    new CodeInstruction(OpCodes.Ldflda, optionField), // push &V_7.option
                    loadRewardItem, // push reward
                    new CodeInstruction(OpCodes.Ldarg_0), // push this
                    new CodeInstruction(OpCodes.Call, customLinkedRewardSetCheck),
                    new CodeInstruction(OpCodes.Brtrue, endLabel), // handled -> skip the rest of the else-branch
        };

        matcher.Insert(newInstructions); // insert before the (now unlabeled) original else-branch code

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