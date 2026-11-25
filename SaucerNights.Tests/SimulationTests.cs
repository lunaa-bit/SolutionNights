using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Bots;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using Xunit.Abstractions;

namespace SaucerNights.Tests;

/// <summary>
/// Whole tables of bots playing over a simulated linkshell that uses the real chat path: text encoding, the
/// 1.2-second send pacing, fragmentation and reassembly.
/// </summary>
public class SimulationTests(ITestOutputHelper output)
{
    private sealed class Sim
    {
        public required ManualClock Clock;
        public required HostSession Host;
        public required LoopbackNetwork Net;
        public required List<(ClientSession Client, IBot Bot)> Players;

        /// <summary>Like a real host, approve rebuy requests after a few seconds.</summary>
        public bool HostApprovesRebuys = true;

        public void Run(TimeSpan duration, Action? check = null)
        {
            var end = Clock.Now + duration;
            while (Clock.Now < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(100));
                Net.Tick(Clock.Now);
                foreach (var (c, b) in Players) b.Tick(c, Clock.Now);
                if (HostApprovesRebuys)
                    foreach (var r in Host.RebuyRequests.Where(r => Clock.Now - r.At > TimeSpan.FromSeconds(3)).ToList())
                        Host.ApproveRebuy(r.Id);
                check?.Invoke();
            }
        }
    }

    private static Sim Build(ITableGame game, int players, bool chat, double dropRate = 0, int seed = 1)
    {
        var clock = new ManualClock(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        var host = new HostSession(game, new HostOptions { HostId = "Dealer@Ultros", HostName = "Dealer", Title = "Sim" }, clock);
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
            list.Add((c, game.Kind == GameKind.Blackjack ? new BlackjackBot(seed * 100 + i, 0.3, 1.0) : new PokerBot(seed * 100 + i, 0.3, 1.0)));
        }
        return new Sim { Clock = clock, Host = host, Net = net, Players = list };
    }

    [Fact]
    public void BlackjackTableRunsOverChat()
    {
        var game = new BlackjackEngine(new BlackjackRules { MaxSeats = 5, MinBet = 10, MaxBet = 200, StartingStack = 1000, ProvablyFair = true });
        var sim = Build(game, 5, chat: true);
        var violations = 0;
        sim.Run(TimeSpan.FromMinutes(25), () =>
        {
            if (game.Phase is BjPhase.Settled or BjPhase.Betting)
            {
                var ledger = sim.Host.Ledger();
                if (ledger.Sum(e => e.Net) + game.HouseNet != 0) violations++;
            }
        });

        output.WriteLine($"rounds={game.Round} lines={sim.Net.LinesSent} longest={sim.Net.LongestLine} houseNet={game.HouseNet}");
        Assert.Equal(0, violations);
        Assert.True(game.Round >= 15, $"only {game.Round} rounds were played");
        Assert.True(sim.Net.LongestLine <= 440, $"a line was {sim.Net.LongestLine} chars");
        Assert.All(sim.Players, p => Assert.Equal(ClientStatus.Seated, p.Client.Status));

        var checks = sim.Players.SelectMany(p => p.Client.Fairness).ToList();
        output.WriteLine($"fairness checks: {checks.Count}, failed: {checks.Count(c => c.Status == FairStatus.Failed)}");
        Assert.NotEmpty(checks);
        Assert.DoesNotContain(checks, c => c.Status == FairStatus.Failed);
        Assert.True(checks.Count(c => c.Ok) >= checks.Count * 0.8);

        // Pause so the table settles, then every client's last snapshot must agree with the host.
        sim.Host.SetPaused(true);
        sim.Run(TimeSpan.FromSeconds(60));
        var hostView = game.BuildState(sim.Clock.Now).Blackjack!;
        foreach (var (c, _) in sim.Players)
            Assert.Equal(hostView.Seats.Sum(s => s.Bankroll), c.Blackjack!.Seats.Sum(s => s.Bankroll));
    }

    [Fact]
    public void PokerTableRunsOverChatWithPrivateHoleCards()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 6, SmallBlind = 10, BigBlind = 20, StartingStack = 1000, ProvablyFair = true, HostPlays = false });
        var sim = Build(game, 6, chat: true, seed: 3);
        var violations = 0;
        var holeChecks = 0;
        sim.Run(TimeSpan.FromMinutes(30), () =>
        {
            if (game.Phase is PokerPhase.Showdown or PokerPhase.Waiting)
            {
                if (sim.Host.Ledger().Sum(e => e.Net) != 0) violations++;
            }
            // Each player can read their own cards and nobody else's.
            if (game.Phase == PokerPhase.Flop)
            {
                foreach (var (c, _) in sim.Players)
                {
                    if (c.Poker is not { Phase: PokerPhase.Flop } v || c.HoleHand != v.Hand) continue;
                    var truth = game.PrivateCards().FirstOrDefault(p => p.Seat == c.MySeat).Cards;
                    if (truth != null)
                    {
                        Assert.Equal(truth, c.Hole);
                        holeChecks++;
                    }
                }
            }
        });

        output.WriteLine($"hands={game.Round} lines={sim.Net.LinesSent} longest={sim.Net.LongestLine} holeChecks={holeChecks}");
        Assert.Equal(0, violations);
        Assert.True(game.Round >= 15, $"only {game.Round} hands were played");
        Assert.True(holeChecks > 0);
        Assert.True(sim.Net.LongestLine <= 440);

        var checks = sim.Players.SelectMany(p => p.Client.Fairness).ToList();
        output.WriteLine($"fairness checks: {checks.Count}, ok: {checks.Count(c => c.Ok)}");
        Assert.NotEmpty(checks);
        Assert.DoesNotContain(checks, c => c.Status == FairStatus.Failed);
    }

    [Fact]
    public void PokerSurvivesLostMessages()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000, ProvablyFair = false, HostPlays = false });
        var sim = Build(game, 4, chat: true, dropRate: 0.05, seed: 9);
        sim.Run(TimeSpan.FromMinutes(20));
        output.WriteLine($"hands={game.Round}");
        Assert.True(game.Round >= 8, $"only {game.Round} hands were played with 5% loss");
    }

    [Fact]
    public void BlackjackSurvivesLostMessages()
    {
        var game = new BlackjackEngine(new BlackjackRules { MaxSeats = 4, StartingStack = 1000 });
        var sim = Build(game, 4, chat: true, dropRate: 0.05, seed: 5);
        sim.Run(TimeSpan.FromMinutes(15));
        output.WriteLine($"rounds={game.Round}");
        Assert.True(game.Round >= 8);
    }

    [Fact]
    public void PlayersCanLeaveAndRejoin()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000, HostPlays = false });
        var sim = Build(game, 3, chat: false);
        sim.Run(TimeSpan.FromMinutes(2));
        var leaver = sim.Players[0].Client;
        leaver.Leave();
        sim.Run(TimeSpan.FromSeconds(10));
        Assert.Equal(2, sim.Host.Players.Count);
        var ledger = sim.Host.Ledger();
        var before = ledger.First(e => e.Id == leaver.SelfId);
        Assert.False(before.Seated);
        var boughtIn = before.BuyIns;
        var leftWith = before.Banked;
        var cashedOut = before.CashedOut;
        Assert.True(leftWith > 0);

        sim.Net.Remove(leaver);
        var again = new ClientSession(sim.Host.Info(sim.Clock.Now), leaver.SelfId, sim.Clock);
        sim.Net.Add(again);
        again.Join();
        sim.Run(TimeSpan.FromSeconds(10));
        Assert.Equal(ClientStatus.Seated, again.Status);
        Assert.Equal(3, sim.Host.Players.Count);
        // Coming back is not a fresh buy-in: the chips they left with follow them.
        var after = sim.Host.Ledger().First(e => e.Id == leaver.SelfId);
        Assert.Equal(boughtIn, after.BuyIns);
        Assert.Equal(cashedOut - leftWith, after.CashedOut);
        Assert.Equal(0, after.Banked);
    }

    [Fact]
    public void FullTableRejectsExtraPlayers()
    {
        var game = new BlackjackEngine(new BlackjackRules { MaxSeats = 2 });
        var sim = Build(game, 2, chat: false);
        var extra = new ClientSession(sim.Host.Info(sim.Clock.Now), "Late@World", sim.Clock);
        sim.Net.Add(extra);
        extra.Join();
        sim.Run(TimeSpan.FromSeconds(5));
        Assert.Equal(ClientStatus.Rejected, extra.Status);
        Assert.Equal(RejectReason.Full, extra.RejectedFor);
    }

    [Fact]
    public void SpectatorsSeeTheTableButNoHoleCards()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000, HostPlays = false });
        var sim = Build(game, 3, chat: true);
        var watcher = new ClientSession(sim.Host.Info(sim.Clock.Now), "Watcher@World", sim.Clock);
        sim.Net.Add(watcher);
        watcher.RequestResync(ResyncNeed.Roster | ResyncNeed.State);
        sim.Run(TimeSpan.FromMinutes(2));
        Assert.Equal(ClientStatus.Spectating, watcher.Status);
        Assert.NotNull(watcher.Poker);
        Assert.Equal(3, watcher.Roster.Entries.Count);
        Assert.Empty(watcher.Hole);
    }

    [Fact]
    public void PracticeTableDealsForTheHuman()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        using var practice = new PracticeTable(GameKind.Blackjack, 3, "You@Practice", clock);
        var dealtToMe = false;
        practice.Me.Updated += u =>
        {
            if (u.Current.Blackjack?.Seat(practice.Me.MySeat) is { Hands.Count: > 0 } me && me.Hands[0].Cards.Count >= 2) dealtToMe = true;
        };
        for (var i = 0; i < 100; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            practice.Tick(clock.Now);
        }
        Assert.Equal(ClientStatus.Seated, practice.Me.Status);
        Assert.Equal(BjPhase.Betting, practice.Me.Blackjack!.Phase);
        practice.Me.PlaceBet(50);
        for (var i = 0; i < 80 && !dealtToMe; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            practice.Tick(clock.Now);
        }
        Assert.True(dealtToMe);
    }
}
