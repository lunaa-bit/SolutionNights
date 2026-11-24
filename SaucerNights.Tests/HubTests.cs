using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Bots;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using SaucerNights.Core.Wire;

namespace SaucerNights.Tests;

/// <summary>Several plugin instances (one TableHub each) talking over a fake linkshell, exactly as in game.</summary>
public class HubTests
{
    private sealed class Linkshell
    {
        private readonly List<(DateTime At, string Sender, string Line)> _inFlight = [];
        public readonly List<(TableHub Hub, Link Link)> Members = [];
        public int Lines;
        public int Longest;

        public void Post(string sender, string line, DateTime now)
        {
            Lines++;
            Longest = Math.Max(Longest, line.Length);
            Assert.True(FrameCodec.LooksLikeProtocol(line));
            _inFlight.Add((now + TimeSpan.FromMilliseconds(350), sender, line));
        }

        public void Deliver(DateTime now)
        {
            var due = _inFlight.Where(x => x.At <= now).ToList();
            _inFlight.RemoveAll(x => x.At <= now);
            foreach (var (_, sender, line) in due)
                foreach (var (hub, _) in Members)
                    hub.OnChatLine(sender, line); // every member hears every line, including the sender's echo
        }
    }

    private sealed class Link(Linkshell ls, string id, ManualClock clock) : IChatLink
    {
        public bool Ready => true;

        public string SelfId => id;

        public void Send(string line) => ls.Post(id, line, clock.Now);
    }

    private static (Linkshell Ls, ManualClock Clock, TableHub[] Hubs) World(params string[] ids)
    {
        var clock = new ManualClock(new DateTime(2026, 11, 21, 21, 0, 0, DateTimeKind.Utc));
        var ls = new Linkshell();
        var hubs = ids.Select(id =>
        {
            var link = new Link(ls, id, clock);
            var hub = new TableHub(link, clock);
            ls.Members.Add((hub, link));
            return hub;
        }).ToArray();
        return (ls, clock, hubs);
    }

