using Godot;

namespace BaseLib.BaseLibScenes;

[GlobalClass]
public partial class NLogWindow : Window
{
    private static readonly LimitedLog _log = new(256);
    private static readonly List<NLogWindow> _listeners = [];

    public static void AddLog(string msg)
    {
        _log.Enqueue(msg);
        foreach (var window in _listeners)
        {
            window.Refresh();
        }
    }

    private ScrollContainer? _scrollContainer;
    private RichTextLabel? _logLabel;
    private bool _isFollowingLog = true;

    public override void _EnterTree()
    {
        base._EnterTree();
        _listeners.Add(this);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _listeners.Remove(this);
    }

    public override void _Ready()
    {
        base._Ready();

        _scrollContainer = GetNode<ScrollContainer>("Scroll");
        _logLabel = GetNode<RichTextLabel>("Scroll/Log");

        SizeChanged += UpdateText;
        CloseRequested += QueueFree;

        var scrollbar = _scrollContainer.GetVScrollBar();
        scrollbar.ValueChanged += OnScrollbarValueChanged;

        _isFollowingLog = true;
        Refresh();
    }

    public void Refresh()
    {
        UpdateText();
    }

    public void UpdateText()
    {
        if (_logLabel is null || _scrollContainer is null) return;

        _isFollowingLog = _isFollowingLog || IsNearBottom();

        _log.Render(_logLabel);

        if (_isFollowingLog)
        {
            CallDeferred(nameof(ScrollToBottom));
        }
    }

    private void ScrollToBottom()
    {
        if (_scrollContainer is null) return;

        _scrollContainer.ScrollVertical = (int)_scrollContainer.GetVScrollBar().MaxValue;
        _isFollowingLog = true;
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

    private class LimitedLog : Queue<string>
    {
        private int Limit { get; }

        private static readonly Color ErrorColor = Color.FromHtml("#ff6d6d");
        private static readonly Color WarnColor = Color.FromHtml("#ffd866");
        private static readonly Color DebugColor = Color.FromHtml("#7fdfff");

        public LimitedLog(int limit) : base(limit)
        {
            Limit = limit;
        }

        public new void Enqueue(string item)
        {
            while (Count >= Limit)
            {
                Dequeue();
            }
            base.Enqueue(item);
        }

        public void Render(RichTextLabel label)
        {
            label.Clear();

            foreach (var line in this)
            {
                var color = GetColorForLine(line);
                if (color is not null)
                {
                    label.PushColor(color.Value);
                }

                label.AddText(line);
                label.Newline();

                if (color is not null)
                {
                    label.Pop();
                }
            }
        }

        private static Color? GetColorForLine(string line)
        {
            string? level = TryGetBracketLevel(line);
            if (level is null) return null;

            return level switch
            {
                "ERROR" or "FATAL" or "EXCEPTION" => ErrorColor,
                "WARN" or "WARNING" => WarnColor,
                "DEBUG" or "TRACE" or "VERYDEBUG" => DebugColor,
                _ => null
            };
        }

        private static string? TryGetBracketLevel(string line)
        {
            if (!line.StartsWith('[')) return null;

            int closeIndex = line.IndexOf(']');
            if (closeIndex <= 1) return null;

            return line[1..closeIndex].ToUpperInvariant();
        }
    }
}
