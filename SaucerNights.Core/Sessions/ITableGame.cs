using SaucerNights.Core.Cards;
using SaucerNights.Core.Protocol;

namespace SaucerNights.Core.Sessions;

/// <summary>
/// A host-side game engine. It owns the cards and the rules; <see cref="HostSession"/> owns players, keys and the
/// network. Every method that changes the table bumps <see cref="Version"/> and may queue events.
/// </summary>
public interface ITableGame
{
    GameKind Kind { get; }

    int MaxSeats { get; }

    long StartingStack { get; }

    /// <summary>Changes whenever the public state changes.</summary>
    int Version { get; }

    /// <summary>Current round (blackjack) or hand (poker) number.</summary>
    long Round { get; }

    /// <summary>True between the deal and the settlement.</summary>
    bool InRound { get; }

    /// <summary>True while waiting on players before a deal (bets, shuffle seeds).</summary>
    bool AwaitingPlayers { get; }

    bool Paused { get; set; }

    ushort TableId { get; set; }

    byte[] RulesBytes();

    bool IsSeated(int seat);

    void Seat(int seat, long stack, DateTime now);

    /// <summary>Removes a player (folding / forfeiting what is in play) and returns the chips they leave with.</summary>
    long Unseat(int seat, DateTime now);

    /// <summary>Chips the player holds, not counting chips in play.</summary>
    long StackOf(int seat);

    void AddChips(int seat, long amount);

    void SetSittingOut(int seat, bool sitOut, DateTime now);

    bool IsSittingOut(int seat);

    void OnAction(int seat, ActionMsg msg, DateTime now);

    void OnBet(int seat, BetMsg msg, DateTime now);

    void OnReady(int seat, ReadyMsg msg, DateTime now);

    void Tick(DateTime now);

    /// <summary>The host pressed "deal" / "start".</summary>
    void StartNow(DateTime now);

    /// <summary>Public snapshot (events and roster version are filled in by the session).</summary>
    StateMsg BuildState(DateTime now);

    /// <summary>Hole cards that must be sealed for their owners (poker only).</summary>
    IReadOnlyList<(int Seat, Card[] Cards)> PrivateCards();

    List<TableEvent> DrainEvents();

    /// <summary>Net chips won by the house (blackjack); always 0 for poker.</summary>
    long HouseNet { get; }
}

/// <summary>Time source, so sessions can be driven by a fake clock in tests.</summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime Now => DateTime.UtcNow;
}

public sealed class ManualClock(DateTime start) : IClock
{
    public DateTime Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now += by;
}
