using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using SaucerNights.App;
using SaucerNights.Core.Net;
using SaucerNights.Core.Wire;

namespace SaucerNights.Game;

/// <summary>
/// The plugin's connection to the game: who we are, posting lines to the chosen channel, and reading lines back from
/// chat (hiding the protocol ones).
/// </summary>
internal sealed unsafe class GameBridge(Func<Settings> settings) : IGameBridge, IChatLink
{
    private GameApp? _app;
    private string _cachedId = string.Empty;
    private ulong _cachedContentId;

    public void Attach(GameApp app) => _app = app;

    public bool LoggedIn => Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.IsLoaded;

    public bool Ready => LoggedIn;

    public string SelfName => LoggedIn ? Plugin.PlayerState.CharacterName : string.Empty;

    public string SelfId
    {
        get
        {
            if (!LoggedIn) return string.Empty;
            var cid = Plugin.PlayerState.ContentId;
            if (cid != _cachedContentId || _cachedId.Length == 0)
            {
                var world = Plugin.PlayerState.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
                _cachedId = world.Length > 0 ? $"{Plugin.PlayerState.CharacterName}@{world}" : Plugin.PlayerState.CharacterName;
                _cachedContentId = cid;
            }
            return _cachedId;
        }
    }

    public string ChannelName(ChatChannel channel)
    {
        if (!LoggedIn || !ChatChannels.IsCrossWorld(channel)) return string.Empty;
        try
        {
            var proxy = InfoProxyCrossWorldLinkshell.Instance();
            if (proxy == null) return string.Empty;
            var name = proxy->GetCrossworldLinkshellName((uint)(ChatChannels.Number(channel) - 1));
            return name == null ? string.Empty : name->ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public void PlaySound(int effect)
    {
        if (effect is >= 1 and <= 16) UIGlobals.PlayChatSoundEffect((uint)effect);
    }

    /// <summary>Protocol line for the game channel (paced by the hub's outbox).</summary>
    public void Send(string line) => Post(line);

    /// <summary>The player's own chat message, typed in the table window.</summary>
    public void SendChat(string text)
    {
        // Never let a typed message look like a protocol line or a command.
        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length == 0) return;
        if (FrameCodec.LooksLikeProtocol(text) || text.StartsWith('/')) text = " " + text;
        Post(text);
    }

    private void Post(string text)
    {
        if (!LoggedIn) return;
        var full = $"{ChatChannels.Command(settings().Channel)} {text}";
        if (Encoding.UTF8.GetByteCount(full) > 500)
        {
            Plugin.Log.Warning($"Dropped a chat line over 500 bytes ({full.Length} chars).");
            return;
        }
        var str = Utf8String.FromString(full);
        try
        {
            str->SanitizeString((AllowedEntities)0x27F);
            UIModule.Instance()->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }

    private static XivChatType ChatType(ChatChannel c) => c switch
    {
        ChatChannel.Cwls1 => XivChatType.CrossLinkShell1,
        ChatChannel.Cwls2 => XivChatType.CrossLinkShell2,
        ChatChannel.Cwls3 => XivChatType.CrossLinkShell3,
        ChatChannel.Cwls4 => XivChatType.CrossLinkShell4,
        ChatChannel.Cwls5 => XivChatType.CrossLinkShell5,
        ChatChannel.Cwls6 => XivChatType.CrossLinkShell6,
        ChatChannel.Cwls7 => XivChatType.CrossLinkShell7,
        ChatChannel.Cwls8 => XivChatType.CrossLinkShell8,
        ChatChannel.Ls1 => XivChatType.Ls1,
        ChatChannel.Ls2 => XivChatType.Ls2,
        ChatChannel.Ls3 => XivChatType.Ls3,
        ChatChannel.Ls4 => XivChatType.Ls4,
        ChatChannel.Ls5 => XivChatType.Ls5,
        ChatChannel.Ls6 => XivChatType.Ls6,
        ChatChannel.Ls7 => XivChatType.Ls7,
        ChatChannel.Ls8 => XivChatType.Ls8,
        _ => XivChatType.Party,
    };

    public void OnChatMessage(IHandleableChatMessage msg)
    {
        if (_app == null) return;
        try
        {
            var config = settings();
            if (msg.LogKind != ChatType(config.Channel)) return;
            var text = msg.Message.TextValue.Trim();
            var sender = SenderId(msg.Sender);
            if (FrameCodec.LooksLikeProtocol(text))
            {
                if (config.HideProtocolLines) msg.PreventOriginal();
                if (sender.Length > 0) _app.Hub.OnChatLine(sender, text);
                return;
            }
            if (sender.Length > 0) _app.OnChannelChat(sender, text);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Chat handling failed");
        }
    }

    /// <summary>"Name@World" for the sender. Our own lines carry no player link, so they map to <see cref="SelfId"/>.</summary>
    private string SenderId(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is not PlayerPayload pp) continue;
            var world = pp.World.ValueNullable?.Name.ToString();
            return string.IsNullOrEmpty(world) ? pp.PlayerName : $"{pp.PlayerName}@{world}";
        }
        var name = Clean(sender.TextValue);
        if (name.Length == 0) return string.Empty;
        return string.Equals(name, SelfName, StringComparison.OrdinalIgnoreCase) ? SelfId : name;
    }

    /// <summary>Strips the game's icons and channel markers from a plain-text sender name.</summary>
    private static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            if (char.IsLetter(ch) || ch is ' ' or '\'' or '-') sb.Append(ch);
        return sb.ToString().Trim();
    }
}
