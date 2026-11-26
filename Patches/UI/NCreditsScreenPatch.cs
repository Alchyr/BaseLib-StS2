using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Credits;

namespace BaseLib.Patches.UI;

/// <summary>
/// Single Harmony patch class for <see cref="NCreditsScreen"/>. Each hook just
/// delegates to <see cref="CreditsNav"/>.
/// </summary>
[HarmonyPatch(typeof(NCreditsScreen))]
public static class NCreditsScreenPatch
{
    /// <summary>Renders the modded blocks and side-nav after the screen is ready.</summary>
    [HarmonyPatch("_Ready"), HarmonyPostfix]
    public static void OnReady(NCreditsScreen __instance)
        => CreditsNav.Render(__instance);

    /// <summary>Redirects default focus onto the side-nav.</summary>
    [HarmonyPatch("DefaultFocusedControl", MethodType.Getter), HarmonyPostfix]
    public static void OnDefaultFocus(ref Control __result)
        => CreditsNav.RedirectFocus(ref __result);

    /// <summary>Cleans up the confirm binding when the screen leaves the tree.</summary>
    [HarmonyPatch("_ExitTree"), HarmonyPostfix]
    public static void OnExitTree(NCreditsScreen __instance)
        => CreditsNav.RemoveBindings(__instance);
}

/// <summary>
/// Rendering and navigation logic for the modded credits screen. Holds all the
/// non-Harmony work: building the (possibly nested) credit blocks, the fixed
/// side-nav, jump-scrolling, focus seeding, and the controller confirm binding.
/// Driven by <see cref="NCreditsScreenPatch"/>.
/// </summary>
internal static class CreditsNav
{
    private static readonly Color BannerColor   = new(1f, 0.55f, 0.20f);       // orange
    private static readonly Color ModNameColor  = new(0.53f, 0.81f, 0.92f);    // blue
    private static readonly Color NavColor      = new(1f, 0.965f, 0.886f);     // cream
    private static readonly Color NavHoverColor = new(0.937f, 0.784f, 0.318f); // gold

    /// <summary>Where a jumped-to node lands, measured from the top of the screen.</summary>
    private const float NavTopPadding = 200f;

    /// <summary>Confirm actions bound while the screen is open (last-wins over the screen's blockers).</summary>
    private static readonly string[] ConfirmActions = [(string)MegaInput.select];

    /// <summary>First side-nav button; used to seed controller/keyboard focus.</summary>
    private static Button? _firstNavButton;

    /// <summary>Confirm binding pushed into the hotkey manager while the screen is open.</summary>
    private static Action? _confirmBinding;

    /// <summary>Focused-button → jump action, so a confirm press triggers the right entry.</summary>
    private static readonly Dictionary<Button, Action> NavActions = [];

    /// <summary>Builds the modded credit blocks and the side navigation once the screen is ready.</summary>
    public static void Render(NCreditsScreen screen)
    {
        _firstNavButton = null;
        if (ModCredits.Entries.Count == 0) return;

        var margin = screen.GetNodeOrNull<Control>("%ScreenContents");
        var vbox = margin?.GetNodeOrNull<VBoxContainer>("VBoxContainer");
        if (margin == null || vbox == null) return;

        var headerTpl = vbox.GetNodeOrNull<MegaLabel>("ModdingSupportHeader");
        var namesTpl  = vbox.GetNodeOrNull<MegaRichTextLabel>("ModdingSupportNames");
        var greenTpl  = vbox.GetNodeOrNull<MegaLabel>("LocalizationHeader") ?? headerTpl;
        var rolesCTpl = vbox.GetNodeOrNull<HBoxContainer>("zhsContainer");
        var multiCTpl = vbox.GetNodeOrNull<HBoxContainer>("PlaytestersContainer");
        if (headerTpl == null || namesTpl == null) return;

        var anchor = vbox.GetNodeOrNull<Control>("Spacer3");
        var at = anchor?.GetIndex() ?? vbox.GetChildCount();

        // Side-nav targets: the vanilla top first, then one per mod.
        var navTargets = new List<(string Text, Control Target)>();
        var top = vbox.GetNodeOrNull<Control>("StS2Logo") ?? (Control)vbox.GetChild(0);
        navTargets.Add(("Slay the Spire 2", top));

        BuildHeader(vbox, ref at, ModCredits.Resolve("BASELIB-BANNER.name"), BannerColor, headerTpl, "Banner", null, 60);

        // Renders a section body (names) according to its layout.
        void EmitBody(ModCredits.Layout kind, string body, string tag)
        {
            switch (kind)
            {
                case ModCredits.Layout.Roles when rolesCTpl != null:
                    BuildRoles(vbox, ref at, body, rolesCTpl, tag);
                    break;
                case ModCredits.Layout.Columns3 when multiCTpl != null:
                    BuildMulti(vbox, ref at, body, multiCTpl, tag);
                    break;
                case ModCredits.Layout.Names:
                default:
                    BuildNames(vbox, ref at, body, namesTpl, tag);
                    break;
            }
        }

        var m = 0;
        foreach (var e in ModCredits.Entries)
        {
            var title = ModCredits.Resolve($"{e.ModId}-{e.ModId}.title");
            var titleNode = BuildHeader(vbox, ref at, title, ModNameColor, headerTpl, "Mod" + m, null, 44);
            navTargets.Add((title, titleNode));

            var s = 0;
            foreach (var sec in e.Sections)
                Emit(sec, e.ModId, "M" + m + "S" + s++);

            m++;
        }

        BuildSideNav(screen, margin, headerTpl, navTargets);
        return;

        // Recursive section renderer. Leaves render gold header + names. Groups render
        // a green header, optional own names (only if the .names key exists), then children.
        void Emit(ModCredits.Section sec, string modId, string tag)
        {
            var baseKey = modId + "-" + sec.Name.ToUpperInvariant();

            if (sec.IsGroup)
            {
                BuildHeader(vbox, ref at, ModCredits.Resolve(baseKey + ".header"), null, greenTpl, tag + "G", 60f);

                if (ModCredits.Has(baseKey + ".names"))
                    EmitBody(sec.Kind, ModCredits.Resolve(baseKey + ".names"), tag);

                var ci = 0;
                foreach (var child in sec.Children!)
                    Emit(child, modId, tag + "_" + ci++);
                return;
            }

            BuildHeader(vbox, ref at, ModCredits.Resolve(baseKey + ".header"), null, headerTpl, tag + "H", 60f);
            EmitBody(sec.Kind, ModCredits.Resolve(baseKey + ".names"), tag);
        }
    }

