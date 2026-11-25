using SaucerNights.Core.Bots;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Net;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Roulette;
using SaucerNights.Core.Sessions;
using SaucerNights.Core.Wire;
using Xunit.Abstractions;

namespace SaucerNights.Tests;

/// <summary>The bet table and payouts.</summary>
public class RouletteBetTableTests
{
    private static int Spot(params int[] numbers) => RouletteBets.Find(RouletteBets.MaskOf(numbers));

    [Fact]
    public void EveryLegalBetIsListedOnce()
    {
        var all = RouletteBets.All;
        Assert.Equal(157, all.Count);
        Assert.True(all.Count < 256);

        var counts = all.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(37, counts[RouletteBetKind.Straight]);
        Assert.Equal(60, counts[RouletteBetKind.Split]);
        Assert.Equal(12, counts[RouletteBetKind.Street]);
        Assert.Equal(2, counts[RouletteBetKind.Trio]);
        Assert.Equal(22, counts[RouletteBetKind.Corner]);
        Assert.Equal(1, counts[RouletteBetKind.FirstFour]);
        Assert.Equal(11, counts[RouletteBetKind.SixLine]);
        Assert.Equal(3, counts[RouletteBetKind.Dozen]);
        Assert.Equal(3, counts[RouletteBetKind.Column]);
        foreach (var k in new[] { RouletteBetKind.Red, RouletteBetKind.Black, RouletteBetKind.Odd, RouletteBetKind.Even, RouletteBetKind.Low, RouletteBetKind.High })
            Assert.Equal(1, counts[k]);

        var size = new Dictionary<RouletteBetKind, int>
        {
            [RouletteBetKind.Straight] = 1, [RouletteBetKind.Split] = 2, [RouletteBetKind.Street] = 3, [RouletteBetKind.Trio] = 3,
            [RouletteBetKind.Corner] = 4, [RouletteBetKind.FirstFour] = 4, [RouletteBetKind.SixLine] = 6, [RouletteBetKind.Dozen] = 12,
            [RouletteBetKind.Column] = 12, [RouletteBetKind.Red] = 18, [RouletteBetKind.Black] = 18, [RouletteBetKind.Odd] = 18,
            [RouletteBetKind.Even] = 18, [RouletteBetKind.Low] = 18, [RouletteBetKind.High] = 18,
        };
        for (var i = 0; i < all.Count; i++)
        {
            var b = all[i];
            Assert.Equal(i, b.Index);
            Assert.Equal(size[b.Kind], b.Count);
            Assert.Equal(0UL, b.Mask >> 37);
            Assert.Equal(i, RouletteBets.Find(b.Mask));
        }
        Assert.Equal(all.Count, all.Select(b => b.Mask).Distinct().Count());
        Assert.Equal(all.Count, all.Select(b => b.Name).Distinct().Count());

        // Straights come first, so index n is a bet on n.
        for (var n = 0; n <= 36; n++)
        {
            Assert.Equal(RouletteBetKind.Straight, all[n].Kind);
            Assert.Equal(n.ToString(), all[n].Name);
            Assert.True(RouletteBets.Covers(n, n));
        }
    }

    [Fact]
    public void BetsFollowTheLayout()
    {
        var all = RouletteBets.All;
        foreach (var b in all)
        {
            var n = b.Numbers.ToArray();
            switch (b.Kind)
            {
                case RouletteBetKind.Split when n[0] == 0:
                    Assert.InRange(n[1], 1, 3);
                    break;
                case RouletteBetKind.Split:
                    Assert.True(n[1] - n[0] == 3 || (n[1] - n[0] == 1 && n[0] % 3 != 0), b.Name);
                    break;
                case RouletteBetKind.Street:
                    Assert.Equal(1, n[0] % 3);
                    Assert.Equal([n[0], n[0] + 1, n[0] + 2], n);
                    break;
                case RouletteBetKind.Corner:
                    Assert.NotEqual(0, n[0] % 3);
                    Assert.Equal([n[0], n[0] + 1, n[0] + 3, n[0] + 4], n);
                    break;
                case RouletteBetKind.SixLine:
                    Assert.Equal(1, n[0] % 3);
                    Assert.Equal(Enumerable.Range(n[0], 6), n);
                    break;
                case RouletteBetKind.Column:
                    Assert.All(n, x => Assert.Equal(n[0] % 3, x % 3));
                    break;
                case RouletteBetKind.Trio or RouletteBetKind.FirstFour:
                    Assert.Equal(0, n[0]);
                    break;
            }
        }

        Assert.Equal("Split 8/11", all[Spot(8, 11)].Name);
        Assert.Equal("Split 0/2", all[Spot(0, 2)].Name);
        Assert.Equal("Street 13-15", all[Spot(13, 14, 15)].Name);
        Assert.Equal("Corner 1/2/4/5", all[Spot(1, 2, 4, 5)].Name);
        Assert.Equal("Six line 1-6", all[Spot(1, 2, 3, 4, 5, 6)].Name);
        Assert.Equal("0/1/2", all[Spot(0, 1, 2)].Name);
        Assert.Equal(RouletteBetKind.FirstFour, all[Spot(0, 1, 2, 3)].Kind);
        Assert.Equal("1st 12", all[Spot(Enumerable.Range(1, 12).ToArray())].Name);
        Assert.Equal("Column 1", all[Spot(1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34)].Name);
        Assert.Equal("1-18", all[Spot(Enumerable.Range(1, 18).ToArray())].Name);
        Assert.Equal("Red", all[Spot(Enumerable.Range(1, 36).Where(RouletteBets.IsRed).ToArray())].Name);

        // Not bets: numbers that don't touch, a street across columns, nothing at all, a bit past 36.
        Assert.Equal(-1, Spot(1, 36));
        Assert.Equal(-1, Spot(3, 4));
        Assert.Equal(-1, Spot(0, 4));
        Assert.Equal(-1, Spot(2, 3, 4));
        Assert.Equal(-1, Spot(3, 4, 6, 7));
        Assert.Equal(-1, RouletteBets.Find(0));
        Assert.Equal(-1, RouletteBets.Find(1UL << 37));
        Assert.Equal(-1, RouletteBets.Find(RouletteBets.MaskOf(Enumerable.Range(0, 37))));
        Assert.False(RouletteBets.Covers(157, 1));
        Assert.False(RouletteBets.Covers(0, 37));
    }

