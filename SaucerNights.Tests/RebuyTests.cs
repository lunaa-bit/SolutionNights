using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Tests;

/// <summary>Buying back in: the host approves every rebuy, and leaving can't be used to reset a stack.</summary>
public class RebuyTests
{
    private sealed class Table
    {
        public required ManualClock Clock;
        public required HostSession Host;
        public required LoopbackNetwork Net;
        public required List<ClientSession> Players;

        public void Run(double seconds)
        {
            var end = Clock.Now + TimeSpan.FromSeconds(seconds);
            while (Clock.Now < end)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(100));
                Net.Tick(Clock.Now);
            }
        }

        public long Stack(ClientSession c) => Host.Game.StackOf(c.MySeat);
    }

    private static Table Build(ITableGame game, int players, bool autoApprove = false)
    {
        var clock = new ManualClock(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        var host = new HostSession(game, new HostOptions { HostId = "Dealer@Ultros", HostName = "Dealer", AutoApproveRebuys = autoApprove }, clock);
        var net = new LoopbackNetwork(host, TimeSpan.FromMilliseconds(300));
        var list = new List<ClientSession>();
        for (var i = 0; i < players; i++)
        {
            var c = new ClientSession(host.Info(clock.Now), $"Player{i}@World", clock);
            net.Add(c);
            c.Join();
            list.Add(c);
        }
        var t = new Table { Clock = clock, Host = host, Net = net, Players = list };
        t.Run(3);
        return t;
    }

    private static PokerEngine PausedPoker() => new(new PokerRules { MaxSeats = 4, StartingStack = 1000, HostPlays = false, AutoStart = false });

    [Fact]
    public void RebuyWaitsForTheHost()
    {
        var game = PausedPoker();
        var t = Build(game, 2);
        var p = t.Players[0];
        t.Host.SetPaused(true);
        t.Host.AdjustChips(p.MySeat, -800);
        t.Run(2);
        var boughtIn = t.Host.Ledger().First(e => e.Id == p.SelfId).BuyIns;

        p.RequestRebuy();
        Assert.True(p.RebuyPending); // shown straight away, before the host answers
        t.Run(3);

        Assert.Single(t.Host.RebuyRequests);
        Assert.Equal(800, t.Host.RebuyAmount(p.SelfId));
        Assert.Equal(200, t.Stack(p));
        Assert.True(p.RebuyPending);
        Assert.True(p.Roster.BySeat(p.MySeat)!.Flags.HasFlag(RosterFlags.RebuyPending));
        Assert.False(t.Players[1].RebuyPending);

        // Asking twice doesn't queue twice.
        p.RequestRebuy();
        t.Run(2);
        Assert.Single(t.Host.RebuyRequests);

        t.Host.ApproveRebuy(p.SelfId);
        t.Run(3);
        Assert.Empty(t.Host.RebuyRequests);
        Assert.Equal(1000, t.Stack(p));
        Assert.False(p.RebuyPending);
        Assert.Equal(boughtIn + 800, t.Host.Ledger().First(e => e.Id == p.SelfId).BuyIns);
    }

    [Fact]
    public void HostCanTurnDownARebuy()
    {
        var game = new BlackjackEngine(new BlackjackRules { MaxSeats = 3, StartingStack = 500, MinBet = 10 });
        var t = Build(game, 2);
        var p = t.Players[1];
        t.Host.AdjustChips(p.MySeat, -495);
        t.Run(2);

        var events = new List<EventKind>();
        p.Updated += u => events.AddRange(u.Events.Select(e => e.Kind));
        p.RequestRebuy();
        t.Run(3);
        Assert.True(p.RebuyPending);

        t.Host.DenyRebuy(p.SelfId);
        t.Run(3);
        Assert.False(p.RebuyPending);
        Assert.Empty(t.Host.RebuyRequests);
        Assert.Equal(5, t.Stack(p));
        Assert.Contains(EventKind.RebuyAsked, events);
        Assert.Contains(EventKind.RebuyDenied, events);

        // They can ask again later.
        p.RequestRebuy();
        t.Run(3);
        Assert.Single(t.Host.RebuyRequests);
    }

    [Fact]
    public void FullStackCannotRebuy()
    {
        var t = Build(PausedPoker(), 2);
        t.Players[0].RequestRebuy();
        t.Run(3);
        Assert.Empty(t.Host.RebuyRequests);
        Assert.False(t.Players[0].RebuyPending);
    }

    [Fact]
    public void LeavingAndRejoiningKeepsTheChipsYouLeftWith()
    {
        var game = PausedPoker();
        var t = Build(game, 2);
        var p = t.Players[0];
        t.Host.SetPaused(true);
        t.Host.AdjustChips(p.MySeat, -650);
        t.Run(2);
        p.Leave();
        t.Run(3);
        Assert.Single(t.Host.Players);

        var again = new ClientSession(t.Host.Info(t.Clock.Now), p.SelfId, t.Clock);
        t.Net.Remove(p);
        t.Net.Add(again);
        again.Join();
        t.Run(3);
        Assert.Equal(ClientStatus.Seated, again.Status);
        Assert.Equal(350, t.Stack(again));
        var e = t.Host.Ledger().First(x => x.Id == p.SelfId);
        Assert.Equal(1000, e.BuyIns); // no second buy-in
        Assert.Equal(650, e.CashedOut); // only what the host took away
        Assert.Equal(0, e.Banked);
        Assert.Empty(t.Host.RebuyRequests);
    }

    [Fact]
    public void BustedPlayersWhoComeBackAskForARebuy()
    {
        var game = PausedPoker();
        var t = Build(game, 2);
        var p = t.Players[0];
        t.Host.SetPaused(true);
        t.Host.AdjustChips(p.MySeat, -1000);
        t.Run(2);
        p.Leave();
        t.Run(3);

        var again = new ClientSession(t.Host.Info(t.Clock.Now), p.SelfId, t.Clock);
        t.Net.Remove(p);
        t.Net.Add(again);
        again.Join();
        t.Run(3);
        Assert.Equal(ClientStatus.Seated, again.Status);
        Assert.Equal(0, t.Stack(again));
        Assert.Single(t.Host.RebuyRequests);
        Assert.True(again.RebuyPending);

        t.Host.ApproveRebuy(p.SelfId);
        t.Run(3);
        Assert.Equal(1000, t.Stack(again));
        Assert.Equal(2000, t.Host.Ledger().First(x => x.Id == p.SelfId).BuyIns);
    }

    [Fact]
    public void PracticeTablesApproveBotsAutomatically()
    {
        var game = PausedPoker();
        var t = Build(game, 2, autoApprove: true);
        var p = t.Players[0];
        t.Host.AdjustChips(p.MySeat, -900);
        t.Run(2);
        p.RequestRebuy();
        t.Run(3);
        Assert.Empty(t.Host.RebuyRequests);
        Assert.Equal(1000, t.Stack(p));
    }
}
