using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Core.Poker;

/// <summary>
/// Authoritative no-limit hold'em table: moving button, blinds/antes, full and incomplete raises, side pots, uncalled
/// bets, split pots with odd chips going left of the button, and automatic showdowns.
/// </summary>
public sealed class PokerEngine : ITableGame
{
    private sealed class SeatState
    {
        public long Stack;
        public long PendingChips;
        public bool SittingOut;
        public bool Leaving;
        public bool Dealt;
        public PokerSeatStatus Status = PokerSeatStatus.Waiting;
        public Card[] Hole = [];
        public long Bet;
        public long Committed;
        public PokerLast Last;
        public long Won;
        public bool Shown;
        public int Timeouts;
        public byte[]? Seed;

        public bool InHand => Dealt && Status is PokerSeatStatus.Active or PokerSeatStatus.AllIn;

        public bool CanAct => Dealt && Status == PokerSeatStatus.Active;
    }

    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>How long shown cards stay on the table before the next hand.</summary>
    public static readonly TimeSpan ShowLinger = TimeSpan.FromSeconds(4);
    private const int SeatSpace = 16;

    private readonly SeatState?[] _seats;
    private readonly List<TableEvent> _events = [];
    private readonly List<Card> _board = [];
    private readonly HashSet<int> _needToAct = [];
    private readonly HashSet<int> _actedSinceFullRaise = [];
    private List<PokerPotView> _finalPots = [];
    private Card[] _deck = [];
    private int _drawn;
    private PokerPhase _phase = PokerPhase.Waiting;
    private long _hand;
    private long _turnId;
    private int _button = -1;
    private int _sb = -1;
    private int _bb = -1;
    private int _active = -1;
    private long _currentBet;
    private long _minRaise;
    private DateTime? _deadline;
    private DateTime? _nextAt;
    private byte[] _serverSeed = [];
    private byte[]? _commit;
    private byte[] _maskSeed = [];
    private PokerFair.Mask? _mask;
    private bool _fairDeal;
    private List<SeatSeed> _handSeeds = [];
    private bool _paused;

    public PokerEngine(PokerRules rules)
    {
        Rules = rules.Normalized();
        _seats = new SeatState?[Rules.MaxSeats];
    }

    public PokerRules Rules { get; }

    /// <summary>Test hook: replaces the shuffled deck.</summary>
    internal Func<Card[]>? DeckOverride { get; set; }

    public GameKind Kind => GameKind.Poker;

    public int MaxSeats => Rules.MaxSeats;

    public long StartingStack => Rules.StartingStack;

    public int Version { get; private set; }

    public long Round => _hand;

    public PokerPhase Phase => _phase;

    public bool InRound => _phase is >= PokerPhase.Preflop and <= PokerPhase.Showdown;

    public bool AwaitingPlayers => _phase == PokerPhase.Starting;

    public ushort TableId { get; set; }

    public long HouseNet => 0;

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Emit(value ? EventKind.Paused : EventKind.Resumed);
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

    private static bool Betting(PokerPhase p) => p is >= PokerPhase.Preflop and <= PokerPhase.River;

    private IEnumerable<int> SeatIndexes() => Enumerable.Range(0, _seats.Length).Where(i => _seats[i] != null);

    private bool EligibleForHand(int i) => _seats[i] is { SittingOut: false, Leaving: false } s && s.Stack + s.PendingChips > 0;

    private int EligibleCount() => Enumerable.Range(0, _seats.Length).Count(EligibleForHand);

    private int CanActCount() => _seats.Count(s => s is { CanAct: true });

    // ───────────── Seats ─────────────

    public bool IsSeated(int seat) => seat >= 0 && seat < _seats.Length && _seats[seat] != null;

    public void Seat(int seat, long stack, DateTime now)
    {
        _seats[seat] = new SeatState { Stack = stack };
        if (_phase == PokerPhase.Waiting) _nextAt = now + TimeSpan.FromSeconds(2);
        Touch();
    }

