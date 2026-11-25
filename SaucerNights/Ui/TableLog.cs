using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

public enum LogTone
{
    Normal,
    Muted,
    Good,
    Bad,
    Gold,
    Header,
}

public sealed record LogLine(DateTime At, string Text, LogTone Tone);

/// <summary>Turns table events into readable lines. Cards are embedded as {c:NN} and drawn as mini cards.</summary>
public sealed partial class TableLog
{
    private ClientSession? _session;

    public List<LogLine> Lines { get; } = [];

    public void Attach(ClientSession? s)
    {
        if (_session == s) return;
        if (_session != null) _session.Updated -= OnUpdate;
        _session = s;
        Lines.Clear();
        if (s != null) s.Updated += OnUpdate;
    }

    private void Add(DateTime at, string text, LogTone tone)
    {
        Lines.Add(new LogLine(at, text, tone));
        if (Lines.Count > 300) Lines.RemoveRange(0, Lines.Count - 300);
    }

    private void OnUpdate(StateUpdate u)
    {
        var s = _session!;
        // Names resolve now if the roster knows the seat; otherwise later, when the roster catches up.
        string N(int seat) => s.Roster.BySeat(seat) is { } e ? SeatArt.ShortName(e.Name) : $"{{s:{seat}}}";
        static string C(Card c) => $"{{c:{c.Code}}}";
        var board = u.Current.Poker?.Board ?? [];
        foreach (var e in u.Events)
        {
            var who = e.Seat == TableEvent.NoSeat ? "" : N(e.Seat);
            var amount = e.Amount.ToString("N0");
            (string Text, LogTone Tone)? line = e.Kind switch
            {
                EventKind.Joined => ($"{who} sat down with {amount}", LogTone.Muted),
                EventKind.Left => ($"{who} left the table with {amount}", LogTone.Muted),
                EventKind.Kicked => ($"{who} was removed by the host", LogTone.Bad),
                EventKind.SatOut => ($"{who} is sitting out", LogTone.Muted),
                EventKind.SatIn => ($"{who} is back", LogTone.Muted),
                EventKind.Rebuy => ($"{who} bought back in for {amount}", LogTone.Good),
                EventKind.RebuyAsked => ($"{who} asked the host for a rebuy ({amount})", LogTone.Muted),
                EventKind.RebuyDenied => ($"The host turned down {who}'s rebuy", LogTone.Bad),
                EventKind.ChipsAdjusted => (e.Extra == 1 ? $"The host gave {who} {amount} chips" : $"The host took {amount} chips from {who}", LogTone.Muted),
                EventKind.Paused => ("The host paused the table", LogTone.Header),
                EventKind.Resumed => ("The host resumed the table", LogTone.Header),
                EventKind.Timeout => ($"{who} ran out of time", LogTone.Bad),

                EventKind.BettingOpen => ($"{(s.Table.Game == GameKind.Roulette ? "Spin" : "Round")} {amount}", LogTone.Header),
                EventKind.BetPlaced => ($"{who} bets {amount}", LogTone.Normal),
                EventKind.Hit => ($"{who} hits {C(new Card(e.Extra))} ({e.Amount})", LogTone.Normal),
                EventKind.Stand => ($"{who} stands on {e.Amount}", LogTone.Normal),
                EventKind.Double => ($"{who} doubles down {C(new Card(e.Extra))}", LogTone.Normal),
                EventKind.Split => ($"{who} splits", LogTone.Normal),
                EventKind.Surrender => ($"{who} surrenders", LogTone.Muted),
                EventKind.InsuranceTaken => ($"{who} takes insurance ({amount})", LogTone.Normal),
                EventKind.InsuranceDeclined => null,
                EventKind.Bust => ($"{who} busts with {e.Amount}", LogTone.Bad),
                EventKind.Blackjack => ($"{who} has blackjack!", LogTone.Gold),
                EventKind.DealerBlackjack => ("Dealer has blackjack", LogTone.Bad),
                EventKind.DealerBust => ($"Dealer busts with {e.Amount}", LogTone.Good),
                EventKind.DealerStands => ($"Dealer stands on {e.Amount}", LogTone.Normal),
                EventKind.Win => (e.Extra == (byte)BjOutcome.Blackjack ? $"{who} is paid {amount} for blackjack" : $"{who} wins, paid {amount}", LogTone.Good),
                EventKind.Lose => ($"{who} loses {amount}", LogTone.Bad),
                EventKind.Push => ($"{who} pushes", LogTone.Muted),
                EventKind.InsurancePaid => ($"{who}'s insurance pays {amount}", LogTone.Good),

                EventKind.HandStarted => ($"Hand #{amount}", LogTone.Header),
                EventKind.PostSmallBlind => ($"{who} posts the small blind ({amount})", LogTone.Muted),
                EventKind.PostBigBlind => ($"{who} posts the big blind ({amount})", LogTone.Muted),
                EventKind.PostAnte => null,
                EventKind.Fold => ($"{who} folds", LogTone.Muted),
                EventKind.Check => ($"{who} checks", LogTone.Normal),
                EventKind.Call => ($"{who} calls {amount}", LogTone.Normal),
                EventKind.Bet => ($"{who} bets {amount}", LogTone.Normal),
                EventKind.Raise => ($"{who} raises to {amount}", LogTone.Normal),
                EventKind.AllIn => ($"{who} is all-in for {amount}", LogTone.Gold),
                EventKind.Flop => (board.Count >= 3 ? $"Flop  {C(board[0])} {C(board[1])} {C(board[2])}" : "Flop", LogTone.Header),
                EventKind.Turn => (board.Count >= 4 ? $"Turn  {C(board[3])}" : "Turn", LogTone.Header),
                EventKind.River => (board.Count >= 5 ? $"River  {C(board[4])}" : "River", LogTone.Header),
                EventKind.Showdown => ("Showdown", LogTone.Header),
                EventKind.WinPot => (e.Extra == 255 ? $"{who} wins {amount}" : $"{who} wins {amount} with {HandEvaluator.CategoryName((HandCategory)e.Extra).ToLowerInvariant()}", LogTone.Gold),
                EventKind.ShowCards => ($"{who} shows {C(new Card((byte)(e.Amount & 0xFF)))} {C(new Card((byte)((e.Amount >> 8) & 0xFF)))}", LogTone.Normal),
                EventKind.UncalledReturned => ($"{amount} returned to {who}", LogTone.Muted),
                EventKind.RouletteBets => (e.Amount > 0 ? $"{who} has {amount} on {e.Extra} spot{(e.Extra == 1 ? "" : "s")}" : $"{who} takes their bets back", LogTone.Normal),
                EventKind.NoMoreBets => ("No more bets", LogTone.Header),
                EventKind.RouletteResult => ($"{e.Amount} {(e.Amount == 0 ? "green" : RouletteArt.IsRed((int)e.Amount) ? "red" : "black")}", LogTone.Header),
                EventKind.RouletteWin => ($"{who} collects {amount}", LogTone.Good),
                EventKind.RouletteLose => ($"{who} loses {amount}", LogTone.Bad),
                _ => null,
            };
            if (line is { } l) Add(u.At, l.Text, l.Tone);
        }
    }

