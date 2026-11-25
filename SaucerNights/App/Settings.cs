using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Roulette;

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

/// <summary>Table felts. Saved as numbers, so existing values keep their slot.</summary>
public enum FeltStyle
{
    Emerald = 0,
    Neon = 1,
    Crimson = 2,
    Ultraviolet = 3,
    Gilded = 4,
}

public enum CardBackStyle
{
    Crimson = 0,
    Midnight = 1,
    Emerald = 2,
    Gilded = 3,
    Neon = 4,
    Ultraviolet = 5,
}

public static class Looks
{
    public static readonly FeltStyle[] Felts = [FeltStyle.Neon, FeltStyle.Ultraviolet, FeltStyle.Gilded, FeltStyle.Emerald, FeltStyle.Crimson];
    public static readonly CardBackStyle[] Backs = [CardBackStyle.Neon, CardBackStyle.Ultraviolet, CardBackStyle.Gilded, CardBackStyle.Midnight, CardBackStyle.Crimson, CardBackStyle.Emerald];

    public static string Name(FeltStyle f) => f switch
    {
        FeltStyle.Neon => "Neon night",
        FeltStyle.Ultraviolet => "Ultraviolet",
        FeltStyle.Gilded => "Gilded",
        FeltStyle.Emerald => "Classic green",
        _ => "Crimson",
    };

    public static string Name(CardBackStyle b) => b switch
    {
        CardBackStyle.Neon => "Neon lattice",
        CardBackStyle.Ultraviolet => "Ultraviolet",
        CardBackStyle.Gilded => "Gilded",
        CardBackStyle.Midnight => "Midnight",
        CardBackStyle.Emerald => "Emerald",
        _ => "Classic red",
    };
}

/// <summary>Plugin settings, saved as JSON next to the plugin's config.</summary>
public sealed class Settings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>False until the player has been through the welcome screen and picked a linkshell.</summary>
    public bool IntroDone { get; set; }

    // Channel
    public ChatChannel Channel { get; set; } = ChatChannel.Cwls1;
    public bool HideProtocolLines { get; set; } = true;
    public int SendIntervalMs { get; set; } = 1200;

    // Look & feel
    public const float MinScale = 0.75f;
    public const float MaxScale = 1.6f;

    /// <summary>Size of everything in the plugin's windows, on top of Dalamud's global scale.</summary>
    public float UiScale { get; set; } = 1f;

    public FeltStyle Felt { get; set; } = FeltStyle.Neon;
    public CardBackStyle CardBack { get; set; } = CardBackStyle.Neon;
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
    public BlackjackRules Blackjack { get; set; } = new();
    public PokerRules Poker { get; set; } = new();
    public RouletteRules Roulette { get; set; } = new();

    /// <summary>Brings settings saved by an older version up to date. Returns true if anything changed.</summary>
    public bool Upgrade()
    {
        var changed = false;
        if (Version < 2)
        {
            // 1.1: the Solution Nine look replaces the old defaults once, and hold'em starts from the safer
            // defaults (checked shuffles, host deals without playing).
            Felt = FeltStyle.Neon;
            CardBack = CardBackStyle.Neon;
            Poker.ProvablyFair = true;
            Poker.HostPlays = false;
            changed = true;
        }
        if (!float.IsFinite(UiScale) || UiScale < MinScale || UiScale > MaxScale)
        {
            UiScale = Math.Clamp(float.IsFinite(UiScale) ? UiScale : 1f, MinScale, MaxScale);
            changed = true;
        }
        if (!Enum.IsDefined(Felt)) (Felt, changed) = (FeltStyle.Neon, true);
        if (!Enum.IsDefined(CardBack)) (CardBack, changed) = (CardBackStyle.Neon, true);
        if (Version != CurrentVersion) (Version, changed) = (CurrentVersion, true);
        return changed;
    }
}