    public long Unseat(int seat, DateTime now)
    {
        if (!IsSeated(seat)) return 0;
        var s = _seats[seat]!;
        var chips = s.Stack + s.PendingChips;
        if (InRound && s.Dealt)
        {
            s.Stack = 0;
            s.PendingChips = 0;
            s.Leaving = true;
            if (Betting(_phase) && s.Status != PokerSeatStatus.Folded)
            {
                var wasActive = _active == seat;
                s.Status = PokerSeatStatus.Folded;
                s.Last = PokerLast.Fold;
                _needToAct.Remove(seat);
                Emit(EventKind.Fold, seat);
                Touch();
                if (wasActive)
                {
                    _turnId++;
                    AfterAction(seat, now);
                }
                else
                {
                    var live = SeatIndexes().Where(i => _seats[i]!.InHand).ToList();
                    if (live.Count == 1) AwardUncontested(live[0], now);
                }
            }
        }
        else
        {
            _seats[seat] = null;
        }
        Touch();
        return chips;
    }

    public long StackOf(int seat) => IsSeated(seat) ? _seats[seat]!.Stack + _seats[seat]!.PendingChips : 0;

    public void AddChips(int seat, long amount)
    {
        if (!IsSeated(seat)) return;
        var s = _seats[seat]!;
        if (InRound && s.Dealt && !s.Leaving) s.PendingChips += amount;
        else s.Stack = Math.Max(0, s.Stack + amount);
        if (s.PendingChips < 0)
        {
            s.Stack = Math.Max(0, s.Stack + s.PendingChips);
            s.PendingChips = 0;
        }
        Touch();
    }

    public void SetSittingOut(int seat, bool sitOut, DateTime now)
    {
        if (!IsSeated(seat)) return;
        var s = _seats[seat]!;
        if (s.SittingOut == sitOut) return;
        s.SittingOut = sitOut;
        s.Timeouts = 0;
        if (!s.Dealt) s.Status = sitOut ? PokerSeatStatus.SittingOut : PokerSeatStatus.Waiting;
        Emit(sitOut ? EventKind.SatOut : EventKind.SatIn, seat);
        if (!sitOut && _phase == PokerPhase.Waiting) _nextAt ??= now + TimeSpan.FromSeconds(1);
        Touch();
    }

    public bool IsSittingOut(int seat) => IsSeated(seat) && _seats[seat]!.SittingOut;

    public void OnBet(int seat, BetMsg msg, DateTime now) { }

    // ───────────── Hand flow ─────────────

    public void StartNow(DateTime now)
    {
        if (_phase == PokerPhase.Waiting && EligibleCount() >= 2) BeginHand(now);
        else if (_phase == PokerPhase.Starting) Deal(now);
        else if (_phase == PokerPhase.Showdown) EndHand(now, startNext: true);
    }

    public void Tick(DateTime now)
    {
        switch (_phase)
        {
            case PokerPhase.Waiting:
                if (Rules.AutoStart && !Paused && EligibleCount() >= 2 && (_nextAt == null || now >= _nextAt))
                    BeginHand(now);
                break;
            case PokerPhase.Starting:
                if (EligibleCount() < 2)
                {
                    _phase = PokerPhase.Waiting;
                    _deadline = null;
                    Touch();
                }
                else if (_deadline is { } ds && now >= ds)
                {
                    Deal(now);
                }
                break;
            case PokerPhase.Showdown:
                if (_nextAt is { } n && now >= n) EndHand(now, startNext: Rules.AutoStart && !Paused);
                break;
            default:
                if (Betting(_phase) && _deadline is { } d && now >= d + Grace && _active >= 0) TimeoutActive(now);
                break;
        }
    }

