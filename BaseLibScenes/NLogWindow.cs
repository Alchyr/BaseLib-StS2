using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BaseLib.Config;
using BaseLib.ConsoleCommands;
using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace BaseLib.BaseLibScenes;

[GlobalClass]
public partial class NLogWindow : Window
{
    private static readonly Lock _logLock = new();
    private static ImmutableList<NLogWindow> _listeners = ImmutableList<NLogWindow>.Empty;
    private static bool _openedOnErr = false;

    // Hard cap on retained log lines. Must comfortably exceed the max display size
    // (the LimitedLogSize slider tops out at 2048) so RegenText always has enough history.
    private const int MaxBufferedLines = 8192;
    // Trim in chunks so the front-removal cost is amortized to ~O(1) per added line.
    private const int TrimChunk = 1024;

    private static readonly List<string> _fullLog = [];
    // Logical index of _fullLog[0] (i.e. how many lines have been dropped off the front).
    private static int _logBaseIndex = 0;
    // Logical total line count ever seen = _logBaseIndex + _fullLog.Count. Monotonically increasing.
    private static int _fullLogCount = 0;

    public int Limit { get; private set; } = BaseLibConfig.LimitedLogSize;
    // Logical index of the next line this window needs to render.
    private int _writeIndex = 0;

    public static bool IsOpen => _listeners.Count > 0;

    public static void AddLog(string msg)
    {
        lock (_logLock)
        {
            _fullLog.Add(msg);
            if (_fullLog.Count > MaxBufferedLines + TrimChunk)
            {
                int remove = _fullLog.Count - MaxBufferedLines;
                _fullLog.RemoveRange(0, remove);
                _logBaseIndex += remove;
            }
            _fullLogCount = _logBaseIndex + _fullLog.Count;
        }

        // SetDirty only sets a managed bool, so it is safe to call from the background
        // threads the engine logs (and therefore this listener) can run on.
        foreach (var window in _listeners)
        {
            window.SetDirty();
        }
    }

    public static void OpenOnErr()
    {
        if (!BaseLibConfig.OpenLogWindowOnError || IsOpen || _openedOnErr) return;
        _openedOnErr = true;
        Callable.From(() => OpenLogWindow.OpenWindow(true)).CallDeferred();
    }

    private ScrollContainer? _scrollContainer;
    private RichTextLabel? _logLabel;
    private Label? _logLevelLabel;
    private OptionButton? _logLevelDropdown;
    private LineEdit? _filterInput;
    private Button? _regexButton;
    private Button? _inverseButton;

    private string _filterText = "";
    private Regex? _regex;
    private bool _settingChanged;

    private bool _isFollowingLog = true;
    private int _currentFontSize; // Set on load
    private bool _needsRefresh;
    private double _timeSinceRefresh;

    private void SetDirty() => _needsRefresh = true;

    #region setup

    public override void _EnterTree()
    {
        base._EnterTree();
        ImmutableInterlocked.Update(ref _listeners, list => list.Add(this));
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        ImmutableInterlocked.Update(ref _listeners, list => list.Remove(this));
        if (_listeners.Count == 0) _openedOnErr = false;
    }

    public override void _Ready()
    {
        // Fix hilarious issue of resting causing the log window to fade to gray
        OwnWorld3D = true;

        base._Ready();

        _scrollContainer = GetNode<ScrollContainer>("MainVBox/Scroll");
        _logLabel = GetNode<RichTextLabel>("MainVBox/Scroll/Log");
        _logLevelLabel = GetNode<Label>("MainVBox/TopBarContainer/TopBarHBox/LogLevelLabel");
        _logLevelDropdown = GetNode<OptionButton>("MainVBox/TopBarContainer/TopBarHBox/LogLevelOption");
        _filterInput = GetNode<LineEdit>("MainVBox/TopBarContainer/TopBarHBox/FilterText");
        _regexButton = GetNode<Button>("MainVBox/TopBarContainer/TopBarHBox/RegexButton");
        _inverseButton = GetNode<Button>("MainVBox/TopBarContainer/TopBarHBox/InverseButton");

        _logLabel.AddThemeFontOverride("normal_font", ResourceLoader.Load<Font>("res://fonts/source_code_pro_medium.ttf"));

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            _logLevelDropdown.AddItem(level.ToString());
        }

        _logLevelDropdown.Selected = BaseLibConfig.LastLogLevel;
        _regexButton.ButtonPressed = BaseLibConfig.LogUseRegex;
        _inverseButton.ButtonPressed = BaseLibConfig.LogInvertFilter;
        _filterInput.Text = BaseLibConfig.LogLastFilter;
        _currentFontSize = BaseLibConfig.LogFontSize;

