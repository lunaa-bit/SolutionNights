using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Core.Blackjack;

/// <summary>
/// Authoritative blackjack table run by the host (the host is the dealer / house). Each round uses a freshly shuffled
/// shoe (continuous shuffle), so every round can be verified on its own.
/// </summary>
public sealed class BlackjackEngine : ITableGame
{
    private sealed class Hand
    {
        public long Bet;
        public readonly List<Card> Cards = [];
        public BjHandFlags Flags;
        public BjOutcome Outcome;
        public long Payout;

        public bool Finished => (Flags & (BjHandFlags.Stood | BjHandFlags.Busted | BjHandFlags.Surrendered | BjHandFlags.Natural)) != 0;
    }

    private sealed class SeatState
    {
        public long Bankroll;
        public long Bet;
        public byte[]? Seed;
        public bool SittingOut;
        public long Insurance;
        public InsuranceState Ins;
        public readonly List<Hand> Hands = [];
        public long RoundNet;
        public int Timeouts;
    }

    /// <summary>Extra time after a displayed deadline before the host acts for a player (covers chat latency).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private readonly SeatState?[] _seats;
    private readonly List<Card> _dealer = [];
    private readonly List<TableEvent> _events = [];
    private Card[] _deck = [];
    private byte[] _deckKey = [];
    private int _drawn;
    private BjPhase _phase = BjPhase.Waiting;
    private long _round;
    private long _turnId;
    private int _activeSeat = -1;
    private int _activeHand;
    private DateTime? _deadline;
    private DateTime? _nextAt;
    private byte[] _serverSeed = [];
    private byte[]? _commit;
    private FairReveal? _reveal;
    private List<SeatSeed> _roundSeeds = [];
    private bool _paused;

    public BlackjackEngine(BlackjackRules rules)
    {
        Rules = rules.Normalized();
        _seats = new SeatState?[Rules.MaxSeats];
    }

    public BlackjackRules Rules { get; }

    /// <summary>Test hook: replaces the shuffled shoe.</summary>
    internal Func<Card[]>? DeckOverride { get; set; }

    public GameKind Kind => GameKind.Blackjack;

    public int MaxSeats => Rules.MaxSeats;

    public long StartingStack => Rules.StartingStack;

    public int Version { get; private set; }

    public long Round => _round;

    public BjPhase Phase => _phase;

    public bool InRound => _phase is BjPhase.Insurance or BjPhase.PlayerTurns or BjPhase.DealerTurn;

    public bool AwaitingPlayers => _phase == BjPhase.Betting;

    public ushort TableId { get; set; }

    public long HouseNet { get; private set; }

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

    public IReadOnlyList<(int Seat, Card[] Cards)> PrivateCards() => [];

    // ───────────── Seats ─────────────

    public bool IsSeated(int seat) => seat >= 0 && seat < _seats.Length && _seats[seat] != null;

    public void Seat(int seat, long stack, DateTime now)
    {
        _seats[seat] = new SeatState { Bankroll = stack };
        if (_phase == BjPhase.Waiting) _nextAt = now + TimeSpan.FromSeconds(1.5);
        Touch();
    }

    public long Unseat(int seat, DateTime now)
    {
        if (!IsSeated(seat)) return 0;
        var s = _seats[seat]!;
        if (InRound && s.Hands.Count > 0)
        {
            // Chips in play are forfeited to the house.
            HouseNet -= s.RoundNet;
        }
        _seats[seat] = null;
        Touch();

        if (_phase == BjPhase.PlayerTurns && _activeSeat == seat)
            AdvanceTurn(now, seat + 1, 0);
        else if (_phase == BjPhase.Insurance && !AnyUndecided())
            ResolveInsurance(now);
        else if (_phase == BjPhase.Betting && !AnyEligible())
            _phase = BjPhase.Waiting;
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
        s.Timeouts = 0;
        if (sitOut && _phase == BjPhase.Betting) s.Bet = 0;
        Emit(sitOut ? EventKind.SatOut : EventKind.SatIn, seat);
        Touch();
    }

    public bool IsSittingOut(int seat) => IsSeated(seat) && _seats[seat]!.SittingOut;

    private IEnumerable<int> SeatIndexes() => Enumerable.Range(0, _seats.Length).Where(i => _seats[i] != null);

