using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.UI;

//TODO - NCombatUi.AnimateOut

/// <summary>
/// When combat UI is initialized (<see cref="NCombatUi.Activate"/>), adds registered nodes.
/// Nodes registered with a specific position will have their position automatically managed.
/// Nodes can also manually request positions using the <see cref="RequestPosition"/> and <see cref="ManagePosition"/>
/// methods.
/// </summary>
[HarmonyPatch]
public class ExtraCombatUi
{
    public enum CombatUiPositioning
    {
        /// <summary>
        /// Default value; means an unmanaged position. Will return (0, 0) if requested.
        /// </summary>
        None,
        /// <summary>
        /// Placed above the player's draw pile.
        /// </summary>
        Left,
        /// <summary>
        /// Placed like the star counter, around the energy counter.
        /// </summary>
        AroundEnergy
    }

    private const float Margin = 2f;
    private const float AroundEnergyDistance = 60f;
    
    /// <summary>
    /// Get the position for a control in a specified position.
    /// The control should already be in the <see cref="ActiveControls"/> list for the requested positioning.
    /// </summary>
    private static Vector2 VecForPosition(NCombatUi ui, CombatUiPositioning positioning, Control requester)
    {
        List<Control> controls = ActiveControls(ui, positioning);
        var index = controls.IndexOf(requester);
        if (index < 0)
        {
            BaseLibMain.Logger.Warn($"UI element {requester} requesting position {positioning} " +
                                    $"not in list of controls for that position");
            return Vector2.Zero;
        }
        
        switch (positioning)
        {
            case CombatUiPositioning.Left:
                var baseY = 828f - Margin; //EnergyCounterContainer is at y 828
                foreach (var member in controls)
                {
                    baseY -= member.Size.Y;
                    if (member == requester)
                    {
                        return new Vector2(Math.Max(Margin, 64f - (requester.Size.X * 0.5f)), baseY);
                    }
                    baseY -= Margin;
                }
                break;
            case CombatUiPositioning.AroundEnergy:
                var count = controls.Count;
                if (ui._starCounter.Visible)
                {
                    count++;
                    index++;
                }
                var angle = (0.41f + index / (float) count) * MathF.Tau;
                var target = Vector2.FromAngle(angle) * AroundEnergyDistance;
                return target - (requester.Size * 0.5f) + ui.EnergyCounterContainer.Position + new Vector2(64, 64);
            default:
                BaseLibMain.Logger.Warn($"Requested combat UI position for unsupported position ({positioning.ToString()})");
                break;
        }
        return Vector2.Zero;
    }

    private static readonly NotNullSpireField<NCombatUi, Dictionary<CombatUiPositioning, List<Control>>> _autoPositionControls =
        new(() => []);
    private static List<Control> AutoPositionControls(NCombatUi ui,
        CombatUiPositioning positioning)
    {
        if (!_autoPositionControls[ui].TryGetValue(positioning, out var controls))
        {
            controls = [];
            _autoPositionControls[ui][positioning] = controls;
        }

        return controls;
    }

    private static readonly SpireField<Control, CombatUiPositioning> _lastRequestedPosition =
        new(() => CombatUiPositioning.None);
    private static readonly NotNullSpireField<NCombatUi, Dictionary<CombatUiPositioning, List<Control>>> _activeControls =
        new(() => []);
    private static List<Control> ActiveControls(NCombatUi ui,
        CombatUiPositioning positioning)
    {
        if (!_activeControls[ui].TryGetValue(positioning, out var controls))
        {
            controls = [];
            _activeControls[ui][positioning] = controls;
        }

        return controls;
    }
    
    private static SpireField<Control, Tween> _positionTweens = new(() => null);

    /// <summary>
    /// Event for adding elements to the combat UI, and adding managed elements to <see cref="_autoPositionControls"/>.
    /// </summary>
    private static event Action<NCombatUi, Player, CombatState>? CombatUiActivate;

    /// <summary>
    /// Add an element to in-combat UI. The provided function will be run at the start of combat, when the combat UI
    /// is initialized (<see cref="NCombatUi.Activate"/>), to allow a node to be added to the NCombatUi.
    /// </summary>
    public static void RegisterCombatUiElement(Action<NCombatUi, Player, CombatState> addElement)
    {
        CombatUiActivate += addElement;
    }

    /// <summary>
    /// Add an element to in-combat UI. The provided function will be run at the start of combat, when the combat UI
    /// is initialized. If positioning is set, the control will be positioned automatically based on its visibility
    /// setting and other elements using the same positioning.
    /// </summary>
    public static void RegisterCombatUiElement(Func<NCombatUi, Player, CombatState, Control?> addElement,
        CombatUiPositioning positioning = CombatUiPositioning.None)
    {
        CombatUiActivate += (ui, player, combat) =>
        {
            var control = addElement(ui, player, combat);
            if (control != null && positioning != CombatUiPositioning.None)
            {
                AutoPositionControls(ui, positioning).Add(control);
                control.VisibilityChanged += () => UpdatePositions(ui, positioning);
            }
        };
    }

