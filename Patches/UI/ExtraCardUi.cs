using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.Patches.UI;

/// <summary>
/// Adds elements to an NCard and updates them when the card is reloaded (<see cref="NCard.Reload"/>).
/// </summary>
[HarmonyPatch]
public class ExtraCardUi
{
    public enum CardUiPositioning
    {
        /// <summary>
        /// Placed along the card's left side, below the cost.
        /// </summary>
        Left,
        /// <summary>
        /// Placed like the star cost, around the card's energy cost.
        /// </summary>
        AroundCost
    }

    private const float Margin = 2f;
    private const float AroundCostDistance = 42f;
    
    /// <summary>
    /// Get the position for a control in a specified position.
    /// The control should already be in the <see cref="ActiveControls"/> list for the requested positioning.
    /// </summary>
    private static Vector2 VecForPosition(NCard card, CardUiPositioning positioning, List<Control> controls, Control requester)
    {
        var index = controls.IndexOf(requester);
        if (index < 0)
        {
            BaseLibMain.Logger.Warn($"Card UI element {requester} requesting position {positioning} " +
                                    $"not in list of controls for that position");
            return Vector2.Zero;
        }
        
        var count = controls.Count;
        
        switch (positioning)
        {
            case CardUiPositioning.Left:
                //center x is -166 + 36, starting y of enchant tab is -116 with a height of 54
                float baseY = -116f;
                if (card.EnchantmentTab.Visible)
                {
                    baseY += card.EnchantmentTab.Size.Y;
                    baseY += Margin;
                }
                
                foreach (var member in controls)
                {
                    if (member == requester)
                    {
                        return new Vector2(-130 - (member.Size.X * 0.5f), baseY);
                    }
                    baseY += member.Size.Y;
                    baseY += Margin;
                }
                break;
            case CardUiPositioning.AroundCost:
                if (card._starIcon.Visible)
                {
                    count++;
                    index++;
                }

                var center = card._energyIcon.Position + card._energyIcon.Size * 0.5f;
                var angle = (0.33f + index / (float) count) * MathF.Tau;
                var target = Vector2.FromAngle(angle) * AroundCostDistance;
                return center + target - (requester.Size * 0.5f);
            default:
                BaseLibMain.Logger.Warn($"Requested card UI position for unsupported position ({positioning.ToString()})");
                break;
        }
        return Vector2.Zero;
    }

    private static readonly Dictionary<CardUiPositioning, List<Func<NCard, Control>>> _controlsForPosition = [];
    private static List<Func<NCard, Control>> RegisteredControls(CardUiPositioning positioning)
    {
        if (!_controlsForPosition.TryGetValue(positioning, out var getters))
        {
            getters = [];
            _controlsForPosition[positioning] = getters;
        }
        return getters;
    }

    private static SpireField<Control, bool> _wasVisible = new(() => false);
    private static SpireField<Control, Tween> _positionTweens = new(() => null);

    /// <summary>
    /// Event for refreshing visibility/initial appearance of registered controls
    /// </summary>
    private static event Action<NCard>? CardReload;


    /// <summary>
    /// Register a control to be managed for card UI whose creation is handled externally.
    /// This will be added to all NCard instances, with visibility checked each time the card is reloaded.
    /// Recommended for use alongside an AddedNode as a simple way to link a node with each NCard instance.
    /// </summary>
    /// <param name="positioning">How the control should be positioned on the card.</param>
    /// <param name="getElement">Function that given an NCard, returns a child Control to be managed. This
    /// should return the same instance each time it is requested for a given NCard.</param>
    /// <param name="onReload">Called whenever the NCard is reloaded. Return value is whether the element
    /// should be visible.</param>
    public static void RegisterCardUiElement<T>(CardUiPositioning positioning, Func<NCard, T> getElement, 
        Func<NCard, CardModel?, T, bool> onReload) where T : Control
    {
        RegisteredControls(positioning).Add(getElement);
        CardReload += (node) =>
        {
            var control = getElement(node);
            control.Visible = onReload(node, node.Model, control);
        };
    }

