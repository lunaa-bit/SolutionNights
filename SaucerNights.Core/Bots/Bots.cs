using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Core.Bots;

/// <summary>A computer player driving a <see cref="ClientSession"/>, for practice tables and simulations.</summary>
public interface IBot
{
    void Tick(ClientSession s, DateTime now);
}

/// <summary>Gives bots a human-feeling pause before acting.</summary>
internal sealed class Thinker(Random rng, double minSeconds, double maxSeconds)
{
    private long _key = long.MinValue;
    private DateTime _readyAt;

    public bool Ready(long key, DateTime now)
    {
        if (key != _key)
        {
            _key = key;
            _readyAt = now + TimeSpan.FromSeconds(minSeconds + rng.NextDouble() * (maxSeconds - minSeconds));
            return false;
        }
        return now >= _readyAt;
    }
}

/// <summary>Plays textbook basic strategy and bets a few units.</summary>
public sealed class BlackjackBot : IBot
{
    private readonly Random _rng;
    private readonly Thinker _think;
    private long _betRound = -1;
    private long _actedTurn = -1;

    public BlackjackBot(int seed, double minThink = 0.7, double maxThink = 1.8)
    {
        _rng = new Random(seed);
        _think = new Thinker(_rng, minThink, maxThink);
    }

    public void Tick(ClientSession s, DateTime now)
    {
        if (!s.IsSeated || s.Blackjack is not { } v || s.AwaitingHost) return;
        var rules = s.Table.BlackjackRules ?? new BlackjackRules();
        var me = v.Seat(s.MySeat);
        if (me == null) return;

        switch (v.Phase)
        {
            case BjPhase.Betting when me.Bet == 0 && _betRound != v.Round:
                if (me.Bankroll < rules.MinBet)
                {
                    if (_think.Ready(v.Round * 10 + 1, now))
                    {
                        _betRound = v.Round;
                        s.RequestRebuy();
                    }
                    return;
                }
                if (!_think.Ready(v.Round * 10, now)) return;
                _betRound = v.Round;
                var units = new[] { 1, 1, 2, 2, 3, 5 }[_rng.Next(6)];
                var bet = Math.Min(Math.Min(rules.MinBet * units, rules.MaxBet), me.Bankroll);
                s.PlaceBet(bet);
                break;

            case BjPhase.Insurance when me.InsuranceState == InsuranceState.Undecided:
                if (_think.Ready(v.TurnId * 100 + s.MySeat, now)) s.Act((byte)BjActionKind.DeclineInsurance);
                break;

            case BjPhase.PlayerTurns when v.ActiveSeat == s.MySeat && _actedTurn != v.TurnId:
                if (!_think.Ready(v.TurnId, now)) return;
                _actedTurn = v.TurnId;
                var hand = me.Hands[v.ActiveHand];
                s.Act((byte)Decide(hand.Cards, v.Dealer[0], v.Allowed), 0, v.ActiveHand);
                break;
        }
    }

    public static BjActionKind Decide(IReadOnlyList<Card> cards, Card dealerUp, BjActions allowed)
    {
        var up = dealerUp.Rank == 14 ? 11 : dealerUp.BlackjackPoints;
        var (total, soft) = BjMath.Evaluate(cards);
        bool Can(BjActions a) => allowed.HasFlag(a);
        BjActionKind DoubleOr(BjActionKind fallback) => Can(BjActions.Double) ? BjActionKind.Double : fallback;

        if (Can(BjActions.Split) && cards.Count == 2)
        {
            var pair = cards[0].Rank == 14 ? 11 : cards[0].BlackjackPoints;
            var split = pair switch
            {
                11 or 8 => true,
                2 or 3 or 7 => up <= 7,
                6 => up <= 6,
                9 => up is <= 6 or 8 or 9,
                4 => up is 5 or 6,
                _ => false,
            };
            if (split) return BjActionKind.Split;
        }

        if (Can(BjActions.Surrender) && !soft && ((total == 16 && up >= 9) || (total == 15 && up == 10)))
            return BjActionKind.Surrender;

        if (soft)
        {
            return total switch
            {
                <= 14 => up is 5 or 6 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
                <= 16 => up is >= 4 and <= 6 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
                17 => up is >= 3 and <= 6 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
                18 => up is >= 3 and <= 6 ? DoubleOr(BjActionKind.Stand) : up >= 9 ? BjActionKind.Hit : BjActionKind.Stand,
                _ => BjActionKind.Stand,
            };
        }

        return total switch
        {
            <= 8 => BjActionKind.Hit,
            9 => up is >= 3 and <= 6 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
            10 => up <= 9 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
            11 => up <= 10 ? DoubleOr(BjActionKind.Hit) : BjActionKind.Hit,
            12 => up is >= 4 and <= 6 ? BjActionKind.Stand : BjActionKind.Hit,
            <= 16 => up <= 6 ? BjActionKind.Stand : BjActionKind.Hit,
            _ => BjActionKind.Stand,
        };
    }
}

/// <summary>A loose-but-sane hold'em player: preflop hand chart, Monte Carlo equity after the flop, pot odds.</summary>
public sealed class PokerBot : IBot
{
    private readonly Random _rng;
    private readonly Thinker _think;
    private readonly double _aggression;
    private long _actedTurn = -1;
    private long _rebuyHand = -1;
    private long _showHand = -1;

    public PokerBot(int seed, double minThink = 0.8, double maxThink = 2.2)
    {
        _rng = new Random(seed);
        _think = new Thinker(_rng, minThink, maxThink);
        _aggression = 0.15 + _rng.NextDouble() * 0.35;
    }