        _filterInput.TextChanged += (_) => { _settingChanged = true; UpdateFilter(); };
        _regexButton.Toggled += (_) => { _settingChanged = true; UpdateFilter(); };
        _inverseButton.Toggled += (_) => { _settingChanged = true; RegenText(); ScrollToBottomAsync(); };
        _logLevelDropdown.ItemSelected += (_) => { _settingChanged = true; RegenText(); ScrollToBottomAsync(); };

        SizeChanged += OnSizeChanged;
        CloseRequested += QueueFree;
        _logLabel.Finished += () => { if (_isFollowingLog) ScrollToBottomAsync(); };

        var scrollbar = _scrollContainer.GetVScrollBar();
        scrollbar.ValueChanged += OnScrollbarValueChanged;

        _isFollowingLog = true;

        SetFontSize(_currentFontSize, false);
        ApplyMinSizeForScale();
        UpdateFilter(); // Also calls Refresh()

        ProcessMode = ProcessModeEnum.Always;
    }

    private void ApplyMinSizeForScale()
    {
        float s = ContentScaleFactor > 0f ? ContentScaleFactor : 1f;
        MinSize = new Vector2I((int)(360 * s), (int)(66 * s));
    }

    private void ApplyChromeFontSize(int size)
    {
        _logLevelLabel?.AddThemeFontSizeOverrideAll(size);
        _logLevelDropdown?.AddThemeFontSizeOverrideAll(size);
        _filterInput?.AddThemeFontSizeOverrideAll(size);
        _regexButton?.AddThemeFontSizeOverrideAll(size);
        _inverseButton?.AddThemeFontSizeOverrideAll(size);

        int dim = Mathf.Max(28, (int)(size * 1.25f));
        if (_regexButton is not null)
            _regexButton.CustomMinimumSize = new Vector2(dim, dim);
        if (_inverseButton is not null)
            _inverseButton.CustomMinimumSize = new Vector2(dim, dim);
    }

    private void OnSizeChanged()
    {
        BaseLibConfig.LogLastSizeX = Size.X;
        BaseLibConfig.LogLastSizeY = Size.Y;
        SetDirty();
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what != NotificationWMPositionChanged) return;

        BaseLibConfig.LogLastPosX = Position.X;
        BaseLibConfig.LogLastPosY = Position.Y;
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    #endregion

    public override void _Process(double delta)
    {
        base._Process(delta);
        
        _timeSinceRefresh += delta;
        if (!_needsRefresh || !Visible || Mode == ModeEnum.Minimized) return;
        if (_timeSinceRefresh < 1d / 30d) return;

        _timeSinceRefresh = 0;
        _needsRefresh = false;

        if (BaseLibConfig.LimitedLogSize > Limit)
        {
            Limit = BaseLibConfig.LimitedLogSize;
            RegenText();
        }
        else
        {
            Limit = BaseLibConfig.LimitedLogSize;
            Refresh();
        }
    }

    private void UpdateFilter()
    {
        _filterText = _filterInput?.Text ?? "";

        if (_regexButton?.ButtonPressed != true || string.IsNullOrEmpty(_filterText))
            _regex = null;
        else
        {
            try
            {
                _regex = new Regex(_filterText, RegexOptions.IgnoreCase);
                _filterInput?.RemoveThemeColorOverride("font_color");
            }
            catch
            {
                _filterInput?.AddThemeColorOverride("font_color", new Color(1, 0.4f, 0.4f));
            }
        }

        RegenText();

        // Jump to the end on filter changes. If we ARE following, Refresh does this.
        if (!_isFollowingLog) ScrollToBottomAsync();
    }

    public void RegenText()
    {
        _logLabel?.Clear();
        int validLineCount = 0;

        lock (_logLock)
        {
            // Default to rendering everything currently buffered. This also handles the
            // empty-buffer case: _writeIndex == _fullLogCount, so UpdateText reads nothing.
            _writeIndex = _logBaseIndex;

            for (int i = _fullLog.Count - 1; i >= 0; i--)
            {
                if (!MatchesFilter(_fullLog[i])) continue;

                ++validLineCount;
                if (validLineCount >= BaseLibConfig.LimitedLogSize)
                {
                    _writeIndex = _logBaseIndex + i; // logical index of this line
                    break;
                }
            }
        }
        Refresh();
    }

    public void Refresh()
    {
        if (!IsNodeReady()) return;
        
        UpdateText();

        if (!_settingChanged) return;

        _settingChanged = false;
        BaseLibConfig.LastLogLevel = _logLevelDropdown!.Selected;
        BaseLibConfig.LogInvertFilter = _inverseButton!.ButtonPressed;
        BaseLibConfig.LogUseRegex = _regexButton!.ButtonPressed;
        BaseLibConfig.LogLastFilter = _filterText;
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    private void UpdateText()
    {
        if (!IsNodeReady()) return;
        if (_logLabel is null || _scrollContainer is null || _logLevelDropdown is null) return;

        _isFollowingLog = _isFollowingLog || IsNearBottom();

        var minLevel = (LogLevel)_logLevelDropdown.Selected;
        
        while (_writeIndex < _fullLogCount)
        {
            string line;
            lock (_logLock)
            {
                // If this window fell so far behind that its next line was already trimmed
                // off the front, skip ahead to the oldest line still retained.
                if (_writeIndex < _logBaseIndex) _writeIndex = _logBaseIndex;
                if (_writeIndex >= _fullLogCount) break;
                line = _fullLog[_writeIndex - _logBaseIndex];
            }
            if (MatchesFilter(line))
            {
                RenderLine(line, minLevel, _logLabel);
            }
            ++_writeIndex;
        }

        var limit = Math.Max(1, BaseLibConfig.LimitedLogSize);
        var safety = 64;
        while (_logLabel.GetParagraphCount() > limit && safety > 0)
        {
            _logLabel.RemoveParagraph(0);
            --safety;
        }
        if (_isFollowingLog) ScrollToBottomAsync();
    }

    private async void ScrollToBottomAsync()
    {
        try
        {
            // If we got here because RichTextLabel.Finished fired, we still need to draw the frame
            // before scroll offsets are valid
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            if (_scrollContainer is null) return;

            var scrollbar = _scrollContainer.GetVScrollBar();
            _scrollContainer.ScrollVertical = (int)scrollbar.MaxValue;
            _isFollowingLog = true;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private bool MatchesFilter(string line)
    {
        if (string.IsNullOrEmpty(_filterText)) return true;
        var isMatch = _regex?.IsMatch(line) ?? line.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        return _inverseButton?.ButtonPressed == true ? !isMatch : isMatch;
    }

    private void OnScrollbarValueChanged(double value)
    {
        if (_scrollContainer is null) return;
        
        _isFollowingLog = IsNearBottom(_scrollContainer.GetVScrollBar(), value);
    }

    private bool IsNearBottom()
    {
        if (_scrollContainer is null) return true;

        var scrollbar = _scrollContainer.GetVScrollBar();
        return IsNearBottom(scrollbar, scrollbar.Value);
    }

    private static bool IsNearBottom(VScrollBar scrollbar, double value)
    {
        double bottomValue = scrollbar.MaxValue - scrollbar.Page;
        return bottomValue - value <= 8;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { CtrlPressed: true } mouseEvent) return;
        if (mouseEvent.ButtonIndex != MouseButton.WheelUp && mouseEvent.ButtonIndex != MouseButton.WheelDown) return;
        if (!mouseEvent.IsReleased()) return; // Don't double-count: pressed, then released
        ChangeFontSize(mouseEvent.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
        GetViewport().SetInputAsHandled();
    }

    private void ChangeFontSize(int deltaPx) =>
        SetFontSize(Math.Clamp(BaseLibConfig.LogFontSize + deltaPx, 8, 48));

    private void SetFontSize(int newSize, bool save = true)
    {
        _logLabel?.AddThemeFontSizeOverrideAll(newSize);
        ApplyChromeFontSize(newSize);
        _currentFontSize = newSize;
        ScrollToBottomAsync();

        if (!save) return;
        BaseLibConfig.LogFontSize = newSize;
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    private static readonly Color ErrorColor = Color.FromHtml("#ff6d6d");
    private static readonly Color WarnColor = Color.FromHtml("#ffd866");
    private static readonly Color DebugColor = Color.FromHtml("#7fdfff");

    private static void RenderLine(string line, LogLevel minLevel, RichTextLabel? label)
    {
        if (label is null) return;
        if (TryGetBracketLevel(line) < minLevel) return;

        label.AddText(line);
    }

    private static LogLevel TryGetBracketLevel(string line)
    {
        if (!line.StartsWith('[')) return LogLevel.Info;

        int closeIndex = line.IndexOf(']');
        if (closeIndex <= 1) return LogLevel.Info;

        var levelStr = line[1..closeIndex];
        return Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var level)
            ? level
            : LogLevel.Error; // Default to error to ensure it's shown
    }

    private static Color? GetColorForLine(string line) => TryGetBracketLevel(line) switch
    {
        LogLevel.Error => ErrorColor,
        LogLevel.Warn => WarnColor,
        LogLevel.Info => null,
        _ => DebugColor, // VeryDebug, Load, Debug
    };
}