    private bool Eligible(SeatState s) => !s.SittingOut && s.Bankroll >= Rules.MinBet;

    private bool AnyEligible() => _seats.Any(s => s != null && Eligible(s));

    private bool AnyBets() => _seats.Any(s => s is { SittingOut: false, Bet: > 0 });

    private bool AllEligibleBet() => _seats.Where(s => s != null && Eligible(s)).All(s => s!.Bet > 0);

    private bool AnyUndecided() => _seats.Any(s => s is { Ins: InsuranceState.Undecided });

    // ───────────── Flow ─────────────

    public void StartNow(DateTime now)
    {
        switch (_phase)
        {
            case BjPhase.Waiting or BjPhase.Settled when AnyEligible():
                OpenBetting(now);
                break;
            case BjPhase.Betting when AnyBets():
                Deal(now);
                break;
        }
    }

    public void Tick(DateTime now)
    {
        switch (_phase)
        {
            case BjPhase.Waiting:
                if (Rules.AutoDeal && !Paused && AnyEligible() && (_nextAt == null || now >= _nextAt))
                    OpenBetting(now);
                break;

            case BjPhase.Betting:
                if (!AnyEligible())
                {
                    _phase = BjPhase.Waiting;
                    _deadline = null;
                    Touch();
                }
                else if (_deadline is { } d && now >= d)
                {
                    if (AnyBets()) Deal(now);
                    else
                    {
                        _deadline = null;
                        Touch();
                    }
                }
                break;

            case BjPhase.Insurance:
                if (_deadline is { } di && now >= di + Grace)
                {
                    foreach (var i in SeatIndexes())
                    {
                        var s = _seats[i]!;
                        if (s.Ins != InsuranceState.Undecided) continue;
                        s.Ins = InsuranceState.Declined;
                        Emit(EventKind.InsuranceDeclined, i);
                    }
                    ResolveInsurance(now);
                }
                break;

            case BjPhase.PlayerTurns:
                if (_deadline is { } dt && now >= dt + Grace && _activeSeat >= 0 && _seats[_activeSeat] is { } active)
                {
                    var hand = active.Hands[_activeHand];
                    hand.Flags |= BjHandFlags.Stood;
                    Emit(EventKind.Timeout, _activeSeat);
                    if (++active.Timeouts >= 2 && !active.SittingOut)
                    {
                        active.SittingOut = true;
                        Emit(EventKind.SatOut, _activeSeat);
                    }
                    Touch();
                    AdvanceTurn(now, _activeSeat, _activeHand + 1);
                }
                break;

            case BjPhase.Settled:
                if (_nextAt is { } n && now >= n)
                {
                    _nextAt = null;
                    if (!AnyEligible())
                    {
                        ClearTable();
                        _phase = BjPhase.Waiting;
                        Touch();
                    }
                    else if (Rules.AutoDeal && !Paused)
                    {
                        OpenBetting(now);
                    }
                }
                break;
        }
    }