    [Fact]
    public void WheelAndColours()
    {
        var wheel = RouletteBets.WheelOrder;
        Assert.Equal(37, wheel.Count);
        Assert.Equal(0, wheel[0]);
        Assert.Equal(Enumerable.Range(0, 37), wheel.OrderBy(n => n));
        Assert.Equal(18, Enumerable.Range(0, 37).Count(RouletteBets.IsRed));
        Assert.Equal(18, Enumerable.Range(0, 37).Count(RouletteBets.IsBlack));
        Assert.False(RouletteBets.IsRed(0) || RouletteBets.IsBlack(0));
        Assert.True(RouletteBets.IsRed(32) && RouletteBets.IsBlack(15) && RouletteBets.IsRed(1) && RouletteBets.IsBlack(2));
        // Around the wheel the colours alternate.
        for (var i = 1; i < 36; i++) Assert.NotEqual(RouletteBets.IsRed(wheel[i]), RouletteBets.IsRed(wheel[i + 1]));
    }

    [Fact]
    public void PayoutsMatchTheOdds()
    {
        var odds = new Dictionary<RouletteBetKind, int>
        {
            [RouletteBetKind.Straight] = 35, [RouletteBetKind.Split] = 17, [RouletteBetKind.Street] = 11, [RouletteBetKind.Trio] = 11,
            [RouletteBetKind.Corner] = 8, [RouletteBetKind.FirstFour] = 8, [RouletteBetKind.SixLine] = 5, [RouletteBetKind.Dozen] = 2,
            [RouletteBetKind.Column] = 2, [RouletteBetKind.Red] = 1, [RouletteBetKind.Black] = 1, [RouletteBetKind.Odd] = 1,
            [RouletteBetKind.Even] = 1, [RouletteBetKind.Low] = 1, [RouletteBetKind.High] = 1,
        };
        foreach (var b in RouletteBets.All)
        {
            Assert.Equal(odds[b.Kind], b.Multiplier);
            Assert.Equal(odds[b.Kind], RouletteBets.Multiplier(b.Index));
            for (var n = 0; n < 37; n++)
                Assert.Equal(b.Covers(n) ? 10 + 10 * odds[b.Kind] : 0, RouletteBets.Payout(b.Index, 10, n));
        }
    }
}

/// <summary>The roulette engine on its own, with a fixed clock.</summary>
public class RouletteEngineTests
{
    private readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static int Spot(params int[] numbers) => RouletteBets.Find(RouletteBets.MaskOf(numbers));

    internal static readonly int Red = RouletteBets.All.First(b => b.Kind == RouletteBetKind.Red).Index;
    internal static readonly int Black = RouletteBets.All.First(b => b.Kind == RouletteBetKind.Black).Index;
    internal static readonly int Even = RouletteBets.All.First(b => b.Kind == RouletteBetKind.Even).Index;

    internal static RouletteBetMsg Msg(long round, params (int Index, long Amount)[] bets) => Msg(round, new byte[8], bets);

    internal static RouletteBetMsg Msg(long round, byte[] seed, params (int Index, long Amount)[] bets) =>
        new() { Round = round, Seed = seed, Bets = bets.Select(b => ((byte)b.Index, b.Amount)).ToList() };

    private static RouletteRules Manual => new() { AutoSpin = false, ProvablyFair = false, MinBet = 10, MaxBet = 500, TableMax = 2000 };

    private RouletteEngine Table(RouletteRules? rules = null, params long[] stacks)
    {
        var e = new RouletteEngine(rules ?? Manual);
        if (stacks.Length == 0) stacks = [1000];
        for (var i = 0; i < stacks.Length; i++) e.Seat(i, stacks[i], _now);
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        return e;
    }

    private RouletteView View(RouletteEngine e, DateTime? at = null) => e.BuildState(at ?? _now).Roulette!;

