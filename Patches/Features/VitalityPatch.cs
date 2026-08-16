using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace BaseLib.Patches.Features;

public static class VitalityPatch
{

    private static readonly Color BlockOutlineColor = new("B4E2FF");
    private static readonly Color VitalityHeartColor = new ("FFC800");
    private static readonly Color VitalityTextOutlineColor = StsColors.rewardLabelGoldOutline;
    
    public static class VitalityField
    {
        /// <summary>
        /// !!IMPORTANT!! If intending to change this value, use SetVitality to avoid issues.
        /// </summary>
        private static readonly SpireField<Creature, int> TemporaryHp = new(() => 0);
        
        public static readonly SpireField<Creature, Action<int,int, Creature>?> VitalityChanged = new(() => null);
        public static readonly SpireField<Creature, Tween?> VitalityTween = new(() => null);
        internal static void SetVitality(Creature creature, int value)    
        {
            if (value < 0)
                throw new ArgumentException("Vitality must be positive ", nameof (value));
            if (TemporaryHp.Get(creature) == value)
                return;
            var tempHp = TemporaryHp.Get(creature);
            TemporaryHp.Set(creature, value);
            var vitalityChanged = VitalityChanged.Get(creature);
            vitalityChanged?.Invoke(tempHp, TemporaryHp.Get(creature), creature);
        }