    private static void Run(Linkshell ls, ManualClock clock, TableHub[] hubs, TimeSpan duration, Func<bool>? until = null, Action? each = null)
    {
        var end = clock.Now + duration;
        while (clock.Now < end)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            foreach (var h in hubs) h.Update();
            ls.Deliver(clock.Now);
            each?.Invoke();
            if (until?.Invoke() == true) return;
        }
        if (until != null) Assert.Fail("Condition not reached in time.");
    }

    [Fact]
    public void FindJoinPlayAndClosePokerWithTheHostPlaying()
    {
        var (ls, clock, hubs) = World("Host Person@Ultros", "Guest One@Gilgamesh", "Guest Two@Balmung");
        var (host, g1, g2) = (hubs[0], hubs[1], hubs[2]);

        host.HostTable(new PokerEngine(new PokerRules { HostPlays = true, ProvablyFair = true, MaxSeats = 6 }),
            new HostOptions { Title = "Friday cards", HostName = "Host Person" });
        Assert.NotNull(host.Table);

        // The guests discover the table.
        g1.Lobby.Refresh(clock.Now);
        Run(ls, clock, hubs, TimeSpan.FromSeconds(20), () => g1.Lobby.Tables(clock.Now).Count == 1 && g2.Lobby.Tables(clock.Now).Count == 1);
        var info = g1.Lobby.Tables(clock.Now)[0];
        Assert.Equal("Friday cards", info.Title);
        Assert.Equal(GameKind.Poker, info.Game);
        Assert.True(info.Flags.HasFlag(TableFlags.ProvablyFair));

        g1.JoinTable(info, spectateOnly: false);
        g2.JoinTable(g2.Lobby.Tables(clock.Now)[0], spectateOnly: false);
        Run(ls, clock, hubs, TimeSpan.FromSeconds(30), () => g1.Table!.IsSeated && g2.Table!.IsSeated && host.Table!.IsSeated);
        Assert.Equal(3, host.Host!.Players.Count);

        // Everyone plays with a bot brain until several hands are done.
        var bots = hubs.Select((_, i) => new PokerBot(40 + i, 0.2, 0.6)).ToArray();
        Run(ls, clock, hubs, TimeSpan.FromMinutes(20), () => host.Host!.Game.Round >= 6, () =>
        {
            for (var i = 0; i < hubs.Length; i++) bots[i].Tick(hubs[i].Table!, clock.Now);
        });
        Assert.True(ls.Longest <= FrameCodec.DefaultMaxLineChars);
        Assert.Equal(0, host.Host!.Ledger().Sum(e => e.Net) - InPlay(host));

        // Remote players verified the shuffles; nobody saw a failure.
        var checks = g1.Table!.Fairness.Concat(g2.Table!.Fairness).ToList();
        Assert.NotEmpty(checks);
        Assert.DoesNotContain(checks, c => c.Status == FairStatus.Failed);

        // A guest leaves, then the host closes the table.
        g2.LeaveTable();
        Run(ls, clock, hubs, TimeSpan.FromSeconds(30), () => host.Host!.Players.Count == 2);
        host.LeaveTable();
        Run(ls, clock, hubs, TimeSpan.FromSeconds(30), () => g1.Table!.Status == ClientStatus.Closed);
        Run(ls, clock, hubs, TimeSpan.FromSeconds(5));
        Assert.False(host.Closing);
        Assert.Empty(g1.Lobby.Tables(clock.Now));
    }

    private static long InPlay(TableHub host)
    {
        // Chips committed to the current hand are on the table, not in anyone's stack.
        var v = host.Host!.Game.BuildState(DateTime.UtcNow).Poker!;
        return -(v.Phase == PokerPhase.Showdown ? 0 : v.Pots.Sum(p => p.Amount) + v.Seats.Sum(s => s.Bet));
    }

    [Fact]
    public void BlackjackHostDealsToRemotePlayersAndWatchers()
    {
        var (ls, clock, hubs) = World("Dealer Dan@Ultros", "Player Pat@Ultros", "Watcher Wes@Ultros");
        var (host, player, watcher) = (hubs[0], hubs[1], hubs[2]);
        host.HostTable(new BlackjackEngine(new BlackjackRules { MaxSeats = 5 }), new HostOptions { Title = "BJ", HostName = "Dealer Dan" });
        Assert.Equal(ClientStatus.Spectating, host.Table!.Status); // the dealer doesn't take a seat

        player.Lobby.Refresh(clock.Now);
        Run(ls, clock, hubs, TimeSpan.FromSeconds(20), () => player.Lobby.Tables(clock.Now).Count == 1 && watcher.Lobby.Tables(clock.Now).Count == 1);
        player.JoinTable(player.Lobby.Tables(clock.Now)[0], false);
        watcher.JoinTable(watcher.Lobby.Tables(clock.Now)[0], true);

        var bot = new BlackjackBot(7, 0.2, 0.6);
        Run(ls, clock, hubs, TimeSpan.FromMinutes(10), () => host.Host!.Game.Round >= 5, () => bot.Tick(player.Table!, clock.Now));

        Assert.Equal(ClientStatus.Spectating, watcher.Table!.Status);
        Assert.NotNull(watcher.Table.Blackjack);
        Assert.Contains(watcher.Table.Roster.Entries, e => e.Name == "Player Pat@Ultros");
        Assert.DoesNotContain(player.Table!.Fairness, f => f.Status == FairStatus.Failed);
        Assert.Contains(player.Table.Fairness, f => f.Ok);
        var ledger = host.Host!.Ledger();
        Assert.Single(ledger);
    }

    [Fact]
    public void OwnEchoesAreIgnored()
    {
        var (ls, clock, hubs) = World("Solo Sam@Ultros");
        var host = hubs[0];
        host.HostTable(new BlackjackEngine(new BlackjackRules()), new HostOptions { Title = "Echo" });
        Run(ls, clock, hubs, TimeSpan.FromSeconds(10));
        // The host heard its own announcement but must not list or join its own table as a stranger.
        Assert.Empty(host.Lobby.Tables(clock.Now));
        Assert.Empty(host.Host!.Players);
    }
}