    public void Tick(ClientSession s, DateTime now)
    {
        if (!s.IsSeated || s.Poker is not { } v || s.AwaitingHost) return;
        var me = v.Seat(s.MySeat);
        if (me == null) return;

        if (v.Phase is PokerPhase.Waiting or PokerPhase.Showdown && me.Stack < v.BigBlind * 2 && _rebuyHand != v.Hand)
        {
            _rebuyHand = v.Hand;
            s.RequestRebuy();
            return;
        }

        // Now and then a bot shows what it folded, or the hand it won without a showdown.
        if (v.Phase == PokerPhase.Showdown && _showHand != v.Hand && s.CanShowCards)
        {
            _showHand = v.Hand;
            if (_rng.NextDouble() < 0.12) s.ShowCards();
            return;
        }

        if (v.ActiveSeat != s.MySeat || _actedTurn == v.TurnId || s.HoleHand != v.Hand || s.Hole.Length != 2) return;
        if (!_think.Ready(v.TurnId, now)) return;
        _actedTurn = v.TurnId;
        var (kind, amount) = Decide(v, me, s.Hole);
        s.Act((byte)kind, amount);
    }

    private (PokerActionKind, long) Decide(PokerView v, PokerSeatView me, Card[] hole)
    {
        var allowed = v.Allowed;
        var toCall = Math.Max(0, v.CurrentBet - me.Bet);
        var pot = v.PotTotal + v.Seats.Sum(x => x.Bet);
        var opponents = Math.Max(1, v.Seats.Count(x => x.Seat != me.Seat && x.InHand));
        var equity = v.Board.Count == 0 ? PreflopStrength(hole) : Equity(hole, v.Board, opponents, 250);
        var odds = toCall == 0 ? 0 : (double)toCall / (pot + toCall);
        var noise = (_rng.NextDouble() - 0.5) * 0.1;
        var e = equity + noise;
        var maxTo = me.Bet + me.Stack;

        long RaiseSize(double potFraction)
        {
            var target = v.CurrentBet + (long)Math.Max(v.BigBlind, (pot + toCall) * potFraction);
            target = Math.Max(target, v.MinRaiseTo);
            return Math.Min(target, maxTo);
        }

        var canRaise = (allowed & (PokerActions.Bet | PokerActions.Raise)) != 0;
        if (e > 0.82 && allowed.HasFlag(PokerActions.AllIn) && _rng.NextDouble() < 0.25) return (PokerActionKind.AllIn, 0);
        if (e > 0.62 + (1 - _aggression) * 0.15 && canRaise) return (PokerActionKind.BetOrRaise, RaiseSize(0.6 + _rng.NextDouble() * 0.5));
        if (toCall == 0)
        {
            if (canRaise && e > 0.5 && _rng.NextDouble() < _aggression) return (PokerActionKind.BetOrRaise, RaiseSize(0.5));
            return (PokerActionKind.Check, 0);
        }
        if (e >= odds + 0.04 || (toCall <= v.BigBlind && e > 0.3))
            return allowed.HasFlag(PokerActions.Call) ? (PokerActionKind.Call, 0) : (PokerActionKind.AllIn, 0);
        return (PokerActionKind.Fold, 0);
    }

    /// <summary>Rough 0–1 strength of two hole cards (Chen-style).</summary>
    public static double PreflopStrength(Card[] hole)
    {
        int hi = Math.Max(hole[0].Rank, hole[1].Rank), lo = Math.Min(hole[0].Rank, hole[1].Rank);
        double Points(int r) => r switch { 14 => 10, 13 => 8, 12 => 7, 11 => 6, _ => r / 2.0 };
        var score = Points(hi);
        if (hi == lo) score = Math.Max(5, score * 2);
        if (hole[0].Suit == hole[1].Suit) score += 2;
        var gap = hi - lo - 1;
        if (hi != lo)
        {
            score -= gap switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, _ => 5 };
            if (gap <= 1 && hi < 12) score += 1;
        }
        return Math.Clamp((score + 1) / 21.0, 0, 1);
    }

    public double Equity(Card[] hole, IReadOnlyList<Card> board, int opponents, int iterations)
    {
        var known = new HashSet<byte>(hole.Select(c => c.Code).Concat(board.Select(c => c.Code)));
        var deck = Enumerable.Range(0, 52).Select(i => (byte)i).Where(b => !known.Contains(b)).Select(b => new Card(b)).ToArray();
        double score = 0;
        var cards = new List<Card>(7);
        for (var it = 0; it < iterations; it++)
        {
            // Partial Fisher–Yates: take what we need from the front.
            var need = opponents * 2 + (5 - board.Count);
            for (var i = 0; i < need; i++)
            {
                var j = i + _rng.Next(deck.Length - i);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            var fullBoard = board.Concat(deck.Skip(opponents * 2).Take(5 - board.Count)).ToList();
            cards.Clear();
            cards.AddRange(hole);
            cards.AddRange(fullBoard);
            var mine = HandEvaluator.Evaluate(cards).Score;
            var best = 0;
            var ties = 0;
            for (var o = 0; o < opponents; o++)
            {
                cards.Clear();
                cards.Add(deck[o * 2]);
                cards.Add(deck[o * 2 + 1]);
                cards.AddRange(fullBoard);
                var their = HandEvaluator.Evaluate(cards).Score;
                if (their > best)
                {
                    best = their;
                    ties = 0;
                }
                if (their == best) ties++;
            }
            if (mine > best) score += 1;
            else if (mine == best) score += 1.0 / (ties + 1);
        }
        return score / iterations;
    }
}