        internal static int GetVitality(Creature creature)
        {
            return TemporaryHp.Get(creature);
        }
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
    public class HpInterceptPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            MethodInfo getCurrentHpInfo = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.CurrentHp));

            MethodInfo tempHp = AccessTools.Method(typeof(HpInterceptPatch), nameof(TemporaryHpHandler));

            codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Call, getCurrentHpInfo)
                )
                .ThrowIfInvalid("Couldn't find getCurrentHp method for TemporaryHpHandler")
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Call, tempHp),
                    new CodeInstruction(OpCodes.Stloc_2)
                );

            return codeMatcher.InstructionEnumeration();
        }

        private static int TemporaryHpHandler(Creature c, int num)
        {
            var tempHp = VitalityField.GetVitality(c);
            if (num >= tempHp)
            {
                num -= tempHp;
                VitalityField.SetVitality(c, 0);
            }
            else
            {
                VitalityField.SetVitality(c, tempHp - num);
                num = 0;
            }

            var absorbed = tempHp - VitalityField.GetVitality(c);
            if(absorbed > 0) PlayAbsorbFx(c, absorbed);
            
            return num;
        }
    }
    
    [HarmonyPatch(typeof(Hook))] 
    [HarmonyPatch(nameof(Hook.AfterCombatEnd))]
    public class CombatEndPatch 
    { 
        static void Postfix(ICombatState? combatState) 
        {
            foreach (Creature c in combatState?.Creatures)
            {
                VitalityField.SetVitality(c, 0);
            }
        }
    }
    
    // Visual Effect patching begins below.
    // Could use either this method (overriding base-game textures to make it appear correctly)
    // or just copying and creating a new instance for each.
    
    
    // this was just directly taken from the other PR because I was lazy. Not a particularly difficult to make on my own just didn't feel the need to bother.
    /// <summary>
    ///     Gold floating number for absorbed damage — the only feedback on a fully absorbed hit, since vanilla
    ///     shows no damage number and no hurt anim when the final HP loss is 0.
    /// </summary>
    internal static void PlayAbsorbFx(Creature target, int absorbed)
    {
        if (absorbed <= 0 || !CombatManager.Instance.IsInProgress)
            return;
        var vfx = NDamageNumVfx.Create(target, absorbed);
        if (vfx == null)
            return;
        vfx.Modulate = StsColors.gold; // the _Ready tween animates modulate gold -> cream, mimicking vanilla's red -> cream
        var label = vfx.GetNodeOrNull<MegaLabel>("Label");
        label?.AddThemeColorOverride("font_color", StsColors.gold);
        label?.AddThemeColorOverride("font_outline_color", StsColors.rewardLabelGoldOutline);
        var container = target.GetVfxContainer();
        if (container != null)
            container.AddChildSafely(vfx);
        else
            NRun.Instance?.GlobalUi.AddChildSafely(vfx);
    }
    
    [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
    public class TempHpOutline
    {
        [HarmonyPostfix]
        public static void SelfModulateOutline(Creature ____creature, Control ____blockOutline)
        {
            if (____creature.Block > 0 || VitalityField.GetVitality(____creature) <= 0)
            {
                ____blockOutline.SelfModulate = Colors.White;
                return;
            }

            ____blockOutline.Visible = true;
            var color = VitalityHeartColor;
            // Altering colors to avoid touching the modulate, but making the color appear correctly. 
            // Could use snowlie's method of rendering instead 
            color.R8 += 255 - BlockOutlineColor.R8;
            color.G8 += 255 - BlockOutlineColor.G8;
            color.B8 += 255 - BlockOutlineColor.B8;
            ____blockOutline.SelfModulate = color;
        }
    }
    
    [HarmonyPatch(typeof(NHealthBar), "RefreshText")]
    public class VitalityText
    {
        
        [HarmonyPostfix]
        public static void SelfModulateOutline(Creature ____creature, MegaLabel ____hpLabel)
        {
            if (____creature.Block > 0 || VitalityField.GetVitality(____creature) <= 0)
                return;

            ____hpLabel.AddThemeColorOverride(ThemeConstants.Label.FontColor, NHealthBar._defaultFontColor);
            ____hpLabel.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, VitalityTextOutlineColor);
        }
    }
    
    // Code courtesy of CanYou with alterations.
    [HarmonyPatch]
    public static class VitalityHealthBarPatch
    {
        public static readonly Dictionary<NHealthBar, (Control container, MegaLabel label)> VitalityUi = new();
        public static readonly Dictionary<Creature, NHealthBar> CreatureHealthBar = new();

        [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.SetCreature))]
        [HarmonyPostfix]
        public static void CreateVitalityUi(NHealthBar __instance, Control ____blockContainer)
        {
            var vitalityContainer = (Control)____blockContainer.Duplicate();
            vitalityContainer.Visible = false;
            vitalityContainer.Name = "VitalityContainer";

            // Swap block icon for heart, tinted yellow
            var icon = vitalityContainer.GetNode<TextureRect>("BlockIcon");
            icon.Texture = PreloadManager.Cache.GetTexture2D("BaseLib/images/ui/tempHP.png");
            icon.SelfModulate = VitalityHeartColor;

            var label = vitalityContainer.GetNode<MegaLabel>("BlockLabel");
            label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, VitalityTextOutlineColor);

            __instance.HpBarContainer.AddChild(vitalityContainer);
            vitalityContainer.SetAnchorsPreset(Control.LayoutPreset.CenterLeft, true);

            // Mirror to the right side
            vitalityContainer.Position = new Vector2(
                __instance.HpBarContainer.Size.X - vitalityContainer.Size.X,
                ____blockContainer.Position.Y);

            CreatureHealthBar[__instance._creature] = __instance;
            VitalityUi[__instance] = (vitalityContainer, label);
        }

        [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
        [HarmonyPostfix]
        public static void RefreshVitalityUi(NHealthBar __instance, Creature ____creature)
        {
            if (!VitalityUi.TryGetValue(__instance, out var ui)) return;

            if (VitalityField.GetVitality(____creature) > 0)
            {
                ui.container.Visible = true;
                ui.label.SetTextAutoSize(((int)VitalityField.GetVitality(____creature)).ToString());
            }
            else
            {
                ui.container.Visible = false;
            }
        }

        [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately")]
        [HarmonyPostfix]
        public static void SetUpVitalityOffset(NHealthBar __instance)
        {
            if (!VitalityUi.TryGetValue(__instance, out var ui)) return;

            ui.container.Position = new Vector2(
                __instance.HpBarContainer.Size.X - ui.container.Size.X + 9f,
                __instance._blockContainer.Position.Y);
        }
    }

    /* Disabled Vitality Healthbar due to janky issues with other Health Bar mechanics.
     Could potentially happen in the future but it isn't enough effort to be worth it imo.
    // Enables a Vitality "Overflow" on the bar where if it loops over it changes colors. Subject to change.
    private static readonly Color[] HbColors = 
        [Colors.Gold, Colors.Green, Colors.MediumAquamarine, Colors.MediumVioletRed];

    private static Color HealthBarColors(int i) => i > HbColors.Length - 1 ? HbColors[^1] : HbColors[i];
    public class VitalityForecast : IHealthBarForecastSource 
    { 
        public IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context) 
        { 
            var list = new List<HealthBarForecastSegment>(); 
            for (var i = 0; i <= VitalityField.GetVitality(context.Creature) / context.Creature.CurrentHp; i++) 
            { 
                list.Add(new HealthBarForecastSegment(
                    (int)VitalityField.GetVitality(context.Creature) - context.Creature.CurrentHp * i,
                    HealthBarColors(i), HealthBarForecastDirection.FromLeft, -i));
            }
            return list;
        }
    } */
    
    public static void AnimateInVitality(int oldVitality, int vitalityGain, Creature creature) 
    { 
        AnimateInVitality(oldVitality, vitalityGain, VitalityHealthBarPatch.CreatureHealthBar[creature]);
    }
    
    public static void AnimateInVitality(int oldVitality, int vitalityGain, NHealthBar healthBar) 
    {
        if (oldVitality != 0 || vitalityGain == 0) 
            return;
        if (!VitalityHealthBarPatch.VitalityUi.TryGetValue(healthBar, out var ui)) return; 
        ui.container.Visible = true; 
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) 
            return;
        var originalPosition = ui.container.Position = new Vector2(
            healthBar.HpBarContainer.Size.X - ui.container.Size.X + 9f,
            healthBar._blockContainer.Position.Y);
        ui.container.Modulate = StsColors.transparentWhite; 
        ui.container.Position = originalPosition - NHealthBar._blockAnimOffset; 
        VitalityField.VitalityTween.Get(healthBar._creature)?.Kill(); 
        VitalityField.VitalityTween.Set(healthBar._creature, healthBar.CreateTween().SetParallel()); 
         VitalityField.VitalityTween.Get(healthBar._creature)?
            .TweenProperty(ui.container, (NodePath)"modulate:a", 1f, 0.5).SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Sine);
        VitalityField.VitalityTween.Get(healthBar._creature)?
            .TweenProperty(ui.container, (NodePath)"position", originalPosition, 0.5)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        if (healthBar._creature.IsPlayer) healthBar.RefreshValues();
    }
    
    [HarmonyPatch] 
    public class NMultiplayerPlayerStatePatch 
    { 
        [HarmonyPatch(typeof(NMultiplayerPlayerState))] 
        [HarmonyPatch("_Ready")] 
        public class _ReadyPatch 
        { 
            static void Postfix(NMultiplayerPlayerState __instance) 
            { 
                VitalityField.VitalityChanged.Set(__instance.Player.Creature, 
                    VitalityField.VitalityChanged.Get(__instance.Player.Creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(NMultiplayerPlayerState))] 
        [HarmonyPatch("_ExitTree")] 
        public class _ExitTreePatch 
        { 
            static void Postfix(NMultiplayerPlayerState __instance) 
            { 
                VitalityField.VitalityChanged.Set(__instance.Player.Creature, 
                    VitalityField.VitalityChanged.Get(__instance.Player.Creature) - AnimateInVitality);
            }
        }
    }
    
    [HarmonyPatch]
    public class NCreatureStateDisplayPatch 
    { 
        [HarmonyPatch(typeof(NCreatureStateDisplay))] 
        [HarmonyPatch("SubscribeToCreatureEvents")] 
        public class SubscribeToCreatureEventsPatch 
        { 
            static void Postfix(NCreatureStateDisplay __instance) 
            { 
                if (__instance._creature == null) return; 
                VitalityField.VitalityChanged.Set(__instance._creature, 
                    VitalityField.VitalityChanged.Get(__instance._creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(NCreatureStateDisplay))] 
        [HarmonyPatch("_ExitTree")] 
        public class _ExitTreePatch 
        { 
            static void Postfix(NCreatureStateDisplay __instance) 
            { 
                if (__instance._creature == null) return; 
                VitalityField.VitalityChanged.Set(__instance._creature, 
                    VitalityField.VitalityChanged.Get(__instance._creature) - AnimateInVitality);
            }
        }
    }
    
    [HarmonyPatch]
    public class CombatStateTrackerPatch 
    { 
        [HarmonyPatch(typeof(CombatStateTracker))] 
        [HarmonyPatch("Subscribe")] 
        [HarmonyPatch([typeof(Creature)])]
        public class SubscribePatch 
        { 
            static void Postfix(Creature creature) 
            { 
                VitalityField.VitalityChanged.Set(creature, 
                    VitalityField.VitalityChanged.Get(creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(CombatStateTracker))] 
        [HarmonyPatch("Unsubscribe")] 
        [HarmonyPatch([typeof(Creature)])]
        public class UnsubscribePatch 
        { 
            static void Postfix(Creature creature) 
            { 
                VitalityField.VitalityChanged.Set(creature, 
                    VitalityField.VitalityChanged.Get(creature) - AnimateInVitality);
            }
        }
    }
}