    /// <summary>
    /// Gets the position for a control in the specified UI positioning, taking into account
    /// other controls using the same positioning.
    /// If the requesting control is not visible, its position will be dropped.
    /// Once this method is called, the position is "reserved" for the requesting control.
    /// </summary>
    public static Vector2 RequestPosition(NCombatUi ui, Control c, CombatUiPositioning positioning)
    {
        if (!c.Visible) positioning = CombatUiPositioning.None;

        if (positioning != _lastRequestedPosition[c])
        {
            //Remove old position
            ActiveControls(ui, _lastRequestedPosition[c]).Remove(c);
            ActiveControls(ui, positioning).Add(c);
        }
        _lastRequestedPosition[c] = positioning;

        return positioning == CombatUiPositioning.None ? Vector2.Zero : VecForPosition(ui, positioning, c);
    }

    /// <summary>
    /// Gets the position for a control in the specified UI positioning, taking into account
    /// other controls using the same positioning, and moves the control.
    /// If the requesting control is not visible, its position will be dropped.
    /// Once this method is called, the position is "reserved" for the requesting control.
    /// Returns true if returned position is different from the control's current position.
    /// </summary>
    public static void ManagePosition(NCombatUi ui, Control c, CombatUiPositioning positioning)
    {
        _positionTweens[c]?.Kill();
        
        if (!c.Visible) positioning = CombatUiPositioning.None;

        var oldPosition = _lastRequestedPosition[c];
        if (positioning != oldPosition)
        {
            //Remove old position
            ActiveControls(ui, oldPosition).Remove(c);
            ActiveControls(ui, positioning).Add(c);
        }
        _lastRequestedPosition[c] = positioning;

        var targetPos = positioning == CombatUiPositioning.None ? Vector2.Zero : VecForPosition(ui, positioning, c);

        if (targetPos != c.Position)
        {
            if (oldPosition == CombatUiPositioning.None)
            {
                //Set position directly
                c.Position = targetPos;
            }
            else
            {
                var tween = c.CreateTween();
                tween.SetEase(Tween.EaseType.Out).TweenProperty(c, "position", targetPos, 0.2f);
                _positionTweens[c] = tween;
            }
        }
    }

    public static void DropPosition(NCombatUi ui, Control c, bool triggerUpdate)
    {
        //Remove element from tracking. The control should remove itself from the scene; this method does not handle that.
        //Calling RequestPosition or ManagePosition with a position of None will have the same effect.
        var oldPosition = _lastRequestedPosition[c];
        if (oldPosition != CombatUiPositioning.None)
        {
            ActiveControls(ui, oldPosition).Remove(c);
        }
        _lastRequestedPosition[c] = CombatUiPositioning.None;

        if (triggerUpdate)
        {
            UpdatePositions(ui, oldPosition);
        }
    }

    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
    [HarmonyTranspiler]
    static List<CodeInstruction> AddUIElement(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new CallMatcher(typeof(GodotTreeExtensions).Method(nameof(GodotTreeExtensions.AddChildSafely))))
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.LoadArgument(1),
                CodeInstruction.Call(typeof(ExtraCombatUi), nameof(AddUIElementInsert))
            ]);
    }

    static void AddUIElementInsert(NCombatUi __instance, CombatState combatState)
    {
        var player = LocalContext.GetMe(combatState);
        var playerCombatState = player?.PlayerCombatState;
        if (player == null || playerCombatState == null)
        {
            BaseLibMain.Logger.Warn("Failed to add custom combat UI; local player or local player combat state null");
            return;
        }

        __instance._starCounter.VisibilityChanged += () => UpdatePositions(__instance, CombatUiPositioning.AroundEnergy);
        
        CombatUiActivate?.Invoke(__instance, player, combatState); //Add controls

        //After controls are added, update positions for automatically managed controls
        foreach (var controlSet in _autoPositionControls[__instance])
        {
            foreach (var control in controlSet.Value)
            {
                ManagePosition(__instance, control, controlSet.Key);
            }
        }
    }

    /// <summary>
    /// Updates the positions of all managed and currently active controls with a certain positioning.
    /// </summary>
    static void UpdatePositions(NCombatUi ui, CombatUiPositioning positioning)
    {
        foreach (var control in ActiveControls(ui, positioning))
        {
            if (!control.Visible) DropPosition(ui, control, false);
        }

        foreach (var control in AutoPositionControls(ui, positioning))
        {
            ManagePosition(ui, control, positioning);
        }
    }
}

