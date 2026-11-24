using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SaucerNights.App;
using SaucerNights.Ui;

namespace SaucerNights.Game;

/// <summary>The lobby: find, host or practise a table, plus settings and help.</summary>
internal sealed class LobbyWindow : Window
{
    private readonly GameApp _app;
    private readonly Action _openTable;
    private readonly LobbyView _view = new();

    public LobbyWindow(GameApp app, Action openTable)
        : base("Saucer Nights##lobby", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _app = app;
        _openTable = openTable;
        Size = new Vector2(980, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(760, 520), MaximumSize = new Vector2(4000, 3000) };
    }

    public void Open(int page)
    {
        _view.Page = page;
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw() => Theme.Push();

    public override void Draw()
    {
        Theme.AfterBegin();
        _view.Draw(_app, _openTable);
    }

    public override void PostDraw() => Theme.Pop();
}

/// <summary>The animated card table.</summary>
internal sealed class TableWindow : Window
{
    private readonly GameApp _app;
    private readonly Action _openLobby;
    private readonly TableView _view = new();

    public TableWindow(GameApp app, Action openLobby)
        : base("Saucer Nights - Table##table", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _app = app;
        _openLobby = openLobby;
        Size = new Vector2(1280, 800);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(900, 600), MaximumSize = new Vector2(6000, 4000) };
    }

    public void Open()
    {
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw() => Theme.Push();

    public override void Draw()
    {
        Theme.AfterBegin();
        var title = _app.Hub.Table?.Table.Title;
        WindowName = string.IsNullOrEmpty(title) ? "Saucer Nights - Table##table" : $"Saucer Nights - {title}##table";
        _view.Draw(_app, _openLobby);
    }

    public override void PostDraw() => Theme.Pop();
}
