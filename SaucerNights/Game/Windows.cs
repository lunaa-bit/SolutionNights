using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using SaucerNights.App;
using SaucerNights.Ui;

namespace SaucerNights.Game;

/// <summary>
/// A window that follows the plugin's own UI scale: minimum size, first-use size and (when the scale changes) the
/// current size all grow and shrink with it. Pushes the theme and the scaled body font around its contents.
/// </summary>
internal abstract class ScaledWindow : Window
{
    private readonly Vector2 _baseSize;
    private readonly Vector2 _baseMin;
    private readonly Func<float> _scale;
    private readonly Func<IDisposable?> _pushFont;
    private IDisposable? _font;
    private float _applied;
    private bool _resizing;
    private Vector2 _lastSize;

    protected ScaledWindow(string name, Vector2 size, Vector2 min, Func<float> scale, Func<IDisposable?> pushFont)
        : base(name, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _baseSize = size;
        _baseMin = min;
        _scale = scale;
        _pushFont = pushFont;
        Size = size * scale();
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        var scale = _scale();
        SizeConstraints = new WindowSizeConstraints { MinimumSize = _baseMin * scale, MaximumSize = new Vector2(8000, 6000) };
        if (_resizing)
        {
            _resizing = false;
            SizeCondition = ImGuiCond.FirstUseEver;
            Size = _baseSize * scale;
        }
        if (_applied > 0 && Math.Abs(scale - _applied) > 0.001f && _lastSize.X > 0)
        {
            // Keep the window's proportions when the UI scale changes.
            Size = _lastSize / ImGuiHelpers.GlobalScale * (scale / _applied);
            SizeCondition = ImGuiCond.Always;
            _resizing = true;
        }
        _applied = scale;
        _font = _pushFont();
        Theme.Push();
    }

    public override void Draw()
    {
        Theme.AfterBegin();
        _lastSize = ImGui.GetWindowSize();
        DrawContents();
    }

    protected abstract void DrawContents();

    public override void PostDraw()
    {
        Theme.Pop();
        _font?.Dispose();
        _font = null;
    }
}

/// <summary>The lobby: find, host or practise a table, plus settings and help.</summary>
internal sealed class LobbyWindow : ScaledWindow
{
    private readonly GameApp _app;
    private readonly Action _openTable;
    private readonly LobbyView _view = new();

    public LobbyWindow(GameApp app, Action openTable, Func<IDisposable?> pushFont)
        : base("Solution Nights##lobby", new Vector2(980, 700), new Vector2(760, 540), () => app.Settings.UiScale, pushFont)
    {
        _app = app;
        _openTable = openTable;
    }

    public void Open(int page)
    {
        _view.Page = page;
        IsOpen = true;
        BringToFront();
    }

    protected override void DrawContents() => _view.Draw(_app, _openTable);
}

/// <summary>The animated card table.</summary>
internal sealed class TableWindow : ScaledWindow
{
    private readonly GameApp _app;
    private readonly Action _openLobby;
    private readonly TableView _view = new();

    public TableWindow(GameApp app, Action openLobby, Func<IDisposable?> pushFont)
        : base("Solution Nights - Table##table", new Vector2(1280, 800), new Vector2(900, 600), () => app.Settings.UiScale, pushFont)
    {
        _app = app;
        _openLobby = openLobby;
    }

    public void Open()
    {
        IsOpen = true;
        BringToFront();
    }

    protected override void DrawContents()
    {
        var title = _app.Hub.Table?.Table.Title;
        WindowName = string.IsNullOrEmpty(title) ? "Solution Nights - Table##table" : $"Solution Nights - {title}##table";
        _view.Draw(_app, _openLobby);
    }
}