    /// <summary>Spins with a chosen number and lets the wheel stop.</summary>
    private void SpinTo(RouletteEngine e, int number)
    {
        e.ResultOverride = () => number;
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Spinning, e.Phase);
        Assert.Equal(number, View(e).Result);
        e.Tick(_now + TimeSpan.FromSeconds(RouletteRules.SpinSeconds));
        Assert.Equal(RoulettePhase.Settled, e.Phase);
    }

    [Fact]
    public void EveryKindOfBetIsPaidOnSeventeen()
    {
        var e = Table();
        var bets = new (int, long)[]
        {
            (17, 10), (Spot(17, 20), 10), (Spot(16, 17, 18), 10), (Spot(13, 14, 16, 17), 10), (Spot(13, 14, 15, 16, 17, 18), 10),
            (Spot(Enumerable.Range(13, 12).ToArray()), 10), (Spot(Enumerable.Range(0, 12).Select(c => 3 * c + 2).ToArray()), 10),
            (Black, 10), (Spot(Enumerable.Range(0, 18).Select(i => 2 * i + 1).ToArray()), 10), (Spot(Enumerable.Range(1, 18).ToArray()), 10),
            (0, 10), (Red, 10),
        };
        e.OnRouletteBet(0, Msg(e.Round, bets), _now);
        Assert.Equal(880, View(e).Seats[0].Bankroll);
        Assert.Equal(12, View(e).Seats[0].Bets.Count);

        e.DrainEvents();
        SpinTo(e, 17);
        // 35 + 17 + 11 + 8 + 5 + 2 + 2 + 1 + 1 + 1 to one, stakes back on the ten winners.
        var paid = 360 + 180 + 120 + 90 + 60 + 30 + 30 + 20 + 20 + 20;
        var v = View(e);
        Assert.Equal(1000 - 120 + paid, v.Seats[0].Bankroll);
        Assert.Equal(paid - 120, v.Seats[0].RoundNet);
        Assert.Equal(-(paid - 120), e.HouseNet);
        Assert.Equal(12, v.Seats[0].Bets.Count); // still on show until the next spin
        var events = e.DrainEvents();
        Assert.Contains(new TableEvent(EventKind.RouletteResult, TableEvent.NoSeat, 17), events);
        Assert.Contains(new TableEvent(EventKind.RouletteWin, 0, paid, 10), events);
    }

    [Fact]
    public void ZeroPaysTheZeroBetsAndBeatsTheOutsideBets()
    {
        var e = Table();
        e.OnRouletteBet(0, Msg(e.Round, (Spot(0, 1), 10), (Spot(0, 1, 2), 10), (Spot(0, 2, 3), 10), (Spot(0, 1, 2, 3), 10), (0, 10),
            (Red, 100), (Black, 100), (Even, 50), (Spot(1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34), 10)), _now);
        SpinTo(e, 0);
        var v = View(e);
        Assert.Equal(1000 - 310 + 180 + 120 + 120 + 90 + 360, v.Seats[0].Bankroll);
        Assert.Equal(0, v.Result);
        Assert.Equal([0], v.History);
    }

    [Fact]
    public void ATableSpinsBySelfWhenAutoSpinIsOn()
    {
        var e = new RouletteEngine(new RouletteRules { ProvablyFair = true, BetSeconds = 20 }) { TableId = 77 };
        e.Seat(0, 1000, _now);
        e.Seat(1, 1000, _now);
        e.Tick(_now + TimeSpan.FromSeconds(1));
        Assert.Equal(RoulettePhase.Waiting, e.Phase);
        var t = _now + TimeSpan.FromSeconds(2);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        var v = View(e, t);
        Assert.Equal(1, v.Round);
        Assert.Equal(255, v.DeadlineSec); // the clock starts with the first bet
        Assert.False(v.HasResult);
        var commit = v.Commit;
        Assert.NotNull(commit);

        e.Tick(t + TimeSpan.FromSeconds(60));
        Assert.Equal(RoulettePhase.Betting, e.Phase);

        t += TimeSpan.FromSeconds(60);
        var seed = Fair.NewClientSeed();
        e.OnRouletteBet(0, Msg(e.Round, seed, (Red, 100), (17, 20)), t);
        Assert.Equal(20, View(e, t).DeadlineSec);
        Assert.Equal(120, e.ChipsOnTable);

        // The displayed deadline passes, then a little grace for lines still in flight.
        e.Tick(t + TimeSpan.FromSeconds(20));
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        Assert.Equal(0, View(e, t + TimeSpan.FromSeconds(20)).DeadlineSec);
        t += TimeSpan.FromSeconds(20) + RouletteEngine.Grace;
        e.Tick(t);
        Assert.Equal(RoulettePhase.Spinning, e.Phase);
        Assert.True(e.InRound);
        v = View(e, t);
        Assert.True(v.HasResult);
        Assert.Equal(RouletteRules.SpinSeconds, v.DeadlineSec);
        Assert.Equal(-120, v.Seats[0].RoundNet);
        var result = v.Result;

        // Too late to bet.
        e.OnRouletteBet(1, Msg(e.Round, (Black, 100)), t);
        Assert.Empty(View(e, t).Seats[1].Bets);

        t += TimeSpan.FromSeconds(RouletteRules.SpinSeconds);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        v = View(e, t);
        Assert.Equal(result, v.Result);
        Assert.Equal([result], v.History);
        Assert.Null(v.Commit);
        Assert.NotNull(v.Reveal);
        var paid = RouletteBets.Payout(Red, 100, result) + RouletteBets.Payout(17, 20, result);
        Assert.Equal(880 + paid, v.Seats[0].Bankroll);
        Assert.Equal(-(paid - 120), e.HouseNet);
        Assert.Equal(0, e.ChipsOnTable);

        var check = Verifier.Roulette(v, commit, 77, 0, seed, t);
        Assert.Equal(FairStatus.Verified, check.Status);

        // After a short pause betting opens again with a clean layout.
        e.Tick(t + RouletteEngine.ResultPause);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        v = View(e, t);
        Assert.Equal(2, v.Round);
        Assert.False(v.HasResult);
        Assert.Equal([result], v.History);
        Assert.All(v.Seats, s => Assert.Empty(s.Bets));
        Assert.All(v.Seats, s => Assert.Equal(0, s.RoundNet));
    }

    [Fact]
    public void TheHostStartsEachSpinWhenAutoSpinIsOff()
    {
        var e = new RouletteEngine(Manual);
        e.Seat(0, 1000, _now);
        e.Tick(_now + TimeSpan.FromMinutes(5));
        Assert.Equal(RoulettePhase.Waiting, e.Phase);
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        e.StartNow(_now); // nothing down: nothing to spin
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        e.OnRouletteBet(0, Msg(e.Round, (Red, 50)), _now);
        SpinTo(e, 3);
        Assert.Equal(1050, e.StackOf(0));
        e.Tick(_now + TimeSpan.FromMinutes(5));
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        Assert.Equal(2, e.Round);
    }

    [Fact]
    public void BetsReplaceEachOtherAndCanBeTakenBack()
    {
        var e = Table();
        e.OnRouletteBet(0, Msg(e.Round, (Red, 100)), _now);
        Assert.Equal(900, e.StackOf(0));
        e.OnRouletteBet(0, Msg(e.Round, (Black, 50), (17, 10)), _now);
        Assert.Equal(940, e.StackOf(0));
        Assert.Equal([((byte)Black, 50L), ((byte)17, 10L)], View(e).Seats[0].Bets);

        // The same list again (a repeated line) changes nothing and logs nothing.
        e.DrainEvents();
        var version = e.Version;
        e.OnRouletteBet(0, Msg(e.Round, (17, 10), (Black, 50)), _now);
        Assert.Equal(version, e.Version);
        Assert.Empty(e.DrainEvents());

        e.OnRouletteBet(0, Msg(e.Round), _now);
        Assert.Equal(1000, e.StackOf(0));
        Assert.Empty(View(e).Seats[0].Bets);
        Assert.Contains(new TableEvent(EventKind.RouletteBets, 0, 0, 0), e.DrainEvents());
    }

    [Fact]
    public void InvalidBetListsAreIgnoredWhole()
    {
        var e = Table(new RouletteRules { AutoSpin = false, ProvablyFair = false, MinBet = 10, MaxBet = 200, TableMax = 300 }, 1000, 150);
        e.OnRouletteBet(0, Msg(e.Round, (Red, 100)), _now);
        e.OnRouletteBet(1, Msg(e.Round, (Red, 100)), _now);
        var before = View(e);
        var version = e.Version;

        void Rejected(int seat, RouletteBetMsg msg)
        {
            e.OnRouletteBet(seat, msg, _now);
            Assert.Equal(version, e.Version);
            var after = View(e);
            for (var i = 0; i < before.Seats.Count; i++)
            {
                Assert.Equal(before.Seats[i].Bankroll, after.Seats[i].Bankroll);
                Assert.Equal(before.Seats[i].Bets, after.Seats[i].Bets);
            }
        }

        Rejected(0, Msg(e.Round, (Red, 200), (Black, 200))); // over the table max
        Rejected(1, Msg(e.Round, (Red, 100), (Black, 100))); // more than they have (150 with what's down)
        Rejected(0, Msg(e.Round, Enumerable.Range(1, 13).Select(n => (n, 10L)).ToArray())); // 13 spots
        Rejected(0, Msg(e.Round, (Red, 5))); // under the minimum
        Rejected(0, Msg(e.Round, (Red, 201))); // over the spot maximum
        Rejected(0, Msg(e.Round, (157, 10))); // no such bet
        Rejected(0, Msg(e.Round, (Red, 10), (Red, 10))); // same spot twice
        Rejected(0, Msg(e.Round + 1, (Black, 10))); // wrong round
        Rejected(0, Msg(e.Round - 1, (Black, 10)));
        Rejected(4, Msg(e.Round, (Black, 10))); // nobody sits there

        // The same checks are open to the UI.
        var rules = e.Rules;
        Assert.Null(rules.BetProblem([((byte)Red, 200), ((byte)Black, 100)], 300));
        Assert.NotNull(rules.BetProblem([((byte)Red, 200), ((byte)Black, 101)], 1000));
        Assert.NotNull(rules.BetProblem([((byte)Red, 200)], 199));
        Assert.NotNull(rules.BetProblem([(157, 10)], 1000));

        // Twelve spots right at the limits are fine.
        e.OnRouletteBet(0, Msg(e.Round, Enumerable.Range(1, 12).Select(n => (n, 25L)).ToArray()), _now);
        Assert.Equal(700, e.StackOf(0));

        e.ResultOverride = () => 5;
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Spinning, e.Phase);
        before = View(e);
        version = e.Version;
        Rejected(1, Msg(e.Round, (5, 10))); // betting closed
        Rejected(0, Msg(e.Round)); // and nothing can be taken back
    }

    [Fact]
    public void LeavingDuringBettingTakesTheBetsBack()
    {
        var e = Table(null, 1000, 1000);
        e.OnRouletteBet(0, Msg(e.Round, (Red, 300)), _now);
        Assert.Equal(1000, e.Unseat(0, _now));
        Assert.Equal(0, e.HouseNet);

        e.OnRouletteBet(1, Msg(e.Round, (Red, 300)), _now);
        e.SetSittingOut(1, true, _now);
        Assert.Equal(1000, e.StackOf(1));
        Assert.Equal(0, e.ChipsOnTable);
        e.OnRouletteBet(1, Msg(e.Round, (Red, 30)), _now); // betting again sits them back in
        Assert.False(e.IsSittingOut(1));
        Assert.Equal(970, e.StackOf(1));
    }

    [Fact]
    public void LeavingDuringTheSpinForfeitsTheBets()
    {
        var e = Table(null, 1000, 1000);
        e.OnRouletteBet(0, Msg(e.Round, (17, 300)), _now);
        e.OnRouletteBet(1, Msg(e.Round, (Red, 10)), _now);
        e.ResultOverride = () => 17;
        e.StartNow(_now);
        Assert.Equal(700, e.Unseat(0, _now));
        Assert.Equal(300, e.HouseNet);
        e.Tick(_now + TimeSpan.FromSeconds(RouletteRules.SpinSeconds));
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        Assert.Equal(310, e.HouseNet); // 17 is black
        Assert.Single(View(e).Seats);
    }

    [Fact]
    public void PausingLetsTheSpinFinishThenWaits()
    {
        var e = new RouletteEngine(new RouletteRules { ProvablyFair = false });
        e.Seat(0, 1000, _now);
        var t = _now + TimeSpan.FromSeconds(2);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        e.Paused = true;
        Assert.Equal(RoulettePhase.Waiting, e.Phase); // nothing down: betting just closes
        e.Paused = false;
        e.Tick(t);
        Assert.Equal(RoulettePhase.Betting, e.Phase);

        e.OnRouletteBet(0, Msg(e.Round, (Red, 100)), t);
        e.Paused = true;
        Assert.Equal(RoulettePhase.Betting, e.Phase);
        t += TimeSpan.FromSeconds(30);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Spinning, e.Phase);
        t += TimeSpan.FromSeconds(RouletteRules.SpinSeconds);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        e.Tick(t + TimeSpan.FromMinutes(2));
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        e.StartNow(t); // paused: no new spin, even by hand
        Assert.Equal(RoulettePhase.Settled, e.Phase);
        e.Paused = false;
        t += TimeSpan.FromMinutes(2);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Betting, e.Phase);

        // Bets taken back after pausing: the empty window closes too.
        e.OnRouletteBet(0, Msg(e.Round, (Red, 100)), t);
        e.Paused = true;
        e.OnRouletteBet(0, Msg(e.Round), t);
        e.Tick(t);
        Assert.Equal(RoulettePhase.Waiting, e.Phase);
        Assert.Equal(e.StackOf(0), View(e, t).Seats[0].Bankroll);
        Assert.Equal(0, e.ChipsOnTable);
    }

    [Fact]
    public void ABrokeTableGoesBackToWaiting()
    {
        var e = Table(null, 100);
        e.OnRouletteBet(0, Msg(e.Round, (Red, 100)), _now);
        SpinTo(e, 2); // black: the player is out of chips
        e.Tick(_now + TimeSpan.FromMinutes(1));
        Assert.Equal(RoulettePhase.Waiting, e.Phase);
        Assert.Empty(View(e).Seats[0].Bets);
        e.AddChips(0, 1000);
        e.StartNow(_now);
        Assert.Equal(RoulettePhase.Betting, e.Phase);
    }

    [Fact]
    public void RulesTravelInTheAnnouncement()
    {
        var rules = new RouletteRules
        {
            MinBet = 25, MaxBet = 1000, TableMax = 5000, StartingStack = 20000, MaxSeats = 8, BetSeconds = 45, ProvablyFair = false, AutoSpin = false,
        };
        var back = RouletteRules.FromBytes(rules.ToBytes());
        Assert.Equal(rules.ToBytes(), back.ToBytes());
        Assert.Equal((25L, 1000L, 5000L, 20000L, (byte)8, (byte)45, false, false),
            (back.MinBet, back.MaxBet, back.TableMax, back.StartingStack, back.MaxSeats, back.BetSeconds, back.ProvablyFair, back.AutoSpin));

        var odd = new RouletteRules { MaxSeats = 20, BetSeconds = 1, MinBet = 50, MaxBet = 10, TableMax = 20, StartingStack = 5 }.Normalized();
        Assert.Equal(((byte)8, (byte)5, 50L, 50L, 50L, 50L), (odd.MaxSeats, odd.BetSeconds, odd.MinBet, odd.MaxBet, odd.TableMax, odd.StartingStack));
        Assert.Equal((byte)1, new RouletteRules { MaxSeats = 0 }.Normalized().MaxSeats);
        Assert.Equal((byte)60, new RouletteRules { BetSeconds = 200 }.Normalized().BetSeconds);

        var clock = new ManualClock(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        using var host = new HostSession(new RouletteEngine(rules), new HostOptions { HostId = "Croupier@Ultros", Title = "Wheel" }, clock);
        var announce = (AnnounceMsg)Messages.Parse(host.BuildAnnounce().ToFrame(host.TableId, 1))!;
        Assert.Equal(GameKind.Roulette, announce.Game);
        Assert.Equal(2, announce.Version);
        var info = TableInfo.From(announce, host.TableId, host.HostId, clock.Now);
        Assert.Equal(rules.ToBytes(), info.RouletteRules!.ToBytes());
        Assert.Null(info.BlackjackRules);
        Assert.Equal($"{25:N0} - {1000:N0}", info.StakesText);
        Assert.False(info.Flags.HasFlag(TableFlags.ProvablyFair));
        Assert.Equal(8, info.SeatsMax);

        using var fair = new HostSession(new RouletteEngine(new RouletteRules()), new HostOptions { HostId = "Croupier@Ultros" }, clock);
        Assert.True(fair.Flags.HasFlag(TableFlags.ProvablyFair));
    }

    [Fact]
    public void AFullSnapshotFitsInAFewLines()
    {
        var v = new RouletteView
        {
            Phase = RoulettePhase.Settled,
            Round = 123_456,
            Result = 36,
            History = Enumerable.Range(0, 12).Select(i => (byte)(i * 3)).ToList(),
            Reveal = new FairReveal { ServerSeed = Fair.NewServerSeed(), ClientSeeds = Enumerable.Range(0, 8).Select(i => new SeatSeed(i, Fair.NewClientSeed())).ToList() },
        };
        for (var s = 0; s < 8; s++)
        {
            v.Seats.Add(new RouletteSeatView
            {
                Seat = (byte)s,
                Status = RouletteSeatStatus.Active,
                Bankroll = 1_234_567,
                RoundNet = -6000,
                Bets = Enumerable.Range(0, 12).Select(i => ((byte)(140 + i), 500L)).ToList(),
            });
        }
        var st = new StateMsg
        {
            Game = GameKind.Roulette,
            RosterVersion = 9,
            Roulette = v,
            Events = Enumerable.Range(0, 12).Select(i => new TableEvent(EventKind.RouletteWin, (byte)(i % 8), 123_456, 12)).ToList(),
        };
        var frame = st.ToFrame(4321, 99);
        byte frag = 0;
        var lines = FrameCodec.ToLines(frame, ref frag);
        Assert.True(lines.Count <= 3, $"{lines.Count} lines");
        Assert.All(lines, l => Assert.True(l.Length <= FrameCodec.DefaultMaxLineChars));

        var back = ((StateMsg)Messages.Parse(frame)!).Roulette!;
        Assert.Equal(v.Phase, back.Phase);
        Assert.Equal(v.Round, back.Round);
        Assert.Equal(v.Result, back.Result);
        Assert.Equal(v.History, back.History);
        Assert.Equal(8, back.Seats.Count);
        Assert.Equal(v.Seats[7].Bets, back.Seats[7].Bets);
        Assert.Equal(-6000, back.Seats[3].RoundNet);
        Assert.Equal(v.Reveal.ServerSeed, back.Reveal!.ServerSeed);
        Assert.Null(back.Commit);

        // A bet index past the table is a broken snapshot, not a crash later.
        var bad = new RouletteView { Seats = [new RouletteSeatView { Bets = [(200, 10)] }] };
        var w = new ByteWriter();
        bad.Write(w);
        Assert.Throws<ProtocolException>(() => RouletteView.Read(new ByteReader(w.ToArray())));
    }

    [Fact]
    public void BetMessagesRoundTrip()
    {
        var msg = Msg(77, Fair.NewClientSeed(), (Red, 100), (17, 5_000_000));
        var back = (RouletteBetMsg)Messages.Parse(msg.ToFrame(1, 1))!;
        Assert.Equal(77, back.Round);
        Assert.Equal(msg.Seed, back.Seed);
        Assert.Equal(msg.Bets, back.Bets);
    }

    [Fact]
    public void TheVerifierCatchesTampering()
    {
        var e = new RouletteEngine(new RouletteRules { AutoSpin = false, ProvablyFair = true }) { TableId = 5 };
        e.Seat(0, 1000, _now);
        e.Seat(1, 1000, _now);
        e.StartNow(_now);
        var commit = View(e).Commit!;
        var mine = Fair.NewClientSeed();
        e.OnRouletteBet(0, Msg(e.Round, mine, (Red, 100)), _now);
        e.OnRouletteBet(1, Msg(e.Round, Fair.NewClientSeed(), (17, 10)), _now);
        e.StartNow(_now);
        e.Tick(_now + TimeSpan.FromSeconds(10));
        var v = View(e);
        Assert.Equal(2, v.Reveal!.ClientSeeds.Count);

        Assert.Equal(FairStatus.Verified, Verifier.Roulette(v, commit, 5, 0, mine, _now).Status);
        Assert.Equal(FairStatus.Verified, Verifier.Roulette(v, commit, 5, -1, null, _now).Status); // a spectator
        Assert.Equal(FairStatus.Unverifiable, Verifier.Roulette(v, null, 5, 0, mine, _now).Status);
        Assert.Equal(FairStatus.Failed, Verifier.Roulette(v, Fair.Commit(Fair.NewServerSeed()), 5, 0, mine, _now).Status);
        Assert.Equal(FairStatus.Failed, Verifier.Roulette(v, commit, 5, 0, Fair.NewClientSeed(), _now).Status);

        var moved = Copy(v);
        moved.Result = (byte)((v.Result + 1) % 37);
        Assert.Equal(FairStatus.Failed, Verifier.Roulette(moved, commit, 5, 0, mine, _now).Status);

        var dropped = Copy(v);
        dropped.Reveal!.ClientSeeds.RemoveAll(s => s.Seat == 1);
        Assert.Equal(FairStatus.Failed, Verifier.Roulette(dropped, commit, 5, 0, mine, _now).Status);

        static RouletteView Copy(RouletteView v)
        {
            var w = new ByteWriter();
            v.Write(w);
            return RouletteView.Read(new ByteReader(w.ToArray()));
        }
    }

    [Fact]
    public void TheFairDrawIsUniform()
    {
        var serverSeed = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        SeatSeed[] seeds = [new(0, [1, 2, 3, 4, 5, 6, 7, 8]), new(3, [9, 9, 9, 9, 9, 9, 9, 9])];
        var counts = new int[37];
        const int spins = 37_000;
        for (var round = 1; round <= spins; round++) counts[Fair.RouletteNumber(serverSeed, 4242, round, seeds)]++;

        // 1,000 expected each; the standard deviation is about 31.
        Assert.All(counts, c => Assert.InRange(c, 850, 1150));
        var chi = counts.Sum(c => (c - 1000.0) * (c - 1000.0) / 1000.0);
        Assert.True(chi < 70, $"chi-square {chi:F1}"); // 36 degrees of freedom: 99.9% under 68
    }
}