    /// <summary>
    /// Register a control to be managed for card UI, which is created by the function passed to this method.
    /// This will be added to all NCard instances, with visibility checked each time the card is reloaded.
    /// necessary.
    /// If your control needs additional updates besides NCard.Reload, you will have to trigger them yourself.
    /// </summary>
    /// <param name="positioning">How the control should be positioned on the card.</param>
    /// <param name="makeControl">Function that creates a Control to be managed. This
    /// control will be made invisible and added as a child to all NCard instances.</param>
    /// <param name="onReload">Called whenever the NCard is reloaded. Return value is whether the element
    /// should be visible.</param>
    /// <returns>A function that returns the created control for a given NCard instance.</returns>
    public static Func<NCard, T> RegisterCreateCardUiElement<T>(CardUiPositioning positioning, Func<NCard, T> makeControl,
        Func<NCard, CardModel?, T, bool> onReload) where T : Control
    {
        AddedNode<NCard, T> registeredControlField = new((card) =>
        {
            var control = makeControl(card);
            
            control.VisibilityChanged += () => UpdatePositions(card, positioning);

            if (card.IsAncestorOf(control)) return control;
            
            var cardContainer = card.GetChild(0) ?? card;
            cardContainer.AddChild(control);

            //Changing position to before the star icon node.
            cardContainer.MoveChild(control, cardContainer.GetNode("%StarIcon").GetIndex());
            return control;
        });
        RegisteredControls(positioning).Add(registeredControlField.Get);
        CardReload += (node) =>
        {
            var control = registeredControlField[node];
            control.Visible = onReload(node, node.Model, control);
        };
        return card => registeredControlField[card];
    }

    /// <summary>
    /// Updates the positions of all controls with a certain positioning.
    /// </summary>
    static void UpdatePositions(NCard card, CardUiPositioning positioning)
    {
        List<Control> activeControls = [];
        foreach (var getControl in RegisteredControls(positioning))
        {
            var control = getControl(card);
            if (control.Visible) activeControls.Add(control);
            else _wasVisible[control] = false;
        }

        foreach (var control in activeControls)
        {
            _positionTweens[control]?.Kill();
            
            var targetPos = VecForPosition(card, positioning, activeControls, control);
            if (targetPos != control.Position)
            {
                if (_wasVisible[control])
                {
                    var tween = control.CreateTween();
                    tween.SetEase(Tween.EaseType.Out).TweenProperty(control, "position", targetPos, 0.2f);
                    _positionTweens[control] = tween;
                }
                else
                {
                    control.Position = targetPos;
                }
            }

            _wasVisible[control] = true;
        }
    }


    [HarmonyPatch(typeof(NCard), nameof(NCard.Reload))]
    [HarmonyTranspiler]
    static List<CodeInstruction> ReloadHook(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new CallMatcher(typeof(NCard).Method(nameof(NCard.UpdatePortrait))))
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(ExtraCardUi), nameof(OnReload))
            ]);
    }

    [HarmonyPatch(typeof(NCard), nameof(NCard._Ready))]
    [HarmonyPostfix]
    static void ListenStarVisibility(NCard __instance)
    {
        __instance._starIcon.VisibilityChanged += () => UpdatePositions(__instance, CardUiPositioning.AroundCost);
        __instance.EnchantmentTab.VisibilityChanged += () => UpdatePositions(__instance, CardUiPositioning.Left);
    }

    static void OnReload(NCard __instance)
    {
        CardReload?.Invoke(__instance);

        foreach (var controlSet in _controlsForPosition)
        {
            UpdatePositions(__instance, controlSet.Key);
        }
    }
    
    /*public static AddedNode<NCard, NAdditionalCostDisplay> Node = new((card) =>
    {
        //would probably suggest loading from scene rather than this manual setup
        var control = new NAdditionalCostDisplay();
        
        var tex = ResourceLoader.Load<Texture2D>("res://BaseLibTests/images/powers/power.png");
        
        var size = tex.GetSize();
        var texRect = new TextureRect();
        texRect.Name = tex.ResourcePath;
        texRect.Size = new(50, 50);
        texRect.Texture = tex;
        texRect.PivotOffset = size / 2f;
        texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        texRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        
        control.Size = new(50, 50);
        control.Position = new(-126, -231);
        control.AddChild(texRect);
        
        var label = new Label { Text = "1" };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        control.AddChild(label);

        //For cards specifically, this is necessary to use the CardContainer instead of the NCard parent node, 
        //which does not receive all the transforms.
        var cardContainer = card.GetChild(0)!;
        cardContainer.AddChild(control);

        //Changing position to before the star icon node.
        cardContainer.MoveChild(control, cardContainer.GetNode("%StarIcon").GetIndex());

        return control;
    });*/
}