    /// <summary>
    /// Redirects the screen's default focus onto the side-nav, so a controller starts
    /// on the navigation list instead of the screen root (which traps directional input).
    /// </summary>
    public static void RedirectFocus(ref Control result)
    {
        if (_firstNavButton != null && GodotObject.IsInstanceValid(_firstNavButton))
            result = _firstNavButton;
    }

    /// <summary>Removes the confirm binding when the screen closes, so bindings don't stack.</summary>
    public static void RemoveBindings(NCreditsScreen creditsScreen)
    {
        if (_confirmBinding == null) return;
        foreach (var a in ConfirmActions)
            NHotkeyManager.Instance?.RemoveHotkeyPressedBinding(a, creditsScreen, _confirmBinding);
        _confirmBinding = null;
    }

    /// <summary>
    /// Builds the fixed left-hand navigation list: click-to-jump buttons, wrapped
    /// focus neighbours, seeded focus, and a hotkey-manager confirm binding so a
    /// controller press fires the focused entry.
    /// </summary>
    private static void BuildSideNav(NCreditsScreen creditsScreen, Control scrollContent,
        Control headerTpl, IReadOnlyList<(string Text, Control Target)> targets)
    {
        NavActions.Clear();

        var font = SafeThemeFont(headerTpl);

        var nav = new VBoxContainer { Name = "ModNav" };
        nav.AddThemeConstantOverride("separation", 10);
        creditsScreen.AddChild(nav);
        nav.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        nav.Position = new Vector2(64, 260);

        var buttons = new List<Button>();
        foreach (var (text, target) in targets)
        {
            var b = new Button
            {
                Text = text,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.All,
            };
            b.AddThemeFontSizeOverride("font_size", 24);
            if (font != null) b.AddThemeFontOverride("font", font);
            b.AddThemeColorOverride("font_color", NavColor);
            b.AddThemeColorOverride("font_hover_color", NavHoverColor);
            b.AddThemeColorOverride("font_focus_color", NavHoverColor);
            b.AddThemeColorOverride("font_pressed_color", NavHoverColor);

            var captured = target;
            Action jump = () => JumpTo(creditsScreen, scrollContent, captured);
            b.Pressed += jump;      // mouse click + native ui_accept
            NavActions[b] = jump;   // controller-confirm path (see confirm binding below)
            nav.AddChild(b);
            buttons.Add(b);
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            var up   = buttons[(i - 1 + buttons.Count) % buttons.Count];
            var down = buttons[(i + 1) % buttons.Count];
            buttons[i].FocusNeighborTop    = buttons[i].GetPathTo(up);
            buttons[i].FocusNeighborBottom = buttons[i].GetPathTo(down);
            buttons[i].FocusPrevious       = buttons[i].GetPathTo(up);
            buttons[i].FocusNext           = buttons[i].GetPathTo(down);
        }

        var viewport = creditsScreen.GetViewport();
        _confirmBinding = () =>
        {
            if (viewport?.GuiGetFocusOwner() is Button fb && NavActions.TryGetValue(fb, out var act))
                act();
        };
        foreach (var a in ConfirmActions)
            NHotkeyManager.Instance?.PushHotkeyPressedBindingCompatibility(a, creditsScreen, _confirmBinding);

        _firstNavButton = buttons.Count > 0 ? buttons[0] : null;
        _firstNavButton?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>
    /// Scrolls the credits so <paramref name="target"/> sits <see cref="NavTopPadding"/>
    /// below the top, by writing the screen's private <c>_targetPosition</c> field.
    /// </summary>
    private static void JumpTo(GodotObject creditsScreen, Control scrollContent, Control target)
    {
        var p = scrollContent.Position.Y - target.GlobalPosition.Y + NavTopPadding;
        Traverse.Create(creditsScreen).Field("_targetPosition").SetValue(p);
    }

    /// <summary>Reads the header template's font so nav buttons match the credits typeface.</summary>
    private static Font? SafeThemeFont(Control c)
    {
        try { return c.GetThemeFont("font"); }
        catch { return null; }
    }

    /// <summary>
    /// Clones the header template and inserts it, optionally overriding vertical
    /// spacing (<paramref name="minHeight"/>) and text size (<paramref name="fontSize"/>).
    /// Returns the created label so callers can reference it (e.g. as a nav target).
    /// </summary>
    private static MegaLabel BuildHeader(VBoxContainer vbox, ref int at, string? text,
        Color? color, MegaLabel tpl, string tag, float? minHeight = null, int? fontSize = null)
    {
        var h = (MegaLabel)tpl.Duplicate();
        h.UniqueNameInOwner = false;
        h.Name = "Mod_" + tag;
        h.Text = text ?? "";
        if (color.HasValue) h.AddThemeColorOverride("font_color", color.Value);
        if (minHeight.HasValue)
            h.CustomMinimumSize = new Vector2(h.CustomMinimumSize.X, minHeight.Value);
        if (fontSize.HasValue)
        {
            h.MaxFontSize = fontSize.Value;
            h.AddThemeFontSizeOverride("font_size", fontSize.Value);
        }
        vbox.AddChild(h);
        vbox.MoveChild(h, at++);
        return h;
    }

    /// <summary>Renders a single-column list of names (one per line).</summary>
    private static void BuildNames(VBoxContainer vbox, ref int at, string? body,
        MegaRichTextLabel tpl, string tag)
    {
        var n = (MegaRichTextLabel)tpl.Duplicate();
        n.UniqueNameInOwner = false;
        n.Name = "Mod_" + tag + "_N";
        n.Text = body ?? "";
        vbox.AddChild(n);
        vbox.MoveChild(n, at++);
    }

    /// <summary>Renders a two-column roles/names block from <c>Role||Name</c> lines.</summary>
    private static void BuildRoles(VBoxContainer vbox, ref int at, string? body,
        HBoxContainer tpl, string tag)
    {
        var c = (HBoxContainer)tpl.Duplicate();
        c.UniqueNameInOwner = false;
        c.Name = "Mod_" + tag + "_C";

        var labels = new List<MegaRichTextLabel>();
        foreach (var child in c.GetChildren())
            if (child is MegaRichTextLabel lbl) labels.Add(lbl);
        if (labels.Count < 2) { c.QueueFree(); return; }

        var roles = new List<string>();
        var names = new List<string>();
        foreach (var line in (body ?? "").Split('\n'))
        {
            var parts = line.Split(["||"], StringSplitOptions.None);
            if (parts.Length != 2) continue;
            roles.Add(parts[0].Trim());
            names.Add(parts[1].Trim());
        }

        labels[0].UniqueNameInOwner = false;  // roles column (blue)
        labels[0].Name = "Mod_" + tag + "_Roles";
        labels[0].Text = string.Join("\n", roles);
        labels[1].UniqueNameInOwner = false;  // names column (cream)
        labels[1].Name = "Mod_" + tag + "_Names";
        labels[1].Text = string.Join("\n", names);

        vbox.AddChild(c);
        vbox.MoveChild(c, at++);
    }

    /// <summary>Renders names across the three columns of the playtester template.</summary>
    private static void BuildMulti(VBoxContainer vbox, ref int at, string? body,
        HBoxContainer tpl, string tag)
    {
        var c = (HBoxContainer)tpl.Duplicate();
        c.UniqueNameInOwner = false;
        c.Name = "Mod_" + tag + "_C";

        var columns = new List<MegaRichTextLabel>();
        foreach (var child in c.GetChildren())
            if (child is MegaRichTextLabel lbl) columns.Add(lbl);
        if (columns.Count == 0) { c.QueueFree(); return; }

        var names = (body ?? "").Split('\n');
        var buckets = new List<string>[columns.Count];
        for (var i = 0; i < columns.Count; i++) buckets[i] = [];
        for (var i = 0; i < names.Length; i++)
            buckets[i % columns.Count].Add(names[i]);

        for (var i = 0; i < columns.Count; i++)
        {
            columns[i].UniqueNameInOwner = false;
            columns[i].Name = "Mod_" + tag + "_C" + i;
            columns[i].Text = string.Join("\n", buckets[i]);
        }

        vbox.AddChild(c);
        vbox.MoveChild(c, at++);
    }
}
