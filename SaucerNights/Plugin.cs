using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SaucerNights.App;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using SaucerNights.Game;
using SaucerNights.Ui;

namespace SaucerNights;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/saucer";
    private const string CommandAlias = "/saucernights";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };

    private readonly WindowSystem _windows = new("SaucerNights");
    private readonly GameBridge _bridge;
    private readonly FontSet _fonts;
    private readonly LobbyWindow _lobby;
    private readonly TableWindow _table;
    private readonly string _settingsPath;

    public Plugin()
    {
        _settingsPath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "settings.json");
        Settings = LoadSettings();

        _bridge = new GameBridge(() => Settings);
        App = new GameApp(_bridge, Settings, SaveSettings, _bridge, SystemClock.Instance);
        _bridge.Attach(App);

        _fonts = new FontSet(PluginInterface.UiBuilder);
        UiEnv.FontProvider = _fonts.Get;

        _lobby = new LobbyWindow(App, () => _table!.Open());
        _table = new TableWindow(App, () => _lobby.Open(0));
        _windows.AddWindow(_lobby);
        _windows.AddWindow(_table);
        App.TableOpened += () =>
        {
            if (Settings.OpenTableOnJoin || App.Hub.IsHosting || App.Hub.IsPractice) _table.Open();
        };

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += () => _lobby.Toggle();
        PluginInterface.UiBuilder.OpenConfigUi += () => _lobby.Open(3);
        Framework.Update += OnFrameworkUpdate;
        ChatGui.ChatMessage += _bridge.OnChatMessage;
        ClientState.Logout += OnLogout;

        var help = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Saucer Nights. Also: /saucer table | host bj | host poker | practice bj | practice poker | leave | settings",
        };
        Commands.AddHandler(Command, help);
        Commands.AddHandler(CommandAlias, new CommandInfo(OnCommand) { ShowInHelp = false });
    }

    internal Settings Settings { get; }

    internal GameApp App { get; }

    private Settings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Warning($"Settings could not be read, using defaults: {ex.Message}");
        }
        return new Settings();
    }

    private void SaveSettings()
    {
        try
        {
            var tmp = _settingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(tmp, _settingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Settings could not be saved: {ex.Message}");
        }
    }

    private void Draw()
    {
        UiEnv.Scale = ImGuiHelpers.GlobalScale;
        _windows.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            App.Update();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed");
        }
    }

    private void OnLogout(int type, int code)
    {
        // Character changes invalidate our chat identity; leave tables cleanly.
        if (App.Hub.Busy && !App.Hub.IsPractice)
        {
            App.Leave();
            App.Notify("You logged out, so you left the table.");
        }
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : string.Empty;
        var game = parts.Length > 1 ? parts[1] : string.Empty;
        GameKind? kind = game switch
        {
            "bj" or "blackjack" or "21" => GameKind.Blackjack,
            "poker" or "holdem" or "hold'em" or "nlhe" => GameKind.Poker,
            _ => null,
        };
        switch (verb)
        {
            case "":
                _lobby.Toggle();
                break;
            case "table":
                _table.Open();
                break;
            case "settings" or "config":
                _lobby.Open(3);
                break;
            case "leave":
                App.Leave();
                break;
            case "host" when kind != null:
                if (App.CanUseChannel() is { } problem) ChatGui.PrintError($"[Saucer Nights] {problem}");
                else if (kind == GameKind.Blackjack) App.HostBlackjack();
                else App.HostPoker();
                break;
            case "practice" when kind != null:
                App.Practice(kind.Value);
                break;
            default:
                ChatGui.Print("[Saucer Nights] Usage: /saucer [table | host bj|poker | practice bj|poker | leave | settings]");
                break;
        }
    }

    public void Dispose()
    {
        // Close tables first so a failure below can't leave a host running.
        try
        {
            App.Leave();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Leaving the table on unload failed");
        }
        Commands.RemoveHandler(Command);
        Commands.RemoveHandler(CommandAlias);
        ClientState.Logout -= OnLogout;
        ChatGui.ChatMessage -= _bridge.OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        _windows.RemoveAllWindows();
        _fonts.Dispose();
        App.Dispose();
        UiEnv.FontProvider = (_, _) => null;
    }
}
