using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Poker;

namespace SaucerNights.App;

/// <summary>Where the plugin talks. Cross-world linkshells are the default; linkshells and party also work.</summary>
public enum ChatChannel
{
    Cwls1, Cwls2, Cwls3, Cwls4, Cwls5, Cwls6, Cwls7, Cwls8,
    Ls1, Ls2, Ls3, Ls4, Ls5, Ls6, Ls7, Ls8,
    Party,
}

public static class ChatChannels
{
    public static readonly ChatChannel[] All = Enum.GetValues<ChatChannel>();

    public static bool IsCrossWorld(ChatChannel c) => c <= ChatChannel.Cwls8;

    public static int Number(ChatChannel c) => c switch
    {
        <= ChatChannel.Cwls8 => c - ChatChannel.Cwls1 + 1,
        <= ChatChannel.Ls8 => c - ChatChannel.Ls1 + 1,
        _ => 0,
    };

    /// <summary>The slash command that posts to this channel.</summary>
    public static string Command(ChatChannel c) => c switch
    {
        <= ChatChannel.Cwls8 => $"/cwlinkshell{Number(c)}",
        <= ChatChannel.Ls8 => $"/linkshell{Number(c)}",
        _ => "/party",
    };

    public static string Short(ChatChannel c) => c switch
    {
        <= ChatChannel.Cwls8 => $"CWLS {Number(c)}",
        <= ChatChannel.Ls8 => $"LS {Number(c)}",
        _ => "Party",
    };
}

public enum FeltStyle
{
    Emerald,
    Sapphire,
    Crimson,
    Amethyst,
    Onyx,
}

public enum CardBackStyle
{
    Crimson,
    Midnight,
    Emerald,
    Gilded,
}

/// <summary>Plugin settings, saved as JSON next to the plugin's config.</summary>
public sealed class Settings
{
    public int Version { get; set; } = 1;

    // Channel
    public ChatChannel Channel { get; set; } = ChatChannel.Cwls1;
    public bool HideProtocolLines { get; set; } = true;
    public int SendIntervalMs { get; set; } = 1200;

    // Look & feel
    public FeltStyle Felt { get; set; } = FeltStyle.Emerald;
    public CardBackStyle CardBack { get; set; } = CardBackStyle.Crimson;
    public bool FourColorDeck { get; set; }
    public float AnimationSpeed { get; set; } = 1f;
    public bool ShowSidePanel { get; set; } = true;
    public bool ShowHandStrength { get; set; } = true;

    // Sounds
    public bool TurnSound { get; set; } = true;
    public int TurnSoundEffect { get; set; } = 6;
    public bool WinSound { get; set; }
    public int WinSoundEffect { get; set; } = 3;

    // Play
    public bool ConfirmFoldWhenCheckIsFree { get; set; } = true;

    /// <summary>Letter shortcuts in the table window. Off by default: the game also sees those keys (S walks backwards).</summary>
    public bool KeyboardShortcuts { get; set; }
    public bool OpenTableOnJoin { get; set; } = true;
    public int PracticeBots { get; set; } = 3;

    // Hosting
    public string TableTitle { get; set; } = string.Empty;
    public bool AutoApproveRebuys { get; set; } = true;
    public BlackjackRules Blackjack { get; set; } = new();
    public PokerRules Poker { get; set; } = new();
}
