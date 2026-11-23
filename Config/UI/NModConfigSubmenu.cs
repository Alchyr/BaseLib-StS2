using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BaseLib.Config.UI;

public partial class NModConfigSubmenu : NSubmenu
{
    private new NBackButton? _backButton;
    private NNativeScrollableContainer _leftScrollArea;
    private NMegaLineEdit _modListFilter;
    private VBoxContainer _modListVbox;
    private Control _modListPanel;
    private MegaRichTextLabel _modListTitle;

    private NNativeScrollableContainer _rightScrollArea;
    private VBoxContainer? _optionContainer;
    private Control _contentPanel;
    private MegaRichTextLabel _modTitle;
    private Tween? _fadeInTween;

    private ModConfig? _currentConfig;
    private double _saveTimer = -1;
    private bool _modLoadFailed;
    private bool _lastFocusOnModList = true;
    private const double AutosaveDelay = 5;

    // Every entry that can appear in the list. Filtering rebuilds the buttons from this list.
    private readonly Dictionary<string, ModConfig?> _modEntries = [];

    private bool _isUsingController;
    private double _navRepeatTimer;
    private StringName? _heldNavAction;
    private const float InitialRepeatDelay = 0.4f;
    private const float RepeatRate = 0.1f;

    private const float ModTitleHeight = 90f;
    private const float TopOffset = ModTitleHeight + 30f;

    private const float ModListPosition = 180f;
    private const float ModListWidth = 360f;

    // The filter sits between the title and the scrolling list; the list's clipped area starts below it.
    private const float FilterHeight = 64f;
    private const float FilterInset = 8f; // Horizontal room so the focus border isn't cut off at the sides
    private const float ListTopOffset = TopOffset + FilterHeight;

    private const float MaxRightSideWidth = 1200f; // Dynamically sized, but not above this (hurts UW readability)
    private const int ModConfigPadding = 16; // Padding between the clipper and the mod config content

    // Read when the screen is shown *and* after a modal (e.g. confirm Restore Defaults). Ensure we return
    // to the same side that was active prior.
    protected override Control? InitialFocusedControl =>
        _lastFocusOnModList ? GetModListFocusTarget() : _optionContainer?.FindFirstFocusable();

    public NModConfigSubmenu()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;

        _leftScrollArea = new NNativeScrollableContainer(ListTopOffset);
        _modListPanel = new Control
        {
            Name = "ModListContent",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _modListPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_leftScrollArea);

        // Aligned with the title's left/right edges, inset a little so the focus border isn't cut off
        const float filterWidth = ModListWidth - NNativeScrollableContainer.ScrollbarGutterWidth - 2 * FilterInset;
        _modListFilter = new NMegaLineEdit
        {
            Name = "ModListFilter",
            CustomMinimumSize = new Vector2(filterWidth, FilterHeight),
            PlaceholderText = LocString.GetIfExists("settings_ui", "BASELIB-MOD_CONFIG_SEARCH_PLACEHOLDER")
                ?.GetFormattedText() ?? "Search mods...",
            FocusMode = FocusModeEnum.All,
            ClearButtonEnabled = true,
        };
        _modListFilter.SetAnchorsPreset(LayoutPreset.TopLeft);
        _modListFilter.Position = new Vector2(ModListPosition + FilterInset, TopOffset);
        _modListFilter.Size = new Vector2(filterWidth, FilterHeight);
        _modListFilter.TextChanged += UpdateModFilter;
        _modListFilter.TextSubmitted += _ =>
        {
            var topButton = _modListVbox?.GetChildren().OfType<NModListButton>().FirstOrDefault();
            if (topButton == null) return;
            if (_modEntries[topButton.ModName] is not { } config) return;

            ModButtonClicked(topButton, config);
            if (_modListFilter.HasFocus())
                _modListFilter.ReleaseFocus();
        };

