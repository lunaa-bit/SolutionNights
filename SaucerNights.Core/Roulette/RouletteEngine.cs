using System.Security.Cryptography;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Core.Roulette;

/// <summary>
/// Authoritative single-zero roulette table run by the host (the host is the croupier / house). Players cover spots on
/// the layout during a betting window; the winning number is fixed the moment the wheel starts, shown so clients can
/// animate the ball to it, and paid out when the wheel stops.
/// </summary>
public sealed class RouletteEngine : ITableGame
{
    private sealed class SeatState
    {
        public long Bankroll;
        public readonly List<(byte Index, long Amount)> Bets = [];
        public byte[]? Seed;
        public bool SittingOut;
        public long RoundNet;

        public long Staked => Bets.Sum(b => b.Amount);
    }

    /// <summary>Extra time after the displayed betting deadline before the wheel starts (covers chat latency).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>How long the result stays on the table before betting reopens.</summary>
    public static readonly TimeSpan ResultPause = TimeSpan.FromSeconds(5);

    /// <summary>Results kept in <see cref="RouletteView.History"/>.</summary>
    public const int HistoryLength = 12;

    private readonly SeatState?[] _seats;
    private readonly List<TableEvent> _events = [];
    private readonly List<byte> _history = [];
    private RoulettePhase _phase = RoulettePhase.Waiting;
    private long _round;
    private int _result = -1;
    private DateTime? _deadline;
    private DateTime? _nextAt;
    private byte[] _serverSeed = [];
    private byte[]? _commit;
    private FairReveal? _reveal;
    private List<SeatSeed> _roundSeeds = [];
    private bool _paused;

    public RouletteEngine(RouletteRules rules)
    {
        Rules = rules.Normalized();
        _seats = new SeatState?[Rules.MaxSeats];
    }

    public RouletteRules Rules { get; }

    /// <summary>Test hook: picks the winning number instead of the wheel.</summary>
    internal Func<int>? ResultOverride { get; set; }

    public GameKind Kind => GameKind.Roulette;

    public int MaxSeats => Rules.MaxSeats;

    public long StartingStack => Rules.StartingStack;

    public int Version { get; private set; }

    public long Round => _round;

    public RoulettePhase Phase => _phase;

    public bool InRound => _phase == RoulettePhase.Spinning;

    public bool AwaitingPlayers => _phase == RoulettePhase.Betting;

    public ushort TableId { get; set; }

    public long HouseNet { get; private set; }