    [GeneratedRegex(@"\{c:(\d+)\}")]
    private static partial Regex CardToken();

    [GeneratedRegex(@"\{s:(\d+)\}")]
    private static partial Regex SeatToken();

    /// <summary>Fills in names that weren't known when the line was written.</summary>
    public string Resolve(string text) =>
        _session == null || !text.Contains("{s:") ? text : SeatToken().Replace(text, m => SeatArt.ShortName(_session.NameAt(int.Parse(m.Groups[1].Value))));

    /// <summary>Draws one line, wrapping, with inline mini cards. Returns the height used.</summary>
    public static void DrawLine(LogLine line, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var fs = ImGui.GetFontSize();
        var col = line.Tone switch
        {
            LogTone.Muted => Pal.Muted,
            LogTone.Good => Pal.Green,
            LogTone.Bad => Pal.Mix(Pal.Red, Pal.Text, 0.25f),
            LogTone.Gold => Pal.GoldLight,
            LogTone.Header => Pal.Accent,
            _ => Pal.Text,
        };
        if (line.Tone == LogTone.Header)
        {
            var ts = Gfx.TextSize(FontRole.Body, fs * 0.85f, line.Text.ToUpperInvariant());
            var y = p.Y + fs * 0.75f;
            var rich = CardToken().IsMatch(line.Text);
            if (!rich)
            {
                dl.AddLine(new Vector2(p.X, y), new Vector2(p.X + (width - ts.X) / 2 - UiEnv.S(8), y), Pal.U(Pal.Accent, 0.3f));
                dl.AddLine(new Vector2(p.X + (width + ts.X) / 2 + UiEnv.S(8), y), new Vector2(p.X + width, y), Pal.U(Pal.Accent, 0.3f));
                dl.AddText(UiEnv.Font(FontRole.Body, fs * 0.85f), fs * 0.85f, new Vector2(p.X + (width - ts.X) / 2, y - ts.Y / 2), Pal.U(col), line.Text.ToUpperInvariant());
                ImGui.Dummy(new Vector2(width, fs * 1.5f));
                return;
            }
        }

        // A small marker for lines that matter, then words and cards laid out with wrapping.
        var indent = UiEnv.S(14);
        var lineH = fs * 1.35f;
        if (line.Tone is LogTone.Good or LogTone.Bad or LogTone.Gold || line.Tone == LogTone.Header)
            W.Diamond(dl, p + new Vector2(UiEnv.S(4), lineH / 2), UiEnv.S(3.5f), col);
        else
            dl.AddCircleFilled(p + new Vector2(UiEnv.S(4), lineH / 2), UiEnv.S(1.5f), Pal.U(Pal.Faint), 8);
        p.X += indent;
        width -= indent;
        var x = 0f;
        var row = 0;
        var pos = 0;
        foreach (Match m in CardToken().Matches(line.Text))
        {
            Words(line.Text[pos..m.Index]);
            var card = new Card(byte.Parse(m.Groups[1].Value));
            var cw = fs * 1.9f;
            if (x + cw > width && x > 0)
            {
                row++;
                x = 0;
            }
            MiniCard(dl, p + new Vector2(x, row * lineH), card, fs);
            x += cw + UiEnv.S(3);
            pos = m.Index + m.Length;
        }
        Words(line.Text[pos..]);
        ImGui.Dummy(new Vector2(width, (row + 1) * lineH));

        void Words(string text)
        {
            foreach (var word in Regex.Split(text, @"(?<= )"))
            {
                if (word.Length == 0) continue;
                var ww = Gfx.TextSize(FontRole.Body, fs, word).X;
                if (x + ww > width && x > 0)
                {
                    row++;
                    x = 0;
                }
                dl.AddText(p + new Vector2(x, row * lineH + (lineH - fs) / 2 - UiEnv.S(1)), Pal.U(col), word);
                x += ww;
            }
        }
    }

    public static void MiniCard(ImDrawListPtr dl, Vector2 p, Card card, float fs)
    {
        var size = new Vector2(fs * 1.9f, fs * 1.3f);
        var min = p + new Vector2(0, (fs * 1.35f - size.Y) / 2);
        dl.AddRectFilled(min, min + size, Pal.U(Pal.Hex(0xF4F7FF)), UiEnv.S(2));
        var ink = CardArt.Ink(card.Suit);
        var rank = card.Rank == 10 ? "10" : card.RankText;
        var ts = Gfx.TextSize(FontRole.Body, fs * 0.9f, rank);
        dl.AddText(UiEnv.Font(FontRole.Body, fs * 0.9f), fs * 0.9f, min + new Vector2(UiEnv.S(3), (size.Y - ts.Y) / 2), Pal.U(ink), rank);
        Suits.Draw(dl, card.Suit, min + new Vector2(size.X - fs * 0.5f, size.Y / 2), fs * 0.7f, ink);
    }
}