/// <summary>Roulette tables of bots over a simulated linkshell using the real chat path, as in <see cref="SimulationTests"/>.</summary>
public class RouletteTableTests(ITestOutputHelper output)
{
    private sealed class Sim
    {
        public required ManualClock Clock;
        public required HostSession Host;
        public required LoopbackNetwork Net;
        public required List<(ClientSession Client, IBot Bot)> Players;

        public void Run(TimeSpan duration, Action? check = null)
        {
            var end = Clock.Now + duration;
            while (Clock.Now < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(100));
                Net.Tick(Clock.Now);
                foreach (var (c, b) in Players) b.Tick(c, Clock.Now);
                // Like a real host, approve rebuy requests after a few seconds.
                foreach (var r in Host.RebuyRequests.Where(r => Clock.Now - r.At > TimeSpan.FromSeconds(3)).ToList())
                    Host.ApproveRebuy(r.Id);
                check?.Invoke();
            }
        }
    }

    private static Sim Build(RouletteEngine game, int players, bool chat = true, double dropRate = 0, int seed = 1)
    {
        var clock = new ManualClock(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        var host = new HostSession(game, new HostOptions { HostId = "Croupier@Ultros", HostName = "Croupier", Title = "Sim" }, clock);
        var net = new LoopbackNetwork(host, TimeSpan.FromMilliseconds(400), chatEncoding: chat, rateLimit: chat ? TimeSpan.FromMilliseconds(1200) : null, seed: seed)
        {
            DropRate = dropRate,
        };
        var list = new List<(ClientSession, IBot)>();
        for (var i = 0; i < players; i++)
        {
            var c = new ClientSession(host.Info(clock.Now), $"Player{i}@World{i % 3}", clock);
            net.Add(c);
            c.Join();
            list.Add((c, new RouletteBot(seed * 100 + i, 0.3, 3.0)));
        }
        return new Sim { Clock = clock, Host = host, Net = net, Players = list };
    }

    [Fact]
    public void RouletteTableRunsOverChat()
    {
        // Small stacks so players go broke and buy back in now and then.
        var game = new RouletteEngine(new RouletteRules { MaxSeats = 8, MinBet = 10, MaxBet = 200, TableMax = 600, StartingStack = 400, BetSeconds = 15 });
        var sim = Build(game, 8);
        var violations = 0;
        var wins = 0;
        sim.Players[0].Client.Updated += u => wins += u.Events.Count(e => e.Kind == EventKind.RouletteWin);
        sim.Run(TimeSpan.FromMinutes(20), () =>
        {
            if (sim.Host.Ledger().Sum(e => e.Net) + game.HouseNet + game.ChipsOnTable != 0) violations++;
        });

        var ledger = sim.Host.Ledger();
        output.WriteLine($"spins={game.Round} lines={sim.Net.LinesSent} longest={sim.Net.LongestLine} houseNet={game.HouseNet} buyIns={ledger.Sum(e => e.BuyIns)}");
        Assert.Equal(0, violations);
        Assert.True(game.Round >= 25, $"only {game.Round} spins");
        Assert.True(sim.Net.LongestLine <= 440, $"a line was {sim.Net.LongestLine} chars");
        Assert.All(sim.Players, p => Assert.Equal(ClientStatus.Seated, p.Client.Status));
        Assert.True(wins > 0);
        Assert.True(ledger.Sum(e => e.BuyIns) > 8 * 400, "nobody bought back in");

        var checks = sim.Players.SelectMany(p => p.Client.Fairness).ToList();
        output.WriteLine($"fairness checks: {checks.Count}, ok: {checks.Count(c => c.Ok)}, failed: {checks.Count(c => c.Status == FairStatus.Failed)}");
        Assert.NotEmpty(checks);
        Assert.DoesNotContain(checks, c => c.Status == FairStatus.Failed);
        Assert.True(checks.Count(c => c.Ok) >= checks.Count * 0.8);

        // Pause so the table settles, then every client's last snapshot must agree with the host.
        sim.Host.SetPaused(true);
        sim.Run(TimeSpan.FromSeconds(60));
        Assert.Equal(0, sim.Host.Ledger().Sum(e => e.Net) + game.HouseNet);
        var hostView = game.BuildState(sim.Clock.Now).Roulette!;
        Assert.NotEqual(RoulettePhase.Betting, hostView.Phase);
        foreach (var (c, _) in sim.Players)
        {
            Assert.Equal(hostView.Seats.Sum(s => s.Bankroll), c.Roulette!.Seats.Sum(s => s.Bankroll));
            Assert.Equal(hostView.History, c.Roulette.History);
        }
    }

    [Fact]
    public void RouletteSurvivesLostMessages()
    {
        var game = new RouletteEngine(new RouletteRules { MaxSeats = 4, StartingStack = 1000 });
        var sim = Build(game, 4, dropRate: 0.05, seed: 5);
        sim.Run(TimeSpan.FromMinutes(12));
        output.WriteLine($"spins={game.Round}");
        Assert.True(game.Round >= 12, $"only {game.Round} spins with 5% loss");
        Assert.Equal(0, sim.Host.Ledger().Sum(e => e.Net) + game.HouseNet + game.ChipsOnTable);
        Assert.DoesNotContain(sim.Players.SelectMany(p => p.Client.Fairness), c => c.Status == FairStatus.Failed);
    }

    [Fact]
    public void APlayersBetsGoDownAndArePaid()
    {
        var game = new RouletteEngine(new RouletteRules { MaxSeats = 3, StartingStack = 1000 });
        var sim = Build(game, 0, chat: true);
        var me = new ClientSession(sim.Host.Info(sim.Clock.Now), "Me@World", sim.Clock);
        sim.Net.Add(me);
        me.Join();
        sim.Run(TimeSpan.FromSeconds(10));
        Assert.True(me.IsSeated);
        Assert.Equal(RoulettePhase.Betting, me.Roulette!.Phase);
        Assert.True(double.IsNaN(me.SecondsLeft(sim.Clock.Now)));

        var red = RouletteEngineTests.Red;
        // The client makes the host's checks first, so a bad list never leaves it waiting on the host.
        Assert.NotNull(me.PlaceRouletteBets([((byte)red, 5)]));
        Assert.NotNull(me.PlaceRouletteBets([((byte)red, 600)]));
        Assert.NotNull(me.PlaceRouletteBets(Enumerable.Range(0, 13).Select(n => ((byte)n, 10L)).ToList()));
        Assert.False(me.AwaitingHost);
        Assert.Equal(0, me.QueuedFrames);

        Assert.Null(me.PlaceRouletteBets([((byte)red, 50), (17, 10), (17, 10)])); // the same spot twice counts once, doubled
        Assert.True(me.AwaitingHost);
        sim.Run(TimeSpan.FromSeconds(6));
        Assert.False(me.AwaitingHost);
        Assert.Equal([((byte)red, 50L), ((byte)17, 20L)], me.Roulette.Seat(me.MySeat)!.Bets);
        Assert.Equal(930, game.StackOf(me.MySeat));
        Assert.InRange(me.SecondsLeft(sim.Clock.Now), 1, 20);

        // Change of mind: everything on black.
        var black = RouletteEngineTests.Black;
        me.PlaceRouletteBets([((byte)black, 100)]);
        sim.Run(TimeSpan.FromSeconds(6));
        Assert.Equal([((byte)black, 100L)], me.Roulette.Seat(me.MySeat)!.Bets);
        Assert.Equal(900, game.StackOf(me.MySeat));

        var settled = false;
        me.Updated += u => settled |= u.Current.Roulette?.Phase == RoulettePhase.Settled;
        sim.Run(TimeSpan.FromSeconds(40));
        Assert.True(settled);
        Assert.True(game.Round >= 2);
        var result = game.BuildState(sim.Clock.Now).Roulette!.History[0];
        Assert.Equal(900 + RouletteBets.Payout(black, 100, result), game.StackOf(me.MySeat));
        Assert.Equal(game.HouseNet, -(game.StackOf(me.MySeat) - 1000));
        var check = Assert.Single(me.Fairness);
        Assert.Equal(FairStatus.Verified, check.Status);
        Assert.Equal(GameKind.Roulette, check.Game);
    }

    [Fact]
    public void RebuysWorkAtRoulette()
    {
        var game = new RouletteEngine(new RouletteRules { MaxSeats = 3, StartingStack = 500 });
        var sim = Build(game, 0, chat: false);
        var me = new ClientSession(sim.Host.Info(sim.Clock.Now), "Me@World", sim.Clock);
        sim.Net.Add(me);
        me.Join();
        sim.Run(TimeSpan.FromSeconds(5));
        me.RequestRebuy(); // a full stack can't
        sim.Run(TimeSpan.FromSeconds(1));
        Assert.Empty(sim.Host.RebuyRequests);

        sim.Host.AdjustChips(me.MySeat, -495);
        sim.Run(TimeSpan.FromSeconds(2));
        me.RequestRebuy();
        Assert.True(me.RebuyPending);
        sim.Run(TimeSpan.FromSeconds(5)); // the sim's host approves after 3 s
        Assert.Equal(500, game.StackOf(me.MySeat));
        Assert.False(me.RebuyPending);
    }

    [Fact]
    public void PracticeRouletteDealsTheHumanIn()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        using var practice = new PracticeTable(GameKind.Roulette, 3, "You@Practice", clock, roulette: new RouletteRules { AutoSpin = false });
        void Run(double seconds)
        {
            for (var i = 0; i < seconds * 10; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                practice.Tick(clock.Now);
            }
        }

        Run(5);
        Assert.Equal(ClientStatus.Seated, practice.Me.Status);
        Assert.Equal("Practice roulette", practice.Me.Table.Title);
        Assert.Equal(RoulettePhase.Betting, practice.Me.Roulette!.Phase); // practice tables always spin by themselves
        practice.Me.PlaceRouletteBets([((byte)RouletteEngineTests.Red, 50)]);
        var spun = false;
        practice.Me.Updated += u => spun |= u.Current.Roulette?.Phase == RoulettePhase.Spinning && u.Current.Roulette.Seat(practice.Me.MySeat)?.Bets.Count == 1;
        Run(40);
        Assert.True(spun);
        Assert.Contains(practice.Me.Fairness, f => f.Ok);
    }

    [Fact]
    public void ForgedOrLateRouletteBetsChangeNothing()
    {
        var game = new RouletteEngine(new RouletteRules { MaxSeats = 4, StartingStack = 1000, ProvablyFair = true });
        var sim = Build(game, 3, seed: 4);
        var clock = sim.Clock;
        sim.Run(TimeSpan.FromSeconds(1), () => { });
        var end = clock.Now + TimeSpan.FromSeconds(60);
        while (!(game.Phase == RoulettePhase.Betting && game.ChipsOnTable > 0) && clock.Now < end) sim.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RoulettePhase.Betting, game.Phase);

        var cheater = sim.Players[0].Client;
        var victim = sim.Players.Select(p => p.Client).First(c => c != cheater);
        var red = (byte)RouletteEngineTests.Red;

        // Someone who isn't seated tries to bet, and to clear a seated player's bets.
        var before = game.BuildState(clock.Now).Roulette!;
        var version = game.Version;
        var forged = new RouletteBetMsg { Round = game.Round, Seed = new byte[8], Bets = [] };
        sim.Host.OnFrame("Stranger@World", forged.ToFrame(sim.Host.TableId, 1), clock.Now);
        sim.Host.OnFrame("Stranger@World", new RouletteBetMsg { Round = game.Round, Seed = new byte[8], Bets = [(red, 100)] }.ToFrame(sim.Host.TableId, 2), clock.Now);
        Assert.Equal(version, game.Version);

        // A seated player's message only ever touches their own seat.
        sim.Host.OnFrame(cheater.SelfId, new RouletteBetMsg { Round = game.Round, Seed = new byte[8], Bets = [(red, 100)] }.ToFrame(sim.Host.TableId, 3), clock.Now);
        var after = game.BuildState(clock.Now).Roulette!;
        Assert.Equal([(red, 100L)], after.Seat(cheater.MySeat)!.Bets);
        foreach (var s in before.Seats.Where(s => s.Seat != cheater.MySeat))
        {
            Assert.Equal(s.Bets, after.Seat(s.Seat)!.Bets);
            Assert.Equal(s.Bankroll, after.Seat(s.Seat)!.Bankroll);
        }

        // Once the wheel turns, nobody can add, change or take back a bet.
        game.StartNow(clock.Now);
        Assert.Equal(RoulettePhase.Spinning, game.Phase);
        before = game.BuildState(clock.Now).Roulette!;
        version = game.Version;
        var winner = RouletteBets.Find(1UL << before.Result);
        sim.Host.OnFrame(cheater.SelfId, new RouletteBetMsg { Round = game.Round, Seed = new byte[8], Bets = [((byte)winner, 500)] }.ToFrame(sim.Host.TableId, 4), clock.Now);
        sim.Host.OnFrame(victim.SelfId, new RouletteBetMsg { Round = game.Round, Seed = new byte[8], Bets = [] }.ToFrame(sim.Host.TableId, 5), clock.Now);
        sim.Host.OnFrame(cheater.SelfId, new RouletteBetMsg { Round = game.Round + 1, Seed = new byte[8], Bets = [((byte)winner, 500)] }.ToFrame(sim.Host.TableId, 6), clock.Now);
        Assert.Equal(version, game.Version);
        after = game.BuildState(clock.Now).Roulette!;
        Assert.Equal(before.Result, after.Result);
        Assert.Equal(before.Seats.Select(s => (s.Bankroll, s.TotalBet)), after.Seats.Select(s => (s.Bankroll, s.TotalBet)));
    }
}