    private void BeginHand(DateTime now)
    {
        if (EligibleCount() < 2) return;
        _hand++;
        _serverSeed = Fair.NewServerSeed();
        _maskSeed = PokerFair.NewMaskSeed();
        _mask = new PokerFair.Mask(_maskSeed, TableId, _hand, _seats.Length);
        _commit = Rules.ProvablyFair ? _mask.Commit(_serverSeed) : null;
        _fairDeal = false;
        _board.Clear();
        _finalPots = [];
        _sb = _bb = _active = -1;
        _deadline = null;
        _nextAt = null;
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            s.Stack += s.PendingChips;
            s.PendingChips = 0;
            s.Hole = [];
            s.Bet = 0;
            s.Committed = 0;
            s.Last = PokerLast.None;
            s.Won = 0;
            s.Shown = false;
            s.Dealt = false;
            s.Seed = null;
            s.Status = s.SittingOut ? PokerSeatStatus.SittingOut : PokerSeatStatus.Waiting;
        }
        Emit(EventKind.HandStarted, TableEvent.NoSeat, _hand);
        if (Rules.ProvablyFair)
        {
            _phase = PokerPhase.Starting;
            _turnId++;
            _deadline = now + TimeSpan.FromSeconds(4);
            Touch();
        }
        else
        {
            Deal(now);
        }
    }

    public void OnReady(int seat, ReadyMsg msg, DateTime now)
    {
        if (_phase != PokerPhase.Starting || msg.Hand != _hand || !EligibleForHand(seat)) return;
        _seats[seat]!.Seed = msg.Seed.ToArray();
        if (Enumerable.Range(0, _seats.Length).Where(EligibleForHand).All(i => _seats[i]!.Seed != null))
            Deal(now);
    }

    private static int NextIn(List<int> sortedSeats, int after)
    {
        foreach (var s in sortedSeats)
            if (s > after) return s;
        return sortedSeats[0];
    }

    private Card Draw() => _deck[_drawn++];

    /// <summary>
    /// Lays the hand's cards out in drawing order (hole cards one at a time around the table, then burn/flop,
    /// burn/turn, burn/river). Each card comes from the deck position the host committed to before the shuffle
    /// existed (see <see cref="PokerFair"/>).
    /// </summary>
    private Card[] ArrangeDeck(List<int> order)
    {
        var shuffled = Fair.Shuffle(Fair.DeckKey(_serverSeed, TableId, _hand, _handSeeds), 1);
        var mask = _mask!;
        var n = order.Count;
        var deck = new Card[2 * n + 8];
        Array.Fill(deck, Card.Hidden); // burn cards are never looked at
        for (var k = 0; k < n; k++)
        {
            var hole = mask.Cards(shuffled, PokerFair.SeatLeaf(order[k]));
            deck[k] = hole[0];
            deck[n + k] = hole[1];
        }
        var flop = mask.Cards(shuffled, PokerFair.FlopLeaf);
        deck[2 * n + 1] = flop[0];
        deck[2 * n + 2] = flop[1];
        deck[2 * n + 3] = flop[2];
        deck[2 * n + 5] = mask.Cards(shuffled, PokerFair.TurnLeaf)[0];
        deck[2 * n + 7] = mask.Cards(shuffled, PokerFair.RiverLeaf)[0];
        return deck;
    }

    private void Deal(DateTime now)
    {
        var dealt = Enumerable.Range(0, _seats.Length).Where(EligibleForHand).ToList();
        if (dealt.Count < 2)
        {
            _phase = PokerPhase.Waiting;
            _deadline = null;
            Touch();
            return;
        }

        _button = _button < 0 ? dealt[0] : NextIn(dealt, _button);
        if (dealt.Count == 2)
        {
            _sb = _button;
            _bb = NextIn(dealt, _button);
        }
        else
        {
            _sb = NextIn(dealt, _button);
            _bb = NextIn(dealt, _sb);
        }

        _handSeeds = Rules.ProvablyFair
            ? dealt.Where(i => _seats[i]!.Seed != null).Select(i => new SeatSeed(i, _seats[i]!.Seed!)).ToList()
            : [];

        foreach (var i in dealt)
        {
            var s = _seats[i]!;
            s.Dealt = true;
            s.Status = PokerSeatStatus.Active;
        }

        if (Rules.Ante > 0)
        {
            foreach (var i in dealt)
            {
                var s = _seats[i]!;
                var a = Math.Min(Rules.Ante, s.Stack);
                s.Stack -= a;
                s.Committed += a;
                Emit(EventKind.PostAnte, i, a);
                if (s.Stack == 0) s.Status = PokerSeatStatus.AllIn;
            }
        }
        Post(_sb, Rules.SmallBlind, PokerLast.SmallBlind, EventKind.PostSmallBlind);
        Post(_bb, Rules.BigBlind, PokerLast.BigBlind, EventKind.PostBigBlind);

        var mask = DealtMask();
        var order = PokerView.DealOrder(mask, _button, SeatSpace);
        _fairDeal = DeckOverride == null;
        _deck = DeckOverride?.Invoke() ?? ArrangeDeck(order);
        _drawn = 0;
        foreach (var i in order) _seats[i]!.Hole = new Card[2];
        for (var round = 0; round < 2; round++)
            foreach (var i in order)
                _seats[i]!.Hole[round] = Draw();

        _currentBet = Math.Max(_seats[_sb]!.Bet, _seats[_bb]!.Bet);
        _minRaise = Rules.BigBlind;
        _actedSinceFullRaise.Clear();
        _needToAct.Clear();
        foreach (var i in dealt.Where(i => _seats[i]!.CanAct)) _needToAct.Add(i);
        _phase = PokerPhase.Preflop;
        Touch();

        var first = dealt.Count == 2 ? _sb : NextIn(dealt, _bb);
        var next = FindNextToAct(first);
        if (next < 0) EndStreet(now);
        else SetActive(next, now);
    }

    private ushort DealtMask()
    {
        var mask = 0;
        foreach (var i in SeatIndexes())
            if (_seats[i]!.Dealt) mask |= 1 << i;
        return (ushort)mask;
    }

    private void Post(int seat, long amount, PokerLast last, EventKind ev)
    {
        var s = _seats[seat]!;
        var a = Math.Min(amount, s.Stack);
        s.Stack -= a;
        s.Bet += a;
        s.Committed += a;
        s.Last = last;
        Emit(ev, seat, a);
        if (s.Stack == 0) s.Status = PokerSeatStatus.AllIn;
    }

    private int FindNextToAct(int start)
    {
        var canAct = CanActCount();
        for (var k = 0; k < _seats.Length; k++)
        {
            var seat = ((start % _seats.Length) + k) % _seats.Length;
            if (!_needToAct.Contains(seat) || _seats[seat] is not { CanAct: true } s) continue;
            if (canAct > 1 || s.Bet < _currentBet) return seat;
        }
        return -1;
    }

    private void SetActive(int seat, DateTime now)
    {
        _active = seat;
        _turnId++;
        _deadline = now + TimeSpan.FromSeconds(Rules.TurnSeconds);
        Touch();
    }

    public PokerActions AllowedFor(int seat)
    {
        if (!Betting(_phase) || seat != _active || _seats[seat] is not { CanAct: true } s) return PokerActions.None;
        var toCall = Math.Max(0, _currentBet - s.Bet);
        var a = PokerActions.Fold | (toCall == 0 ? PokerActions.Check : PokerActions.Call);
        if (s.Stack > toCall)
        {
            if (_currentBet == 0) a |= PokerActions.Bet;
            else if (!_actedSinceFullRaise.Contains(seat)) a |= PokerActions.Raise;
        }
        if (s.Stack > 0 && (s.Stack <= toCall || (a & (PokerActions.Bet | PokerActions.Raise)) != 0)) a |= PokerActions.AllIn;
        return a;
    }

    private long MinRaiseTo(SeatState? s)
    {
        var to = _currentBet == 0 ? Rules.BigBlind : _currentBet + _minRaise;
        return s == null ? to : Math.Min(to, s.Bet + s.Stack);
    }

    public void OnAction(int seat, ActionMsg msg, DateTime now)
    {
        if ((PokerActionKind)msg.Kind == PokerActionKind.Show)
        {
            if (msg.Round == _hand) ShowCards(seat, now);
            return;
        }
        if (!Betting(_phase) || seat != _active || msg.Round != _hand || msg.TurnId != _turnId) return;
        if (!Apply(seat, (PokerActionKind)msg.Kind, msg.Amount, timeout: false)) return;
        _turnId++;
        AfterAction(seat, now);
    }

    private bool Apply(int seat, PokerActionKind kind, long amount, bool timeout)
    {
        var s = _seats[seat]!;
        var allowed = AllowedFor(seat);
        var toCall = Math.Max(0, _currentBet - s.Bet);
        switch (kind)
        {
            case PokerActionKind.Fold:
                s.Status = PokerSeatStatus.Folded;
                s.Last = PokerLast.Fold;
                Emit(EventKind.Fold, seat, 0, timeout ? (byte)1 : (byte)0);
                break;
            case PokerActionKind.Check when allowed.HasFlag(PokerActions.Check):
                s.Last = PokerLast.Check;
                Emit(EventKind.Check, seat, 0, timeout ? (byte)1 : (byte)0);
                break;
            case PokerActionKind.Call when allowed.HasFlag(PokerActions.Call):
            {
                Put(s, Math.Min(toCall, s.Stack));
                if (s.Stack == 0)
                {
                    s.Status = PokerSeatStatus.AllIn;
                    s.Last = PokerLast.AllIn;
                    Emit(EventKind.AllIn, seat, s.Bet);
                }
                else
                {
                    s.Last = PokerLast.Call;
                    Emit(EventKind.Call, seat, s.Bet);
                }
                break;
            }
            case PokerActionKind.BetOrRaise when (allowed & (PokerActions.Bet | PokerActions.Raise)) != 0:
            {
                var max = s.Bet + s.Stack;
                if (amount >= max) return AllIn(seat, s);
                if (amount < MinRaiseTo(s) || amount <= _currentBet) return false;
                RaiseTo(seat, s, amount);
                break;
            }
            case PokerActionKind.AllIn when allowed.HasFlag(PokerActions.AllIn):
                return AllIn(seat, s);
            default:
                return false;
        }
        Finish(seat, s);
        return true;
    }

    private bool AllIn(int seat, SeatState s)
    {
        var target = s.Bet + s.Stack;
        if (target <= _currentBet)
        {
            Put(s, s.Stack);
            s.Status = PokerSeatStatus.AllIn;
            s.Last = PokerLast.AllIn;
            Emit(EventKind.AllIn, seat, s.Bet);
        }
        else
        {
            RaiseTo(seat, s, target);
        }
        Finish(seat, s);
        return true;
    }

    private void RaiseTo(int seat, SeatState s, long target)
    {
        var wasBet = _currentBet == 0;
        var raiseBy = target - _currentBet;
        Put(s, target - s.Bet);
        _currentBet = target;
        if (raiseBy >= _minRaise)
        {
            _minRaise = raiseBy;
            _actedSinceFullRaise.Clear();
        }
        foreach (var i in SeatIndexes())
            if (i != seat && _seats[i]!.CanAct && _seats[i]!.Bet < _currentBet)
                _needToAct.Add(i);

        if (s.Stack == 0)
        {
            s.Status = PokerSeatStatus.AllIn;
            s.Last = PokerLast.AllIn;
            Emit(EventKind.AllIn, seat, s.Bet);
        }
        else
        {
            s.Last = wasBet ? PokerLast.Bet : PokerLast.Raise;
            Emit(wasBet ? EventKind.Bet : EventKind.Raise, seat, s.Bet);
        }
    }

    private void Finish(int seat, SeatState s)
    {
        _actedSinceFullRaise.Add(seat);
        _needToAct.Remove(seat);
        if (s.Stack == 0 && s.Status == PokerSeatStatus.Active) s.Status = PokerSeatStatus.AllIn;
    }

    private static void Put(SeatState s, long amount)
    {
        s.Stack -= amount;
        s.Bet += amount;
        s.Committed += amount;
    }

    private void TimeoutActive(DateTime now)
    {
        var seat = _active;
        var s = _seats[seat]!;
        var allowed = AllowedFor(seat);
        Apply(seat, allowed.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Fold, 0, timeout: true);
        Emit(EventKind.Timeout, seat);
        if (++s.Timeouts >= 2 && !s.SittingOut)
        {
            s.SittingOut = true;
            Emit(EventKind.SatOut, seat);
        }
        _turnId++;
        AfterAction(seat, now);
    }

    private void AfterAction(int seat, DateTime now)
    {
        Touch();
        var live = SeatIndexes().Where(i => _seats[i]!.InHand).ToList();
        if (live.Count == 1)
        {
            AwardUncontested(live[0], now);
            return;
        }
        var next = FindNextToAct(seat + 1);
        if (next < 0) EndStreet(now);
        else SetActive(next, now);
    }

    private void ReturnUncalled()
    {
        var bettors = SeatIndexes().Where(i => _seats[i]!.Dealt).OrderByDescending(i => _seats[i]!.Bet).ToList();
        if (bettors.Count == 0) return;
        var top = _seats[bettors[0]]!;
        var second = bettors.Count > 1 ? _seats[bettors[1]]!.Bet : 0;
        var diff = top.Bet - second;
        // A player who left forfeits what they put in, so their excess stays in the pot.
        if (diff <= 0 || top.Leaving) return;
        top.Bet -= diff;
        top.Committed -= diff;
        top.Stack += diff;
        if (top.Status == PokerSeatStatus.AllIn) top.Status = PokerSeatStatus.Active;
        Emit(EventKind.UncalledReturned, bettors[0], diff);
    }

    private void EndStreet(DateTime now)
    {
        ReturnUncalled();
        foreach (var i in SeatIndexes()) _seats[i]!.Bet = 0;
        _currentBet = 0;
        _minRaise = Rules.BigBlind;
        _actedSinceFullRaise.Clear();
        _needToAct.Clear();
        _active = -1;

        if (_phase == PokerPhase.River)
        {
            Showdown(now, streetsRunOut: 0);
            return;
        }
        if (CanActCount() <= 1)
        {
            var streets = 0;
            while (_phase < PokerPhase.River)
            {
                DealStreet();
                streets++;
            }
            Showdown(now, streets);
            return;
        }

        DealStreet();
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            if (s.CanAct)
            {
                s.Last = PokerLast.None;
                _needToAct.Add(i);
            }
        }
        Touch();
        var first = FindNextToAct(_button + 1);
        if (first < 0) EndStreet(now);
        else SetActive(first, now);
    }

    private void DealStreet()
    {
        _phase++;
        _drawn++; // burn
        switch (_phase)
        {
            case PokerPhase.Flop:
                _board.Add(Draw());
                _board.Add(Draw());
                _board.Add(Draw());
                Emit(EventKind.Flop);
                break;
            case PokerPhase.Turn:
                _board.Add(Draw());
                Emit(EventKind.Turn);
                break;
            case PokerPhase.River:
                _board.Add(Draw());
                Emit(EventKind.River);
                break;
        }
        Touch();
    }

    /// <summary>Main pot and side pots from each player's contribution.</summary>
    private List<PokerPotView> ComputePots(Func<SeatState, long> contribution)
    {
        var entries = SeatIndexes()
            .Where(i => _seats[i]!.Dealt)
            .Select(i => (Seat: i, Amount: contribution(_seats[i]!), Live: _seats[i]!.InHand, AllIn: _seats[i]!.Status == PokerSeatStatus.AllIn))
            .ToList();
        var total = entries.Sum(e => e.Amount);
        var pots = new List<PokerPotView>();
        if (total <= 0) return pots;

        var live = entries.Where(e => e.Live).ToList();
        var levels = live.Where(e => e.AllIn).Select(e => e.Amount).ToList();
        if (live.Count > 0) levels.Add(live.Max(e => e.Amount));
        levels = levels.Where(l => l > 0).Distinct().OrderBy(l => l).ToList();

        long prev = 0;
        foreach (var level in levels)
        {
            var amount = entries.Sum(e => Math.Min(e.Amount, level) - Math.Min(e.Amount, prev));
            var eligible = live.Where(e => e.Amount >= level).Aggregate(0, (m, e) => m | (1 << e.Seat));
            if (amount > 0)
            {
                if (pots.Count > 0 && pots[^1].Eligible == eligible) pots[^1].Amount += amount;
                else pots.Add(new PokerPotView { Amount = amount, Eligible = (ushort)eligible });
            }
            prev = level;
        }
        var leftover = total - pots.Sum(p => p.Amount);
        if (leftover > 0)
        {
            if (pots.Count == 0)
                pots.Add(new PokerPotView { Amount = leftover, Eligible = (ushort)live.Aggregate(0, (m, e) => m | (1 << e.Seat)) });
            else
                pots[^1].Amount += leftover;
        }
        return pots;
    }

    private IEnumerable<int> ClockwiseFromButton()
    {
        for (var k = 1; k <= _seats.Length; k++) yield return (_button + k) % _seats.Length;
    }

    private void Showdown(DateTime now, int streetsRunOut)
    {
        _phase = PokerPhase.Showdown;
        _active = -1;
        _deadline = null;
        var pots = ComputePots(s => s.Committed);
        var contenders = SeatIndexes().Where(i => _seats[i]!.InHand).ToList();
        var values = contenders.ToDictionary(i => i, i => HandEvaluator.Evaluate([.. _seats[i]!.Hole, .. _board]));
        foreach (var i in contenders) _seats[i]!.Shown = true;
        Emit(EventKind.Showdown);

        foreach (var pot in pots)
        {
            var eligible = contenders.Where(i => (pot.Eligible & (1 << i)) != 0).ToList();
            if (eligible.Count == 0) eligible = contenders;
            var best = eligible.Max(i => values[i].Score);
            var winners = ClockwiseFromButton().Where(i => eligible.Contains(i) && values[i].Score == best).ToList();
            var share = pot.Amount / winners.Count;
            var remainder = pot.Amount % winners.Count;
            foreach (var w in winners)
            {
                var win = share + (w == winners[0] ? remainder : 0);
                var s = _seats[w]!;
                s.Won += win;
                if (!s.Leaving) s.Stack += win;
                pot.Winners |= (ushort)(1 << w);
                Emit(EventKind.WinPot, w, win, (byte)values[w].Category);
            }
        }
        _finalPots = pots;
        _nextAt = now + TimeSpan.FromSeconds(7 + 1.2 * streetsRunOut);
        Touch();
    }

    private void AwardUncontested(int winner, DateTime now)
    {
        ReturnUncalled();
        foreach (var i in SeatIndexes()) _seats[i]!.Bet = 0;
        var total = SeatIndexes().Sum(i => _seats[i]!.Committed);
        var w = _seats[winner]!;
        w.Won = total;
        if (!w.Leaving) w.Stack += total;
        _finalPots = [new PokerPotView { Amount = total, Eligible = (ushort)(1 << winner), Winners = (ushort)(1 << winner) }];
        Emit(EventKind.WinPot, winner, total, 255);
        _phase = PokerPhase.Showdown;
        _active = -1;
        _deadline = null;
        _needToAct.Clear();
        _nextAt = now + TimeSpan.FromSeconds(5);
        Touch();
    }

    /// <summary>
    /// The end-of-hand fairness reveal. Only the board cards that were dealt and the hands that were shown are opened,
    /// so nobody can work out what a player folded.
    /// </summary>
    private PokerReveal? BuildReveal()
    {
        if (!Rules.ProvablyFair || !_fairDeal || _mask is not { } mask) return null;
        var reveal = new PokerReveal { ServerSeed = _serverSeed, ClientSeeds = _handSeeds.ToList() };
        for (var leaf = 0; leaf < mask.Leaves; leaf++)
        {
            var open = leaf switch
            {
                PokerFair.FlopLeaf => _board.Count >= 3,
                PokerFair.TurnLeaf => _board.Count >= 4,
                PokerFair.RiverLeaf => _board.Count >= 5,
                _ => _seats[leaf - PokerFair.BoardLeaves] is { Dealt: true, Shown: true },
            };
            reveal.Leaves.Add(mask.Leaf(leaf, open));
        }
        return reveal;
    }

    /// <summary>True if the player may still turn their cards face up this hand (folded, or won without a showdown).</summary>
    public bool CanShow(int seat) =>
        _phase == PokerPhase.Showdown && IsSeated(seat) && _seats[seat] is { Dealt: true, Shown: false, Leaving: false, Hole.Length: 2 };

    private void ShowCards(int seat, DateTime now)
    {
        if (!CanShow(seat)) return;
        var s = _seats[seat]!;
        s.Shown = true;
        Emit(EventKind.ShowCards, seat, s.Hole[0].Code | (s.Hole[1].Code << 8), (byte)(s.Status == PokerSeatStatus.Folded ? 1 : 0));
        // Leave the cards on the table long enough for everyone to see them.
        var until = now + ShowLinger;
        if (_nextAt is { } n && n < until) _nextAt = until;
        Touch();
    }

    private void EndHand(DateTime now, bool startNext)
    {
        foreach (var i in SeatIndexes().ToList())
        {
            var s = _seats[i]!;
            if (s.Leaving)
            {
                _seats[i] = null;
                continue;
            }
            s.Stack += s.PendingChips;
            s.PendingChips = 0;
            s.Dealt = false;
            s.Hole = [];
            s.Bet = 0;
            s.Committed = 0;
            s.Shown = false;
            s.Won = 0;
            s.Last = PokerLast.None;
            s.Status = s.SittingOut ? PokerSeatStatus.SittingOut : PokerSeatStatus.Waiting;
        }
        _board.Clear();
        _finalPots = [];
        _sb = _bb = _active = -1;
        _phase = PokerPhase.Waiting;
        _deadline = null;
        _nextAt = now + TimeSpan.FromSeconds(0.5);
        Touch();
        if (startNext && EligibleCount() >= 2) BeginHand(now);
    }

    // ───────────── Snapshot ─────────────

    private static byte Seconds(DateTime? deadline, DateTime now) =>
        deadline is { } d ? (byte)Math.Clamp((int)Math.Ceiling((d - now).TotalSeconds), 0, 254) : (byte)255;

    private static byte B(int seat) => seat >= 0 ? (byte)seat : (byte)255;

    public IReadOnlyList<(int Seat, Card[] Cards)> PrivateCards()
    {
        if (!Betting(_phase)) return [];
        return SeatIndexes()
            .Where(i => _seats[i]!.Dealt && !_seats[i]!.Leaving && _seats[i]!.Hole.Length == 2)
            .Select(i => (i, _seats[i]!.Hole))
            .ToList();
    }

    public StateMsg BuildState(DateTime now)
    {
        var active = _active >= 0 ? _seats[_active] : null;
        var v = new PokerView
        {
            Phase = _phase,
            Hand = _hand,
            TurnId = _turnId,
            Button = B(_button),
            SmallBlindSeat = B(_sb),
            BigBlindSeat = B(_bb),
            ActiveSeat = B(_active),
            Allowed = _active >= 0 ? AllowedFor(_active) : PokerActions.None,
            DeadlineSec = Seconds(_deadline, now),
            CurrentBet = _currentBet,
            MinRaiseTo = MinRaiseTo(active),
            SmallBlind = Rules.SmallBlind,
            BigBlind = Rules.BigBlind,
            DealtMask = DealtMask(),
            Board = [.. _board],
            Pots = _phase == PokerPhase.Showdown
                ? _finalPots.Select(p => new PokerPotView { Amount = p.Amount, Eligible = p.Eligible, Winners = p.Winners }).ToList()
                : ComputePots(s => s.Committed - s.Bet),
            Commit = _phase is >= PokerPhase.Starting and <= PokerPhase.River ? _commit : null,
            Reveal = _phase == PokerPhase.Showdown ? BuildReveal() : null,
        };
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            v.Seats.Add(new PokerSeatView
            {
                Seat = (byte)i,
                Status = s.Dealt ? s.Status : s.SittingOut ? PokerSeatStatus.SittingOut : PokerSeatStatus.Waiting,
                Stack = s.Stack + s.PendingChips,
                Bet = s.Bet,
                Last = s.Last,
                Shown = _phase == PokerPhase.Showdown && s.Shown ? [.. s.Hole] : [],
                Won = s.Won,
            });
        }
        return new StateMsg { Game = GameKind.Poker, Poker = v };
    }
}