    /// <summary>Chips on the layout that are not settled yet (they are in nobody's stack).</summary>
    public long ChipsOnTable => _phase is RoulettePhase.Betting or RoulettePhase.Spinning ? _seats.Sum(s => s?.Staked ?? 0) : 0;

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Emit(value ? EventKind.Paused : EventKind.Resumed);
            // An empty betting window closes; one with chips on it still spins, then the table waits.
            if (value && _phase == RoulettePhase.Betting && !AnyBets())
            {
                _phase = RoulettePhase.Waiting;
                _deadline = null;
            }
            Touch();
        }
    }

    public byte[] RulesBytes() => Rules.ToBytes();

    private void Touch() => Version++;

    private void Emit(EventKind kind, int seat = TableEvent.NoSeat, long amount = 0, byte extra = 0) =>
        _events.Add(new TableEvent(kind, (byte)seat, amount, extra));

    public List<TableEvent> DrainEvents()
    {
        var list = _events.ToList();
        _events.Clear();
        return list;
    }

    public IReadOnlyList<(int Seat, Card[] Cards)> PrivateCards() => [];

    // ───────────── Seats ─────────────

    public bool IsSeated(int seat) => seat >= 0 && seat < _seats.Length && _seats[seat] != null;

    public void Seat(int seat, long stack, DateTime now)
    {
        _seats[seat] = new SeatState { Bankroll = stack };
        if (_phase == RoulettePhase.Waiting) _nextAt = now + TimeSpan.FromSeconds(1.5);
        Touch();
    }

    public long Unseat(int seat, DateTime now)
    {
        if (!IsSeated(seat)) return 0;
        var s = _seats[seat]!;
        if (_phase == RoulettePhase.Betting) ReturnBets(s);
        // Once the wheel is turning, chips on the layout are forfeited to the house.
        else if (_phase == RoulettePhase.Spinning) HouseNet += s.Staked;
        _seats[seat] = null;
        Touch();
        return s.Bankroll;
    }

    public long StackOf(int seat) => IsSeated(seat) ? _seats[seat]!.Bankroll : 0;

    public void AddChips(int seat, long amount)
    {
        if (!IsSeated(seat)) return;
        var s = _seats[seat]!;
        s.Bankroll = Math.Max(0, s.Bankroll + amount);
        Touch();
    }

    public void SetSittingOut(int seat, bool sitOut, DateTime now)
    {
        if (!IsSeated(seat)) return;
        var s = _seats[seat]!;
        if (s.SittingOut == sitOut) return;
        s.SittingOut = sitOut;
        // Bets that aren't riding yet come back; once the wheel turns they play out.
        if (sitOut && _phase == RoulettePhase.Betting && s.Bets.Count > 0)
        {
            ReturnBets(s);
            Emit(EventKind.RouletteBets, seat);
        }
        Emit(sitOut ? EventKind.SatOut : EventKind.SatIn, seat);
        Touch();
    }

    public bool IsSittingOut(int seat) => IsSeated(seat) && _seats[seat]!.SittingOut;

    private static void ReturnBets(SeatState s)
    {
        s.Bankroll += s.Staked;
        s.Bets.Clear();
        s.Seed = null;
    }

    private IEnumerable<int> SeatIndexes() => Enumerable.Range(0, _seats.Length).Where(i => _seats[i] != null);

    /// <summary>Chips of this seat that are on the layout and not settled.</summary>
    private long Riding(SeatState s) => _phase is RoulettePhase.Betting or RoulettePhase.Spinning ? s.Staked : 0;

    private bool Eligible(SeatState s) => !s.SittingOut && s.Bankroll + Riding(s) >= Rules.MinBet;

    private bool AnyEligible() => _seats.Any(s => s != null && Eligible(s));

    private bool AnyBets() => _phase is RoulettePhase.Betting or RoulettePhase.Spinning && _seats.Any(s => s is { Bets.Count: > 0 });

    // ───────────── Flow ─────────────

    public void StartNow(DateTime now)
    {
        switch (_phase)
        {
            case RoulettePhase.Waiting or RoulettePhase.Settled when AnyEligible() && !Paused:
                OpenBetting(now);
                break;
            case RoulettePhase.Betting when AnyBets():
                Spin(now);
                break;
        }
    }

    public void Tick(DateTime now)
    {
        switch (_phase)
        {
            case RoulettePhase.Waiting:
                if (Rules.AutoSpin && !Paused && AnyEligible() && (_nextAt == null || now >= _nextAt))
                    OpenBetting(now);
                break;

            case RoulettePhase.Betting:
                // Paused, an empty window closes; one with chips on it still spins.
                if (!AnyBets() && (Paused || !AnyEligible()))
                {
                    _phase = RoulettePhase.Waiting;
                    _deadline = null;
                    Touch();
                }
                else if (_deadline is { } d && now >= d + Grace)
                {
                    if (AnyBets()) Spin(now);
                    else
                    {
                        // Everyone took their bets back: wait for the next first bet.
                        _deadline = null;
                        Touch();
                    }
                }
                break;

            case RoulettePhase.Spinning:
                if (_deadline is { } end && now >= end) Settle(now);
                break;

            case RoulettePhase.Settled:
                if (_nextAt is { } n && now < n) break;
                if (!AnyEligible())
                {
                    ClearTable();
                    _phase = RoulettePhase.Waiting;
                    _nextAt = null;
                    Touch();
                }
                else if (Rules.AutoSpin && !Paused)
                {
                    OpenBetting(now);
                }
                break;
        }
    }

    private void ClearTable()
    {
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            s.Bets.Clear();
            s.Seed = null;
            s.RoundNet = 0;
        }
    }

    private void OpenBetting(DateTime now)
    {
        ClearTable();
        _round++;
        _phase = RoulettePhase.Betting;
        _serverSeed = Fair.NewServerSeed();
        _commit = Rules.ProvablyFair ? Fair.Commit(_serverSeed) : null;
        _reveal = null;
        _roundSeeds = [];
        _result = -1;
        _deadline = null;
        _nextAt = null;
        Emit(EventKind.BettingOpen, TableEvent.NoSeat, _round);
        Touch();
    }

    /// <summary>
    /// A player's complete bets for this spin. Replaces what they had down; anything invalid ignores the whole message.
    /// </summary>
    public void OnRouletteBet(int seat, RouletteBetMsg msg, DateTime now)
    {
        if (_phase != RoulettePhase.Betting || msg.Round != _round || !IsSeated(seat)) return;
        var s = _seats[seat]!;
        if (Rules.BetProblem(msg.Bets, s.Bankroll + s.Staked) != null) return;

        var total = msg.Bets.Sum(b => b.Amount);
        if (SameBets(s.Bets, msg.Bets))
        {
            // A repeated line. A new seed (the player rejoined) still counts.
            if (total > 0 && s.Seed is { } old && !old.AsSpan().SequenceEqual(msg.Seed))
            {
                s.Seed = msg.Seed.ToArray();
                Touch();
            }
            return;
        }

        s.Bankroll += s.Staked;
        s.Bets.Clear();
        s.Bets.AddRange(msg.Bets);
        s.Bankroll -= total;
        s.Seed = total > 0 ? msg.Seed.ToArray() : null;
        if (total > 0 && s.SittingOut)
        {
            s.SittingOut = false;
            Emit(EventKind.SatIn, seat);
        }
        Emit(EventKind.RouletteBets, seat, total, (byte)s.Bets.Count);
        if (total > 0) _deadline ??= now + TimeSpan.FromSeconds(Rules.BetSeconds);
        Touch();
    }

    private static bool SameBets(List<(byte Index, long Amount)> a, List<(byte Index, long Amount)> b) =>
        a.Count == b.Count && a.OrderBy(x => x.Index).SequenceEqual(b.OrderBy(x => x.Index));

    public void OnBet(int seat, BetMsg msg, DateTime now) { }

    public void OnAction(int seat, ActionMsg msg, DateTime now) { }

    public void OnReady(int seat, ReadyMsg msg, DateTime now) { }

    private void Spin(DateTime now)
    {
        var players = SeatIndexes().Where(i => _seats[i]!.Bets.Count > 0).ToList();
        if (players.Count == 0) return;

        _roundSeeds = Rules.ProvablyFair
            ? players.Where(i => _seats[i]!.Seed != null).Select(i => new SeatSeed(i, _seats[i]!.Seed!)).ToList()
            : [];
        _result = ResultOverride?.Invoke()
                  ?? (Rules.ProvablyFair
                      ? Fair.RouletteNumber(_serverSeed, TableId, _round, _roundSeeds)
                      : RandomNumberGenerator.GetInt32(RouletteBets.Pockets));

        long total = 0;
        foreach (var i in players)
        {
            var s = _seats[i]!;
            s.RoundNet = -s.Staked;
            total += s.Staked;
        }
        _phase = RoulettePhase.Spinning;
        _deadline = now + TimeSpan.FromSeconds(RouletteRules.SpinSeconds);
        Emit(EventKind.NoMoreBets, TableEvent.NoSeat, total, (byte)players.Count);
        Touch();
    }

    private void Settle(DateTime now)
    {
        Emit(EventKind.RouletteResult, TableEvent.NoSeat, _result);
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            if (s.Bets.Count == 0) continue;
            long paid = 0;
            var wins = 0;
            foreach (var (index, amount) in s.Bets)
            {
                var p = RouletteBets.Payout(index, amount, _result);
                if (p == 0) continue;
                paid += p;
                wins++;
            }
            s.Bankroll += paid;
            s.RoundNet += paid;
            HouseNet -= s.RoundNet;
            if (paid > 0) Emit(EventKind.RouletteWin, i, paid, (byte)wins);
            else Emit(EventKind.RouletteLose, i, s.Staked, (byte)s.Bets.Count);
        }

        _history.Add((byte)_result);
        if (_history.Count > HistoryLength) _history.RemoveRange(0, _history.Count - HistoryLength);
        _phase = RoulettePhase.Settled;
        _deadline = null;
        _reveal = Rules.ProvablyFair ? new FairReveal { ServerSeed = _serverSeed, ClientSeeds = _roundSeeds.ToList() } : null;
        _nextAt = now + ResultPause;
        Touch();
    }

    // ───────────── Snapshot ─────────────

    private static byte Seconds(DateTime? deadline, DateTime now) =>
        deadline is { } d ? (byte)Math.Clamp((int)Math.Ceiling((d - now).TotalSeconds), 0, 254) : (byte)255;

    public StateMsg BuildState(DateTime now)
    {
        var v = new RouletteView
        {
            Phase = _phase,
            Round = _round,
            DeadlineSec = Seconds(_deadline, now),
            Result = _phase is RoulettePhase.Spinning or RoulettePhase.Settled && _result >= 0 ? (byte)_result : RouletteBets.NoResult,
            History = [.. _history],
            Commit = _phase is RoulettePhase.Betting or RoulettePhase.Spinning ? _commit : null,
            Reveal = _phase == RoulettePhase.Settled ? _reveal : null,
        };
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            v.Seats.Add(new RouletteSeatView
            {
                Seat = (byte)i,
                Status = s.SittingOut ? RouletteSeatStatus.SittingOut : RouletteSeatStatus.Active,
                Bankroll = s.Bankroll,
                Bets = [.. s.Bets],
                RoundNet = s.RoundNet,
            });
        }
        return new StateMsg { Game = GameKind.Roulette, Roulette = v };
    }
}