    private void ClearTable()
    {
        _dealer.Clear();
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            s.Hands.Clear();
            s.Bet = 0;
            s.Seed = null;
            s.Insurance = 0;
            s.Ins = InsuranceState.None;
            s.RoundNet = 0;
        }
    }

    private void OpenBetting(DateTime now)
    {
        ClearTable();
        _round++;
        _phase = BjPhase.Betting;
        _serverSeed = Fair.NewServerSeed();
        _commit = Rules.ProvablyFair ? Fair.Commit(_serverSeed) : null;
        _reveal = null;
        _deadline = null;
        _activeSeat = -1;
        _turnId++;
        Emit(EventKind.BettingOpen, TableEvent.NoSeat, _round);
        Touch();
    }

    public void OnBet(int seat, BetMsg msg, DateTime now)
    {
        if (_phase != BjPhase.Betting || msg.Round != _round || !IsSeated(seat)) return;
        var s = _seats[seat]!;
        if (msg.Amount == 0)
        {
            s.Bet = 0;
            Touch();
            return;
        }
        if (msg.Amount < Rules.MinBet || msg.Amount > Rules.MaxBet || msg.Amount > s.Bankroll) return;
        if (s.SittingOut)
        {
            s.SittingOut = false;
            Emit(EventKind.SatIn, seat);
        }
        s.Bet = msg.Amount;
        s.Seed = msg.Seed.ToArray();
        s.Timeouts = 0;
        Emit(EventKind.BetPlaced, seat, msg.Amount);
        _deadline ??= now + TimeSpan.FromSeconds(Rules.BetSeconds);
        if (AllEligibleBet())
        {
            var soon = now + TimeSpan.FromSeconds(1.5);
            if (_deadline > soon) _deadline = soon;
        }
        Touch();
    }

    public void OnReady(int seat, ReadyMsg msg, DateTime now) { }

    private Card Draw()
    {
        if (_drawn >= _deck.Length)
            _deck = [.. _deck, .. Fair.Shuffle(Fair.ExtensionKey(_deckKey, _deck.Length / (Rules.Decks * 52)), Rules.Decks)];
        return _deck[_drawn++];
    }

    private void Deal(DateTime now)
    {
        var players = SeatIndexes().Where(i => _seats[i] is { SittingOut: false } s && s.Bet > 0 && s.Bet <= s.Bankroll).ToList();
        if (players.Count == 0) return;

        _roundSeeds = Rules.ProvablyFair
            ? players.Where(i => _seats[i]!.Seed != null).Select(i => new SeatSeed(i, _seats[i]!.Seed!)).ToList()
            : [];
        _deckKey = Fair.DeckKey(_serverSeed, TableId, _round, _roundSeeds);
        _deck = DeckOverride?.Invoke() ?? Fair.Shuffle(_deckKey, Rules.Decks);
        _drawn = 0;
        _dealer.Clear();

        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            s.Hands.Clear();
            s.Ins = InsuranceState.None;
            s.Insurance = 0;
            s.RoundNet = 0;
            if (!players.Contains(i)) s.Bet = 0;
        }
        foreach (var i in players)
        {
            var s = _seats[i]!;
            s.Bankroll -= s.Bet;
            s.RoundNet = -s.Bet;
            s.Hands.Add(new Hand { Bet = s.Bet });
        }

        foreach (var i in players) _seats[i]!.Hands[0].Cards.Add(Draw());
        _dealer.Add(Draw());
        foreach (var i in players) _seats[i]!.Hands[0].Cards.Add(Draw());
        _dealer.Add(Draw());

        Emit(EventKind.Dealt, TableEvent.NoSeat, players.Count);
        foreach (var i in players)
        {
            var h = _seats[i]!.Hands[0];
            if (!BjMath.IsNatural(h.Cards)) continue;
            h.Flags |= BjHandFlags.Natural;
            Emit(EventKind.Blackjack, i);
        }

        _deadline = null;
        if (_dealer[0].Rank == 14 && Rules.Insurance)
        {
            _phase = BjPhase.Insurance;
            foreach (var i in players)
            {
                var s = _seats[i]!;
                var cost = s.Bet / 2;
                s.Ins = cost > 0 && s.Bankroll >= cost ? InsuranceState.Undecided : InsuranceState.Declined;
            }
            Touch();
            if (!AnyUndecided())
            {
                ResolveInsurance(now);
                return;
            }
            _turnId++;
            _deadline = now + TimeSpan.FromSeconds(Math.Min(15, (int)Rules.TurnSeconds));
            return;
        }

        Touch();
        Peek(now);
    }

    private void ResolveInsurance(DateTime now)
    {
        _deadline = null;
        Peek(now);
    }

    /// <summary>American hole-card rule: the dealer checks for blackjack under an ace or ten.</summary>
    private void Peek(DateTime now)
    {
        var up = _dealer[0];
        if ((up.Rank == 14 || up.BlackjackPoints == 10) && BjMath.IsNatural(_dealer))
        {
            Emit(EventKind.DealerBlackjack);
            Settle(now, dealerBlackjack: true, dealerDraws: 0);
            return;
        }
        _phase = BjPhase.PlayerTurns;
        Touch();
        AdvanceTurn(now, 0, 0);
    }

    private BjActions AllowedFor(SeatState s, int handIndex)
    {
        var h = s.Hands[handIndex];
        if (h.Finished) return BjActions.None;
        var splitAces = h.Flags.HasFlag(BjHandFlags.SplitAces);
        if (splitAces && !Rules.HitSplitAces) return BjActions.Stand;

        var a = BjActions.Hit | BjActions.Stand;
        var two = h.Cards.Count == 2;
        var (total, soft) = BjMath.Evaluate(h.Cards);
        var fromSplit = h.Flags.HasFlag(BjHandFlags.FromSplit);
        if (two && s.Bankroll >= h.Bet && (Rules.DoubleAnyTwo || (!soft && total is >= 9 and <= 11)) && (!fromSplit || Rules.DoubleAfterSplit))
            a |= BjActions.Double;
        if (two && h.Cards[0].BlackjackPoints == h.Cards[1].BlackjackPoints && s.Hands.Count < Rules.MaxHands && s.Bankroll >= h.Bet
            && (!splitAces || Rules.ResplitAces))
            a |= BjActions.Split;
        if (two && Rules.LateSurrender && s.Hands.Count == 1 && !fromSplit)
            a |= BjActions.Surrender;
        return a;
    }

    /// <summary>Moves to the next unfinished hand at or after (seat, hand); the dealer plays when there is none.</summary>
    private void AdvanceTurn(DateTime now, int fromSeat, int fromHand)
    {
        for (var seat = Math.Max(0, fromSeat); seat < _seats.Length; seat++)
        {
            var s = _seats[seat];
            if (s == null || s.Hands.Count == 0) continue;
            for (var h = seat == fromSeat ? fromHand : 0; h < s.Hands.Count; h++)
            {
                var hand = s.Hands[h];
                if (hand.Cards.Count == 1)
                {
                    // Second card for a hand created by a split.
                    hand.Cards.Add(Draw());
                    if (hand.Flags.HasFlag(BjHandFlags.SplitAces) && !Rules.HitSplitAces) hand.Flags |= BjHandFlags.Stood;
                    else if (BjMath.Total(hand.Cards) == 21) hand.Flags |= BjHandFlags.Stood;
                    Touch();
                }
                if (hand.Finished) continue;
                _activeSeat = seat;
                _activeHand = h;
                _turnId++;
                _deadline = now + TimeSpan.FromSeconds(Rules.TurnSeconds);
                Touch();
                return;
            }
        }
        _activeSeat = -1;
        _activeHand = 0;
        DealerPlay(now);
    }

    public void OnAction(int seat, ActionMsg msg, DateTime now)
    {
        if (!IsSeated(seat) || msg.Round != _round) return;
        var s = _seats[seat]!;
        var kind = (BjActionKind)msg.Kind;

        if (_phase == BjPhase.Insurance)
        {
            if (s.Ins != InsuranceState.Undecided) return;
            if (kind == BjActionKind.TakeInsurance)
            {
                var cost = s.Hands[0].Bet / 2;
                s.Bankroll -= cost;
                s.RoundNet -= cost;
                s.Insurance = cost;
                s.Ins = InsuranceState.Taken;
                Emit(EventKind.InsuranceTaken, seat, cost);
            }
            else if (kind == BjActionKind.DeclineInsurance)
            {
                s.Ins = InsuranceState.Declined;
                Emit(EventKind.InsuranceDeclined, seat);
            }
            else return;
            s.Timeouts = 0;
            Touch();
            if (!AnyUndecided()) ResolveInsurance(now);
            return;
        }

        if (_phase != BjPhase.PlayerTurns || seat != _activeSeat || msg.Hand != _activeHand || msg.TurnId != _turnId) return;
        var h = s.Hands[_activeHand];
        var allowed = AllowedFor(s, _activeHand);
        switch (kind)
        {
            case BjActionKind.Hit when allowed.HasFlag(BjActions.Hit):
            {
                var c = Draw();
                h.Cards.Add(c);
                Emit(EventKind.Hit, seat, BjMath.Total(h.Cards), c.Code);
                AfterCard(seat, h);
                break;
            }
            case BjActionKind.Stand when allowed.HasFlag(BjActions.Stand):
                h.Flags |= BjHandFlags.Stood;
                Emit(EventKind.Stand, seat, BjMath.Total(h.Cards));
                break;
            case BjActionKind.Double when allowed.HasFlag(BjActions.Double):
            {
                s.Bankroll -= h.Bet;
                s.RoundNet -= h.Bet;
                h.Bet *= 2;
                h.Flags |= BjHandFlags.Doubled;
                var c = Draw();
                h.Cards.Add(c);
                Emit(EventKind.Double, seat, h.Bet, c.Code);
                AfterCard(seat, h);
                if (!h.Finished) h.Flags |= BjHandFlags.Stood;
                break;
            }
            case BjActionKind.Split when allowed.HasFlag(BjActions.Split):
            {
                s.Bankroll -= h.Bet;
                s.RoundNet -= h.Bet;
                var second = h.Cards[1];
                h.Cards.RemoveAt(1);
                var aces = second.Rank == 14;
                var extra = BjHandFlags.FromSplit | (aces ? BjHandFlags.SplitAces : BjHandFlags.None);
                h.Flags |= extra;
                var nh = new Hand { Bet = h.Bet, Flags = extra };
                nh.Cards.Add(second);
                s.Hands.Insert(_activeHand + 1, nh);
                Emit(EventKind.Split, seat, h.Bet);
                h.Cards.Add(Draw());
                if (aces && !Rules.HitSplitAces) h.Flags |= BjHandFlags.Stood;
                else if (BjMath.Total(h.Cards) == 21) h.Flags |= BjHandFlags.Stood;
                break;
            }
            case BjActionKind.Surrender when allowed.HasFlag(BjActions.Surrender):
                h.Flags |= BjHandFlags.Surrendered;
                Emit(EventKind.Surrender, seat, h.Bet / 2);
                break;
            default:
                return;
        }

        s.Timeouts = 0;
        Touch();
        if (h.Finished) AdvanceTurn(now, _activeSeat, _activeHand + 1);
        else
        {
            _turnId++;
            _deadline = now + TimeSpan.FromSeconds(Rules.TurnSeconds);
        }
    }

    private void AfterCard(int seat, Hand h)
    {
        var total = BjMath.Total(h.Cards);
        if (total > 21)
        {
            h.Flags |= BjHandFlags.Busted;
            Emit(EventKind.Bust, seat, total);
        }
        else if (total == 21)
        {
            h.Flags |= BjHandFlags.Stood;
        }
    }

    private void DealerPlay(DateTime now)
    {
        _phase = BjPhase.DealerTurn;
        var live = _seats.Any(s => s != null && s.Hands.Any(h => (h.Flags & (BjHandFlags.Busted | BjHandFlags.Surrendered | BjHandFlags.Natural)) == 0));
        var before = _dealer.Count;
        if (live)
        {
            while (true)
            {
                var (t, soft) = BjMath.Evaluate(_dealer);
                if (t < 17 || (t == 17 && soft && Rules.DealerHitsSoft17)) _dealer.Add(Draw());
                else break;
            }
        }
        var total = BjMath.Total(_dealer);
        Emit(total > 21 ? EventKind.DealerBust : EventKind.DealerStands, TableEvent.NoSeat, total);
        Settle(now, dealerBlackjack: false, dealerDraws: _dealer.Count - before);
    }

    private void Settle(DateTime now, bool dealerBlackjack, int dealerDraws)
    {
        var dealerTotal = BjMath.Total(_dealer);
        var dealerBust = dealerTotal > 21;
        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            if (s.Hands.Count == 0) continue;
            foreach (var h in s.Hands)
            {
                BjOutcome outcome;
                long payout;
                if (h.Flags.HasFlag(BjHandFlags.Surrendered))
                {
                    outcome = BjOutcome.Surrender;
                    payout = h.Bet / 2;
                }
                else if (h.Flags.HasFlag(BjHandFlags.Busted))
                {
                    outcome = BjOutcome.Lose;
                    payout = 0;
                }
                else if (h.Flags.HasFlag(BjHandFlags.Natural))
                {
                    (outcome, payout) = dealerBlackjack ? (BjOutcome.Push, h.Bet) : (BjOutcome.Blackjack, h.Bet + Rules.BlackjackWinnings(h.Bet));
                }
                else if (dealerBlackjack)
                {
                    (outcome, payout) = (BjOutcome.Lose, 0);
                }
                else
                {
                    var total = BjMath.Total(h.Cards);
                    (outcome, payout) = dealerBust || total > dealerTotal ? (BjOutcome.Win, h.Bet * 2)
                        : total == dealerTotal ? (BjOutcome.Push, h.Bet)
                        : (BjOutcome.Lose, 0);
                }
                h.Outcome = outcome;
                h.Payout = payout;
                s.Bankroll += payout;
                s.RoundNet += payout;
                Emit(outcome switch
                {
                    BjOutcome.Win => EventKind.Win,
                    BjOutcome.Blackjack => EventKind.Win,
                    BjOutcome.Push => EventKind.Push,
                    BjOutcome.Surrender => EventKind.Surrender,
                    _ => EventKind.Lose,
                }, i, outcome == BjOutcome.Lose ? h.Bet : payout, (byte)outcome);
            }
            if (s.Ins == InsuranceState.Taken && dealerBlackjack)
            {
                var pay = s.Insurance * 3;
                s.Bankroll += pay;
                s.RoundNet += pay;
                Emit(EventKind.InsurancePaid, i, pay);
            }
            HouseNet -= s.RoundNet;
        }

        _phase = BjPhase.Settled;
        _activeSeat = -1;
        _deadline = null;
        _reveal = Rules.ProvablyFair
            ? new FairReveal { ServerSeed = _serverSeed, ClientSeeds = _roundSeeds.ToList(), Drawn = _drawn }
            : null;
        // Leave time for clients to animate the dealer's cards before the next round opens.
        _nextAt = now + TimeSpan.FromSeconds(5 + 0.8 * (dealerDraws + 1));
        Touch();
    }

    // ───────────── Snapshot ─────────────

    private static byte Seconds(DateTime? deadline, DateTime now) =>
        deadline is { } d ? (byte)Math.Clamp((int)Math.Ceiling((d - now).TotalSeconds), 0, 254) : (byte)255;

    private BjSeatStatus StatusOf(SeatState s)
    {
        if (s.SittingOut && s.Hands.Count == 0) return BjSeatStatus.SittingOut;
        return _phase switch
        {
            BjPhase.Betting => s.Bet > 0 ? BjSeatStatus.BetPlaced : BjSeatStatus.Seated,
            BjPhase.Insurance or BjPhase.PlayerTurns or BjPhase.DealerTurn when s.Hands.Count > 0 =>
                s.Hands.All(h => h.Finished) ? BjSeatStatus.Done : BjSeatStatus.Playing,
            BjPhase.Settled when s.Hands.Count > 0 => BjSeatStatus.Done,
            _ => BjSeatStatus.Seated,
        };
    }

    public StateMsg BuildState(DateTime now)
    {
        var v = new BlackjackView
        {
            Phase = _phase,
            Round = _round,
            TurnId = _turnId,
            ActiveSeat = _activeSeat >= 0 ? (byte)_activeSeat : (byte)255,
            ActiveHand = (byte)_activeHand,
            DeadlineSec = Seconds(_deadline, now),
            Dealer = _phase is BjPhase.Insurance or BjPhase.PlayerTurns && _dealer.Count == 2 ? [_dealer[0], Card.Hidden] : [.. _dealer],
            Commit = _phase is BjPhase.Betting or BjPhase.Insurance or BjPhase.PlayerTurns or BjPhase.DealerTurn ? _commit : null,
            Reveal = _phase == BjPhase.Settled ? _reveal : null,
        };
        if (_phase == BjPhase.PlayerTurns && _activeSeat >= 0 && _seats[_activeSeat] is { } active)
            v.Allowed = AllowedFor(active, _activeHand);
        else if (_phase == BjPhase.Insurance)
            v.Allowed = BjActions.Insurance;

        foreach (var i in SeatIndexes())
        {
            var s = _seats[i]!;
            v.Seats.Add(new BjSeatView
            {
                Seat = (byte)i,
                Status = StatusOf(s),
                Bankroll = s.Bankroll,
                Bet = s.Bet,
                Insurance = s.Insurance,
                InsuranceState = s.Ins,
                RoundNet = s.RoundNet,
                Hands = s.Hands.Select(h => new BjHandView { Bet = h.Bet, Flags = h.Flags, Outcome = h.Outcome, Payout = h.Payout, Cards = [.. h.Cards] }).ToList(),
            });
        }
        return new StateMsg { Game = GameKind.Blackjack, Blackjack = v };
    }
}