        _rightScrollArea = new NNativeScrollableContainer(TopOffset);
        _contentPanel = new Control
        {
            Name = "ModConfigContent",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _contentPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        AddChild(_rightScrollArea);

        var modsText = LocString.GetIfExists("settings_ui", "BASELIB-MOD_CONFIG_MODS_TITLE")
            ?.GetFormattedText() ?? "Mods";
        _modListTitle = CreateTitleControl("ModListTitle", $"[center]{modsText}[/center]", 0f);
        _modListTitle.OffsetLeft = ModListPosition;
        _modListTitle.OffsetRight = ModListPosition + ModListWidth - NNativeScrollableContainer.ScrollbarGutterWidth;

        var unknownModText = LocString.GetIfExists("settings_ui", "BASELIB-UNKNOWN_MOD_NAME")
            ?.GetFormattedText() ?? "Unknown mod name";
        _modTitle = CreateTitleControl("ModTitle", $"[center]{unknownModText}[/center]", 0f);
        _modListVbox = new VBoxContainer();
    }

    public override void _Ready()
    {
        AddChild(_modTitle);
        AddChild(_modListTitle);
        AddChild(_modListFilter); // Added after the scroll areas so it draws above them

        _modListPanel.AddChild(_modListVbox);
        _modListPanel.SetAnchorsPreset(LayoutPreset.TopLeft);

        // The list must be in the tree before it's populated, so focus neighbor paths to the filter resolve
        _leftScrollArea.AttachContent(_modListPanel);
        InitializeModList();

        _modListVbox.MinimumSizeChanged += () => {
             var size = new Vector2(_leftScrollArea.AvailableContentWidth, _modListVbox.GetMinimumSize().Y);
            _modListPanel.CustomMinimumSize = size;
            _modListPanel.Size = size; // Not a container, so it doesn't shrink on its own when the content does
        };

        _leftScrollArea.DisableScrollingIfContentFits();
        _rightScrollArea.AttachContent(_contentPanel);
        _rightScrollArea.DisableScrollingIfContentFits();

        _backButton = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/back_button"))
            .Instantiate<NBackButton>();
        _backButton.Name = "BackButton";
        AddChild(_backButton);

        _isUsingController = NControllerManager.Instance?.IsUsingButtonInputsCompatibility() ?? false;

        ConnectSignals();
        GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(RefreshSize));
        GetViewport().Connect(Viewport.SignalName.GuiFocusChanged, Callable.From<Control>(OnGlobalFocusChanged));
        NControllerManager.Instance?.Connect(NControllerManager.SignalName.MouseDetected,
            Callable.From(InputTypeChanged));
        NControllerManager.Instance?.Connect(NControllerManager.SignalName.ControllerDetected,
            Callable.From(InputTypeChanged));
    }

    private void InitializeModList()
    {
        foreach (var modConfig in ModConfigRegistry.GetAll().Where(mod => mod.VisibleInModList()))
            _modEntries.Add(GetModTitle(modConfig), modConfig);

        PopulateModList();
    }

    /// <summary>
    /// Rebuilds the mod list buttons from <see cref="_modEntries"/>, keeping only entries whose title contains
    /// <paramref name="filterText"/> (case-insensitive), then rewires focus neighbors and the active highlight.
    /// </summary>
    private void PopulateModList(string filterText = "")
    {
        // Never free the node that currently has focus; move it to the filter first.
        var focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner != null && _modListVbox.IsAncestorOf(focusOwner))
            _modListFilter.GrabFocus();

        foreach (var child in _modListVbox.GetChildren())
        {
            _modListVbox.RemoveChild(child);
            child.QueueFreeSafely();
        }

        // Spacer due to the fade effect at the top of the scroll area; also serves as the gap under the filter
        _modListVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        var filter = filterText.Trim();
        var buttons = new List<NModListButton>();
        var selfNodePath = new NodePath(".");

        foreach (var (modName, modConfig) in _modEntries)
        {
            if (filter.Length > 0 && !modName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var modButton = new NModListButton(modName);
            _modListVbox.AddChild(modButton);
            buttons.Add(modButton);

            if (modConfig != null)
            {
                modButton.Connect(NClickableControl.SignalName.Released, Callable.From<NModListButton>(button =>
                    ModButtonClicked(button, modConfig)));

                modButton.Connect(NClickableControl.SignalName.Focused,
                    Callable.From<NModListButton>(ModButtonFocused));
            }

            modButton.FocusNeighborLeft = selfNodePath;
            modButton.FocusNeighborRight = selfNodePath;
        }

        if (buttons.Count == 0)
        {
            var noResults = ModConfig.CreateRawLabelControl("[center]No mods found[/center]", 28);
            noResults.Name = "NoResultsLabel";
            noResults.FitContent = true;
            noResults.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            noResults.MouseFilter = MouseFilterEnum.Ignore;
            _modListVbox.AddChild(noResults);
        }

        // Add a spacer at the bottom due to the fade effect (the top one is added above)
        _modListVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        // Focus neighbors for controller navigation. The filter is the top of the list as far as navigation goes:
        // filter -> first mod ... last mod -> filter, and up from the first mod goes to the filter as well.
        _modListFilter.FocusNeighborLeft = selfNodePath;
        _modListFilter.FocusNeighborRight = selfNodePath;

        if (buttons.Count > 0)
        {
            var firstMod = buttons[0];
            var lastMod = buttons[^1];

            firstMod.FocusNeighborTop = firstMod.GetPathTo(_modListFilter);
            lastMod.FocusNeighborBottom = lastMod.GetPathTo(_modListFilter);

            _modListFilter.FocusNeighborBottom = _modListFilter.GetPathTo(firstMod);
            _modListFilter.FocusNeighborTop = _modListFilter.GetPathTo(lastMod);
        }
        else
        {
            _modListFilter.FocusNeighborBottom = selfNodePath;
            _modListFilter.FocusNeighborTop = selfNodePath;
        }

        // The buttons are new, so the highlight has to be reapplied (the active mod may be filtered out; that's fine)
        if (_currentConfig != null)
            SetHighlightedModButton(_currentConfig, instant: true);
    }

    private void UpdateModFilter(string filterText)
    {
        PopulateModList(filterText);
        _leftScrollArea.InstantlyScrollToTop();
    }

    private NModListButton? GetActiveModButton()
    {
        if (_currentConfig == null) return null;
        foreach (var button in _modListPanel.GetChild(0).GetChildren())
        {
            if (button is NModListButton listButton && listButton.ModName == GetModTitle(_currentConfig))
                return listButton;
        }

        return null;
    }

    /// <summary>
    /// Where focus should go when it enters the mod list side: the active mod's button, else the first visible
    /// result, else the filter (e.g. when the active mod is filtered out, or nothing matches).
    /// </summary>
    private Control GetModListFocusTarget() =>
        (Control?)GetActiveModButton()
        ?? (Control?)_modListVbox.GetChildren().OfType<NModListButton>().FirstOrDefault()
        ?? _modListFilter;

    private void ModButtonClicked(NModListButton button, ModConfig modConfig)
    {
        if (modConfig != _currentConfig)
            LoadModConfig(modConfig);

        if (!_isUsingController || _modLoadFailed) return;

        button.SetHotkeyIconVisible(true);
        Callable.From(() => { _optionContainer?.FindFirstFocusable()?.TryGrabFocus(); })
            .CallDeferred();
    }

    private void ModButtonFocused(NModListButton button)
    {
        SetBackButtonVisible(true);
    }

    private void FocusActiveModButton()
    {
        // Don't pull focus out of the filter while the user is typing (e.g. when the input device changes)
        if (GetViewport().GuiGetFocusOwner() == _modListFilter) return;

        Callable.From(() => GetModListFocusTarget().TryGrabFocus()).CallDeferred();
    }

    private void SetBackButtonVisible(bool visible)
    {
        if (_backButton == null) return;

        if (!visible) _backButton.Disable();
        else
        {
            // An early return in NClickableControl.Enable() causes a desync issue where _isEnabled is true, but the
            // button isn't enabled/visible. Bypass the return and *actually* enable the button.
            _backButton._isEnabled = false;
            _backButton.Enable();
        }
    }

    private void SetHighlightedModButton(ModConfig config, bool instant = false)
    {
        foreach (var button in _modListPanel.GetChild(0).GetChildren())
        {
            if (button is NModListButton listButton)
                listButton.SetActiveState(listButton.ModName == GetModTitle(config), instant);
        }
    }

    private void InputTypeChanged()
    {
        _isUsingController = NControllerManager.Instance?.IsUsingButtonInputsCompatibility() ?? false;
        SetBackButtonVisible(true);
        FocusActiveModButton();
    }

    private static bool IsBackEvent(InputEvent @event) =>
        @event.IsAction(MegaInput.cancel) ||
        @event.IsAction(MegaInput.pauseAndBack) ||
        @event.IsAction(MegaInput.back);

    public override void _Input(InputEvent @event)
    {
        // Back/Escape in the filter with text in it: clear the text instead of leaving the screen.
        if (IsBackEvent(@event) && GetViewport().GuiGetFocusOwner() == _modListFilter && _modListFilter.Text.Length > 0)
        {
            if (@event.IsReleased())
            {
                _modListFilter.Text = "";
                UpdateModFilter(""); // Setting Text from code doesn't emit TextChanged
            }

            AcceptEvent();
            return;
        }

        // Handle moving from the mod config list back to the mod list on back (e.g. B on Xbox controllers)
        base._Input(@event);
        if (_backButton?.IsEnabled == true) return;

        if (!@event.IsActionReleased(MegaInput.cancel) &&
            !@event.IsActionReleased(MegaInput.pauseAndBack) &&
            !@event.IsActionReleased(MegaInput.back)) return;

        // Ensure we're not in a modal dialog (such as Restore Defaults), on-screen keyboard, etc. that should handle this
        var focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner == null || _optionContainer?.IsAncestorOf(focusOwner) != true)
        {
            return;
        }

        FocusActiveModButton();
        AcceptEvent();
    }

    private void LoadModConfig(ModConfig config)
    {
        if (config.ModId != null)
            BaseLibConfig.LastModConfigModId = config.ModId;

        if (_optionContainer != null || _currentConfig != null)
            SaveAndClearCurrentMod();

        _currentConfig = config;
        config.ConfigChanged += OnConfigChanged;
        SetHighlightedModButton(config);

        // Recreate the container to ensure the previous mod can't change something persistent by mistake
        _optionContainer = CreateOptionContainer();
        _contentPanel.AddChild(_optionContainer);

        try
        {
            config.SetupConfigUI(_optionContainer);
            _modLoadFailed = false;
        }
        catch (Exception e)
        {
            _modLoadFailed = true;
            SaveAndClearCurrentMod();
            _currentConfig = config; // Cleared by the above, but should remain in this case

            _optionContainer = CreateOptionContainer();
            _contentPanel.AddChild(_optionContainer);

            var modName = GetModTitle(config);
            var message = $"[center]BaseLib failed setting up the mod config for {modName}.\n" +
                          "This is either because the mod set something up incorrectly, or a " +
                          "compatibility issue.\n" +
                          $"Try updating BaseLib and {modName}, if newer versions exist.[/center]";
            var errorLabel = ModConfig.CreateRawLabelControl(message, 32);

            errorLabel.FitContent = true;
            errorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            _optionContainer.AddChild(errorLabel);

            BaseLibMain.Logger.Error($"SetupConfigUI failed for mod {modName}: {e}");
        }

        try
        {
            var title = $"[center]{GetModTitle(config)}[/center]";
            _modTitle.SetTextAutoSize(title);

            RefreshSize();
            _rightScrollArea.InstantlyScrollToTop();

            ModConfig.ShowAndClearPendingErrors();
        }
        catch (Exception e)
        {
            ModConfig.ModConfigLogger.Error("An error occurred while loading the mod config screen.\n" +
                                            "Please report a bug at:\nhttps://github.com/Alchyr/BaseLib-StS2");
            BaseLibMain.Logger.Error(e.ToString());
            _stack.Pop();
        }
    }

    private VBoxContainer CreateOptionContainer()
    {
        var container = new VBoxContainer {
            Name = "VBoxContainer",
            CustomMinimumSize = new Vector2(0f, 0f),
            AnchorRight = 1f,
            GrowHorizontal = GrowDirection.End,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        container.AddThemeConstantOverride("separation", 8);
        container.MinimumSizeChanged += RefreshSize;
        return container;
    }

    private static MegaRichTextLabel CreateTitleControl(string name, string defaultText, float minimumWidth)
    {
        var title = ModConfig.CreateRawLabelControl(defaultText, 36);
        title.Name = name;
        title.AutoSizeEnabled = true;
        title.MaxFontSize = 64;
        title.CustomMinimumSize = new Vector2(minimumWidth, ModTitleHeight);

        title.SetAnchorsPreset(LayoutPreset.TopLeft);
        title.OffsetBottom = TopOffset - 10;
        title.OffsetTop = title.OffsetBottom - ModTitleHeight;

        return title;
    }

    private static string GetModTitle(ModConfig config)
    {
        // ModPrefix is "" for a config class declared in the global namespace (TypePrefix.GetPrefix
        // returns empty when Namespace is null), which ModConfig explicitly supports as long as the
        // constructor passed an explicit filename. Slicing [..^1] on "" threw ArgumentOutOfRange and
        // took down the whole mod-config screen, including its own error path.
        var prefix = config.ModPrefix;
        if (prefix.Length > 0 && prefix[^1] == TypePrefix.PrefixSplitChar)
            prefix = prefix[..^1];

        if (prefix.Length > 0)
        {
            var locKey = $"{prefix}.mod_title";
            var locStr = LocString.GetIfExists("settings_ui", locKey);
            if (locStr != null)
                return locStr.GetFormattedText();

            ModConfig.ModConfigLogger.Warn($"No {locKey} found in localization table, using fallback title");
        }

        var fallbackTitle = config.GetType().GetRootNamespace();
        if (string.IsNullOrWhiteSpace(fallbackTitle))
            fallbackTitle = config.GetType().Assembly.GetName().Name;
        // BaseLib ships settings_ui.json for deu/eng/jpn/rus/zhs only, so a language without that
        // table (ita/kor today) reaches this line with GetIfExists returning null when the game does
        // not fall back to eng. A missing name must not take down the whole screen, so use the same
        // literal-fallback shape as ModConfigPatch.
        if (string.IsNullOrWhiteSpace(fallbackTitle))
            fallbackTitle = LocString.GetIfExists("settings_ui", "BASELIB-UNKNOWN_MOD_NAME")
                ?.GetFormattedText() ?? "Unknown mod name";

        return fallbackTitle;
    }

    private void OnGlobalFocusChanged(Control newFocus)
    {
        if (!IsVisibleInTree()) return;

        // The filter lives outside the scroll area but belongs to the mod list side
        var focusOnModList = _leftScrollArea.IsAncestorOf(newFocus) || newFocus == _modListFilter;

        var focusMovedToModList = focusOnModList && !_lastFocusOnModList;
        var focusMovedToContent = !focusOnModList && _lastFocusOnModList;
        _lastFocusOnModList = focusOnModList;

        if (focusMovedToModList)
        {
            SetBackButtonVisible(true);

            foreach (var modButton in _modListVbox.GetChildren())
            {
                if (modButton is NModListButton listButton)
                    listButton.SetHotkeyIconVisible(false);
            }

            // Land on the active mod (or the first result / the filter if it's filtered out), but leave focus alone
            // if it's already on the filter or the target
            if (newFocus != _modListFilter && newFocus != GetModListFocusTarget())
                FocusActiveModButton();
        }
        else if (focusMovedToContent && _isUsingController)
        {
            SetBackButtonVisible(false);
        }
    }

    private void RefreshSize()
    {
        if (_optionContainer == null) return;

        var (screenWidth, screenHeight) = GetViewportRect().Size;

        // Handle the left hand side (mod list)
        _leftScrollArea.Position = new Vector2(ModListPosition, 0);
        _leftScrollArea.Size = new Vector2(ModListWidth, screenHeight);

        var leftContentWidth = _leftScrollArea.AvailableContentWidth;

        _modListPanel.CustomMinimumSize = new Vector2(leftContentWidth, _modListPanel.CustomMinimumSize.Y);
        _modListVbox.CustomMinimumSize = new Vector2(leftContentWidth, _modListVbox.CustomMinimumSize.Y);
        _modListVbox.Size = new Vector2(leftContentWidth, _modListVbox.Size.Y);

        // The rest of this method handles the right side spacing. It's complex, but behaves well with any aspect ratio.

        const float minimumGap = 32f;
        const float modListEnd = ModListPosition + ModListWidth;
        const float scrollbarGutter = 60f; // Space reserved for the scrollbar
        const float TotalPaddingWidth = ModConfigPadding * 2f;
        const float maxSettingsWidth = MaxRightSideWidth - scrollbarGutter - TotalPaddingWidth;

        var totalAvailableSpace = screenWidth - modListEnd;
        var spaceForSettings = totalAvailableSpace - 2 * minimumGap - scrollbarGutter - TotalPaddingWidth;
        var actualSettingsWidth = Mathf.Min(spaceForSettings, maxSettingsWidth);

        var leftoverSpace = totalAvailableSpace - actualSettingsWidth - scrollbarGutter - TotalPaddingWidth;
        var unallocatedSpace = leftoverSpace - 2 * minimumGap;

        var extraScrollbarSpacing = 0f;
        var centeringOffset = 0f;

        if (unallocatedSpace > 0)
        {
            // First, give breathing room to the scrollbar
            extraScrollbarSpacing = Mathf.Min(unallocatedSpace, 64f);
            unallocatedSpace -= extraScrollbarSpacing;

            // Then use whatever is left to center the entire block
            centeringOffset = unallocatedSpace / 2f;
        }

        var contentPosition = modListEnd + minimumGap + centeringOffset;
        var containerWidth = actualSettingsWidth + TotalPaddingWidth + extraScrollbarSpacing + scrollbarGutter;
        var containerPosition = contentPosition - ModConfigPadding;

        // Position and size the scroll area
        _rightScrollArea.Position = new Vector2(containerPosition, 0);
        _rightScrollArea.Size = new Vector2(containerWidth, screenHeight);

        // Offset the VBox by the padding amount instead of using a MarginContainer, which complicates things with its
        // strict auto-layout rules
        _optionContainer.Position = new Vector2(ModConfigPadding, 0);
        _optionContainer.CustomMinimumSize = new Vector2(actualSettingsWidth, 0);
        _optionContainer.Size = new Vector2(actualSettingsWidth, 0);

        var requiredHeight = _optionContainer.GetMinimumSize().Y;

        var paddedHeight = requiredHeight + 30f;

        // Emulate the game and add extra space at the bottom
        var clipperSize = _contentPanel.GetParent<Control>().Size;
        if (paddedHeight >= clipperSize.Y)
            paddedHeight += clipperSize.Y * 0.3f;

        // Update the internal container sizes, accounting for the scrollbar area
        var rightContentWidth = _rightScrollArea.AvailableContentWidth;
        _contentPanel.CustomMinimumSize = new Vector2(rightContentWidth, paddedHeight);
        _contentPanel.Size = new Vector2(rightContentWidth, paddedHeight);

        _optionContainer.Size = new Vector2(actualSettingsWidth, requiredHeight);

        // Force center the mod title over the actual settings
        _modTitle.OffsetLeft = contentPosition;
        _modTitle.OffsetRight = contentPosition + actualSettingsWidth;
        _modTitle.CustomMinimumSize = new Vector2(actualSettingsWidth, ModTitleHeight);
    }

    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();
        _contentPanel.Modulate = new Color(1f, 1f, 1f, 0f);

        _saveTimer = -1;

        if (_modListFilter.Text.Length > 0)
        {
            _modListFilter.Text = "";
            PopulateModList();
        }

        // Load the most recent config, or default to BaseLib
        var baseLibConfig = ModConfigRegistry.Get<BaseLibConfig>()!;
        var lastModId = BaseLibConfig.LastModConfigModId;
        var lastMod = !string.IsNullOrWhiteSpace(lastModId) ? ModConfigRegistry.Get(lastModId) : baseLibConfig;
        LoadModConfig(lastMod ?? baseLibConfig); // lastMod could be null if the mod is no longer loaded

        // Ensure back button is visible when switching between controller/mouse, etc.
        Callable.From(InputTypeChanged).CallDeferred();

        WaitForLayoutAndFadeIn();
    }

    private async void WaitForLayoutAndFadeIn()
    {
        // Wait for the layout: one frame is USUALLY but not always enough to avoid jerking.
        // try/catch due to async void.
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            _leftScrollArea.ScrollToFocusedControl(skipAnimation: true);
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error(e.ToString());
        }
        finally
        {
            if (IsInstanceValid(this) && IsInsideTree())
            {
                _fadeInTween?.Kill();
                _fadeInTween = CreateTween().SetParallel();
                _fadeInTween.TweenProperty(_contentPanel, "modulate", Colors.White, 0.5f)
                    .From(new Color(0, 0, 0, 0))
                    .SetEase(Tween.EaseType.Out)
                    .SetTrans(Tween.TransitionType.Cubic);
            }
        }
    }

    protected override void OnSubmenuHidden()
    {
        SaveAndClearCurrentMod();

        base.OnSubmenuHidden();
    }

    private void SaveAndClearCurrentMod()
    {
        if (_currentConfig != null) _currentConfig.ConfigChanged -= OnConfigChanged;
        SaveCurrentConfig();

        if (_optionContainer != null)
        {
            _optionContainer.MinimumSizeChanged -= RefreshSize;
            _optionContainer.QueueFreeSafely();
            _optionContainer = null;
        }
        
        if (_currentConfig is SimpleModConfig simpleModConfig)
            simpleModConfig.ClearUIEventHandlers();

        _currentConfig = null;

        if (ModConfig.ModConfigLogger.PendingUserMessages.Count > 0)
        {
            // The main menu will only show this when recreated; if a player goes from settings to play a game,
            // that is AFTER finishing the game. We need to show the error now, so let's check here, too.
            Callable.From(ModConfig.ShowAndClearPendingErrors).CallDeferred();
        }
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        _saveTimer = AutosaveDelay;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_isUsingController)
            CreateControllerNavEcho(delta);

        if (_saveTimer <= 0) return;
        _saveTimer -= delta;
        if (_saveTimer <= 0)
        {
            SaveCurrentConfig();
        }
    }

    // Send repeat events of up/down inputs to allow easier movement on controllers
    private void CreateControllerNavEcho(double delta)
    {
        var currentAction =
            Input.IsActionPressed(MegaInput.down) ? MegaInput.down :
            Input.IsActionPressed(MegaInput.up) ? MegaInput.up :
            null;

        if (currentAction != _heldNavAction)
        {
            _heldNavAction = currentAction;
            _navRepeatTimer = InitialRepeatDelay;
            return;
        }

        if (currentAction == null) return;

        _navRepeatTimer -= delta;
        if (_navRepeatTimer > 0) return;
        _navRepeatTimer = RepeatRate;

        Input.ParseInputEvent(new InputEventAction
        {
            Action = currentAction,
            Pressed = true
        });
    }

    private void SaveCurrentConfig()
    {
        _saveTimer = -1;
        if (_modLoadFailed)
            BaseLibMain.Logger.Warn($"Ignoring SaveCurrentConfig for {_currentConfig?.ModId}: UI setup failed");
        else
            _currentConfig?.Save();
    }

    public override void _ExitTree()
    {
        GetViewport().Disconnect(Viewport.SignalName.SizeChanged, Callable.From(RefreshSize));
        GetViewport().Disconnect(Viewport.SignalName.GuiFocusChanged, Callable.From<Control>(OnGlobalFocusChanged));
        NControllerManager.Instance?.Disconnect(NControllerManager.SignalName.MouseDetected,
            Callable.From(InputTypeChanged));
        NControllerManager.Instance?.Disconnect(NControllerManager.SignalName.ControllerDetected,
            Callable.From(InputTypeChanged));

        base._ExitTree();
    }
}