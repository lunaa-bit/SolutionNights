using System.Reflection;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Bots;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Crypto;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Tests;

/// <summary>
/// The plugin's source is public, so these tests play the cheater with a full copy of the code: sitting at the table
/// and reading every line on the linkshell, or posting forged messages. Hidden cards must stay hidden and forged
/// messages must change nothing.
/// </summary>
public class CheatingTests
{
    private static (ManualClock Clock, HostSession Host, LoopbackNetwork Net, List<(ClientSession Client, IBot Bot)> Players) Table(ITableGame game, int players)
    {
        var clock = new ManualClock(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc));
        var host = new HostSession(game, new HostOptions { HostId = "Dealer@Ultros", HostName = "Dealer" }, clock);
        var net = new LoopbackNetwork(host, TimeSpan.FromMilliseconds(300), chatEncoding: true, rateLimit: TimeSpan.FromMilliseconds(1200), seed: 4);
        var list = new List<(ClientSession, IBot)>();
        for (var i = 0; i < players; i++)
        {
            var c = new ClientSession(host.Info(clock.Now), $"Player{i}@World", clock);
            net.Add(c);
            c.Join();
            list.Add((c, game.Kind == GameKind.Blackjack ? new BlackjackBot(40 + i, 0.3, 1.0) : new PokerBot(40 + i, 0.3, 1.0)));
        }
        return (clock, host, net, list);
    }

    private static void Run((ManualClock Clock, HostSession Host, LoopbackNetwork Net, List<(ClientSession Client, IBot Bot)> Players) t, TimeSpan duration, Action? each = null)
    {
        var end = t.Clock.Now + duration;
        while (t.Clock.Now < end)
        {
            t.Clock.Advance(TimeSpan.FromMilliseconds(100));
            t.Net.Tick(t.Clock.Now);
            foreach (var (c, b) in t.Players) b.Tick(c, t.Clock.Now);
            foreach (var r in t.Host.RebuyRequests.ToList()) t.Host.ApproveRebuy(r.Id);
            each?.Invoke();
        }
    }

    private static byte[]? KeyOf(ClientSession c) =>
        (byte[]?)typeof(ClientSession).GetField("_sharedKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c);

    [Fact]
    public void APlayerWithTheSourceCannotReadOtherHoleCards()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 5, SmallBlind = 10, BigBlind = 20, StartingStack = 1000 });
        var t = Table(game, 5);
        var cheater = t.Players[0].Client;
        var attempts = 0;
        var recovered = 0;
        var clearLeaks = 0;
        cheater.Updated += u =>
        {
            if (u.Current.Poker is not { } v || KeyOf(cheater) is not { } key) return;
            var truth = game.PrivateCards().ToDictionary(p => p.Seat, p => p.Cards);
            // Try the cheater's own key on everyone else's sealed cards.
            foreach (var hole in v.Holes.Where(h => h.Seat != cheater.MySeat))
            {
                attempts++;
                if (CardSeal.TryOpen(key, cheater.Table.TableId, v.Hand, hole.Seat, hole.Data, out var cards)
                    && truth.TryGetValue(hole.Seat, out var real) && cards.SequenceEqual(real.Select(c => c.Code)))
                    recovered++;
            }
            // Before the hand is over, no hole card travels in the clear.
            if (v.Phase != PokerPhase.Showdown && v.Seats.Any(s => s.Shown.Count > 0)) clearLeaks++;
        };
        Run(t, TimeSpan.FromMinutes(8));

        Assert.True(game.Round >= 5, $"only {game.Round} hands");
        Assert.True(attempts > 50, $"only {attempts} attempts");
        Assert.Equal(0, recovered);
        Assert.Equal(0, clearLeaks);
    }

    [Fact]
    public void AListenerWhoIsNotSeatedGetsNoKeyAtAll()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000 });
        var t = Table(game, 3);
        var spy = new ClientSession(t.Host.Info(t.Clock.Now), "Lurker@World", t.Clock);
        t.Net.Add(spy);
        spy.RequestResync(ResyncNeed.Roster | ResyncNeed.State);
        var sawSealed = false;
        spy.Updated += u => sawSealed |= u.Current.Poker?.Holes.Count > 0;
        Run(t, TimeSpan.FromMinutes(3));
        Assert.True(sawSealed);
        Assert.Null(KeyOf(spy));
        Assert.Empty(spy.Hole);
    }

    [Fact]
    public void TheDealersHoleCardStaysHiddenUntilTheDealerPlays()
    {
        var game = new BlackjackEngine(new BlackjackRules { MaxSeats = 4, StartingStack = 2000, MinBet = 10, MaxBet = 200 });
        var t = Table(game, 4);
        var watcher = t.Players[0].Client;
        var checkedStates = 0;
        var leaks = 0;
        watcher.Updated += u =>
        {
            if (u.Current.Blackjack is not { Phase: BjPhase.Insurance or BjPhase.PlayerTurns } v || v.Dealer.Count != 2) return;
            checkedStates++;
            if (!v.Dealer[1].IsHidden) leaks++;
        };
        Run(t, TimeSpan.FromMinutes(6));
        Assert.True(checkedStates > 10, $"only {checkedStates} states");
        Assert.Equal(0, leaks);
    }

    [Fact]
    public void ForgedHostMessagesAreIgnored()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000 });
        var t = Table(game, 3);
        Run(t, TimeSpan.FromSeconds(20));
        var victim = t.Players[1].Client;
        var before = victim.State;
        var roster = victim.Roster;
        Assert.NotNull(before);

        // A cheater posts a "host" snapshot and roster for the same table under their own name.
        var fake = t.Host.BuildState(t.Clock.Now, []);
        fake.Poker!.Seats[0].Stack = 999_999;
        victim.OnFrame("Cheater@World", fake.ToFrame(victim.Table.TableId, 9999), t.Clock.Now);
        victim.OnFrame("Cheater@World", new RosterMsg { Version = 200, Entries = [new RosterEntry(0, "Cheater@World", RosterFlags.Host)] }.ToFrame(victim.Table.TableId, 10000), t.Clock.Now);
        victim.OnFrame("Cheater@World", new ClosedMsg { Reason = CloseReason.HostClosed, Text = "bye" }.ToFrame(victim.Table.TableId, 10001), t.Clock.Now);

        Assert.Same(before, victim.State);
        Assert.Same(roster, victim.Roster);
        Assert.Equal(ClientStatus.Seated, victim.Status);
    }

    [Fact]
    public void PlayersCannotActForOthersOrOutOfTurn()
    {
        var game = new PokerEngine(new PokerRules { MaxSeats = 4, StartingStack = 1000, AutoStart = true });
        var t = Table(game, 3);
        Run(t, TimeSpan.FromSeconds(1));
        // Stop the bots and wait for a betting round.
        var clock = t.Clock;
        var end = clock.Now + TimeSpan.FromSeconds(60);
        while (!game.InRound && clock.Now < end)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            t.Net.Tick(clock.Now);
        }
        var v = game.BuildState(clock.Now).Poker!;
        Assert.True(v.BettingRound);
        var stacks = Enumerable.Range(0, 4).Select(game.StackOf).ToArray();
        var turn = v.TurnId;

        // Someone who isn't seated, and a seated player whose turn it isn't, both try to move.
        var idle = t.Players.Select(p => p.Client).First(c => c.MySeat != v.ActiveSeat);
        var move = new ActionMsg { Round = v.Hand, TurnId = v.TurnId, Kind = (byte)PokerActionKind.Fold };
        t.Host.OnFrame("Stranger@World", move.ToFrame(t.Host.TableId, 1), clock.Now);
        t.Host.OnFrame(idle.SelfId, move.ToFrame(t.Host.TableId, 2), clock.Now);
        t.Host.OnFrame(idle.SelfId, new ActionMsg { Round = v.Hand, TurnId = v.TurnId, Kind = (byte)PokerActionKind.BetOrRaise, Amount = 900 }.ToFrame(t.Host.TableId, 3), clock.Now);

        var after = game.BuildState(clock.Now).Poker!;
        Assert.Equal(turn, after.TurnId);
        Assert.Equal(v.ActiveSeat, after.ActiveSeat);
        Assert.Equal(stacks, Enumerable.Range(0, 4).Select(game.StackOf).ToArray());
        Assert.All(after.Seats, s => Assert.NotEqual(PokerSeatStatus.Folded, s.Status));
    }
}
