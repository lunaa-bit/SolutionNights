using System.Numerics;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Roulette;

/// <summary>House rules for a single-zero (European) roulette table. Sent to players in the lobby announcement.</summary>
public sealed class RouletteRules
{
    /// <summary>How long the wheel spins before the result is settled. Fixed, so it is not sent.</summary>
    public const int SpinSeconds = 7;

    /// <summary>Least on any one bet spot.</summary>
    public long MinBet = 10;

    /// <summary>Most on any one bet spot.</summary>
    public long MaxBet = 500;

    /// <summary>Most one player may have on the layout in one spin.</summary>
    public long TableMax = 2000;

    public long StartingStack = 1000;
    public byte MaxSeats = 6;

    /// <summary>Betting window, counted from the first bet of the spin.</summary>
    public byte BetSeconds = 20;

    public bool ProvablyFair = true;

    /// <summary>Betting reopens by itself after each spin; otherwise the host opens it.</summary>
    public bool AutoSpin = true;

    public RouletteRules Normalized()
    {
        var r = (RouletteRules)MemberwiseClone();
        r.MaxSeats = (byte)Math.Clamp((int)r.MaxSeats, 1, 8);
        r.BetSeconds = (byte)Math.Clamp((int)r.BetSeconds, 5, 60);
        r.MinBet = Math.Clamp(r.MinBet, 1, 1_000_000_000);
        r.MaxBet = Math.Clamp(r.MaxBet, r.MinBet, 10_000_000_000);
        r.TableMax = Math.Clamp(r.TableMax, r.MaxBet, 100_000_000_000);
        r.StartingStack = Math.Clamp(r.StartingStack, r.MinBet, 100_000_000_000);
        return r;
    }

    public string StakesText => $"{MinBet:N0} - {MaxBet:N0}";

    /// <summary>
    /// Why the host would turn this bet list down, or null if it would take it. <paramref name="available"/> is the
    /// player's chips including any already on the layout this spin (bets replace each other).
    /// </summary>
    public string? BetProblem(IReadOnlyList<(byte Index, long Amount)> bets, long available)
    {
        if (bets.Count > RouletteBets.MaxSpots) return $"At most {RouletteBets.MaxSpots} spots a spin.";
        long total = 0;
        var seen = new HashSet<byte>();
        foreach (var (index, amount) in bets)
        {
            if (!RouletteBets.IsValid(index)) return "That isn't a bet on this table.";
            if (!seen.Add(index)) return "The same spot is listed twice.";
            if (amount < MinBet) return $"The least on a spot is {MinBet:N0}.";
            if (amount > MaxBet) return $"The most on a spot is {MaxBet:N0}.";
            total += amount;
        }
        if (total > TableMax) return $"The most you can have down in one spin is {TableMax:N0}.";
        if (total > available) return "You don't have enough chips.";
        return null;
    }

    public byte[] ToBytes()
    {
        var w = new ByteWriter();
        w.U8((ProvablyFair ? 1 : 0) | (AutoSpin ? 2 : 0));
        w.U8(MaxSeats);
        w.U8(BetSeconds);
        w.VarU(MinBet);
        w.VarU(MaxBet);
        w.VarU(TableMax);
        w.VarU(StartingStack);
        return w.ToArray();
    }

    public static RouletteRules FromBytes(byte[] bytes)
    {
        var r = new ByteReader(bytes);
        var f = r.U8();
        var rules = new RouletteRules
        {
            ProvablyFair = (f & 1) != 0,
            AutoSpin = (f & 2) != 0,
            MaxSeats = r.U8(),
            BetSeconds = r.U8(),
            MinBet = r.VarL(),
            MaxBet = r.VarL(),
            TableMax = r.VarL(),
            StartingStack = r.VarL(),
        };
        return rules.Normalized();
    }
}

public enum RoulettePhase : byte
{
    Waiting = 0,
    Betting = 1,
    Spinning = 2,
    Settled = 3,
}

public enum RouletteSeatStatus : byte
{
    Empty = 0,
    Active = 1,
    SittingOut = 2,
}

public enum RouletteBetKind : byte
{
    Straight = 0,
    Split = 1,
    Street = 2,
    Trio = 3,
    Corner = 4,
    FirstFour = 5,
    SixLine = 6,
    Dozen = 7,
    Column = 8,
    Red = 9,
    Black = 10,
    Odd = 11,
    Even = 12,
    Low = 13,
    High = 14,
}

/// <summary>One place to put chips on the layout. <see cref="Mask"/> has bit n set for each number n it covers.</summary>
public sealed record RouletteSpot(byte Index, RouletteBetKind Kind, ulong Mask, string Name)
{
    /// <summary>How many numbers the bet covers.</summary>
    public int Count => BitOperations.PopCount(Mask);

    /// <summary>Winnings per chip: 35 on a single number down to 1 on the even-money bets.</summary>
    public int Multiplier => 36 / Count - 1;

    public bool Covers(int number) => number is >= 0 and < RouletteBets.Pockets && (Mask & (1UL << number)) != 0;

    public IEnumerable<int> Numbers => Enumerable.Range(0, RouletteBets.Pockets).Where(Covers);
}

/// <summary>
/// The single-zero wheel and every legal bet. The layout has 3 rows of 12 columns: column c (0-based) holds 3c+1 at the
/// bottom, 3c+2 and 3c+3 at the top. A bet travels as its index into <see cref="All"/>, so the order is part of the
/// protocol: straights first (index n is a bet on n), then splits, streets, trios, corners, first four, six lines,
/// dozens, columns, red, black, odd, even, 1-18 and 19-36.
/// </summary>
public static class RouletteBets
{
    /// <summary>Numbers on the wheel, 0 to 36.</summary>
    public const int Pockets = 37;

    /// <summary>Most bet spots one player may cover in one spin.</summary>
    public const int MaxSpots = 12;

    /// <summary>"No result yet" in <see cref="RouletteView.Result"/>.</summary>
    public const byte NoResult = 255;

    /// <summary>Numbers clockwise around a European wheel, starting at 0.</summary>
    public static readonly IReadOnlyList<int> WheelOrder =
        [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];

    private static readonly ulong RedMask = MaskOf(1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36);

    public static readonly IReadOnlyList<RouletteSpot> All = Build();

    private static readonly Dictionary<ulong, int> ByMask = All.ToDictionary(s => s.Mask, s => (int)s.Index);

    public static bool IsRed(int n) => n is >= 1 and <= 36 && (RedMask & (1UL << n)) != 0;

    public static bool IsBlack(int n) => n is >= 1 and <= 36 && !IsRed(n);

    public static ulong MaskOf(params int[] numbers) => MaskOf((IEnumerable<int>)numbers);

    public static ulong MaskOf(IEnumerable<int> numbers)
    {
        ulong m = 0;
        foreach (var n in numbers)
            if (n is >= 0 and < Pockets) m |= 1UL << n;
        return m;
    }

    /// <summary>The bet covering exactly these numbers, or -1 if there is no such bet.</summary>
    public static int Find(ulong mask) => ByMask.GetValueOrDefault(mask, -1);

    public static bool IsValid(int index) => index >= 0 && index < All.Count;

    public static bool Covers(int index, int number) => IsValid(index) && All[index].Covers(number);

    public static int Multiplier(int index) => IsValid(index) ? All[index].Multiplier : 0;

    /// <summary>Chips a bet returns when the ball lands on <paramref name="result"/>: stake plus winnings, or 0.</summary>
    public static long Payout(int index, long amount, int result) => Covers(index, result) ? amount + amount * Multiplier(index) : 0;

    private static List<RouletteSpot> Build()
    {
        var list = new List<RouletteSpot>();
        void Add(RouletteBetKind kind, string name, params int[] numbers) => list.Add(new RouletteSpot((byte)list.Count, kind, MaskOf(numbers), name));

        for (var n = 0; n <= 36; n++) Add(RouletteBetKind.Straight, n.ToString(), n);

        for (var n = 1; n <= 3; n++) Add(RouletteBetKind.Split, $"Split 0/{n}", 0, n);
        for (var n = 1; n <= 36; n++)
        {
            if (n % 3 != 0) Add(RouletteBetKind.Split, $"Split {n}/{n + 1}", n, n + 1);
            if (n <= 33) Add(RouletteBetKind.Split, $"Split {n}/{n + 3}", n, n + 3);
        }

        for (var c = 0; c < 12; c++) Add(RouletteBetKind.Street, $"Street {3 * c + 1}-{3 * c + 3}", 3 * c + 1, 3 * c + 2, 3 * c + 3);

        Add(RouletteBetKind.Trio, "0/1/2", 0, 1, 2);
        Add(RouletteBetKind.Trio, "0/2/3", 0, 2, 3);

        for (var c = 0; c < 11; c++)
        {
            for (var r = 1; r <= 2; r++)
            {
                var n = 3 * c + r;
                Add(RouletteBetKind.Corner, $"Corner {n}/{n + 1}/{n + 3}/{n + 4}", n, n + 1, n + 3, n + 4);
            }
        }

        Add(RouletteBetKind.FirstFour, "0/1/2/3", 0, 1, 2, 3);

        for (var c = 0; c < 11; c++)
            Add(RouletteBetKind.SixLine, $"Six line {3 * c + 1}-{3 * c + 6}", Enumerable.Range(3 * c + 1, 6).ToArray());

        string[] dozens = ["1st 12", "2nd 12", "3rd 12"];
        for (var d = 0; d < 3; d++) Add(RouletteBetKind.Dozen, dozens[d], Enumerable.Range(12 * d + 1, 12).ToArray());

        for (var col = 1; col <= 3; col++)
            Add(RouletteBetKind.Column, $"Column {col}", Enumerable.Range(0, 12).Select(c => 3 * c + col).ToArray());

        var numbers = Enumerable.Range(1, 36).ToArray();
        Add(RouletteBetKind.Red, "Red", numbers.Where(IsRed).ToArray());
        Add(RouletteBetKind.Black, "Black", numbers.Where(IsBlack).ToArray());
        Add(RouletteBetKind.Odd, "Odd", numbers.Where(n => n % 2 == 1).ToArray());
        Add(RouletteBetKind.Even, "Even", numbers.Where(n => n % 2 == 0).ToArray());
        Add(RouletteBetKind.Low, "1-18", numbers.Where(n => n <= 18).ToArray());
        Add(RouletteBetKind.High, "19-36", numbers.Where(n => n >= 19).ToArray());
        return list;
    }
}

public sealed class RouletteSeatView
{
    public byte Seat;
    public RouletteSeatStatus Status;

    /// <summary>Chips not on the layout.</summary>
    public long Bankroll;

    /// <summary>This spin's bets: index into <see cref="RouletteBets.All"/> and chips. Kept on show until the next spin opens.</summary>
    public List<(byte Index, long Amount)> Bets = [];

    /// <summary>The last spin's result for this seat: minus the stakes while the wheel turns, then plus what came back.</summary>
    public long RoundNet;

    public long TotalBet => Bets.Sum(b => b.Amount);
}

/// <summary>Public snapshot of a roulette table.</summary>
public sealed class RouletteView
{
    public RoulettePhase Phase;
    public long Round;

    /// <summary>Betting: seconds until betting closes (255 until the first bet). Spinning: seconds until the ball lands.</summary>
    public byte DeadlineSec = 255;

    /// <summary>The winning number, 0–36, from the moment the wheel starts; <see cref="RouletteBets.NoResult"/> otherwise.</summary>
    public byte Result = RouletteBets.NoResult;

    /// <summary>Recent results, newest last.</summary>
    public List<byte> History = [];

    public List<RouletteSeatView> Seats = [];
    public byte[]? Commit;
    public FairReveal? Reveal;

    public RouletteSeatView? Seat(int seat) => Seats.FirstOrDefault(s => s.Seat == seat);

    public bool HasResult => Result < RouletteBets.Pockets;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Phase);
        w.VarU(Round);
        w.U8(DeadlineSec);
        w.U8(Result);
        w.U8(History.Count);
        foreach (var n in History) w.U8(n);
        w.U8(Seats.Count);
        foreach (var s in Seats)
        {
            w.U8(s.Seat);
            w.U8((byte)s.Status);
            w.VarU(s.Bankroll);
            w.VarS(s.RoundNet);
            w.U8(s.Bets.Count);
            foreach (var (index, amount) in s.Bets)
            {
                w.U8(index);
                w.VarU(amount);
            }
        }
        var flags = (Commit != null ? 1 : 0) | (Reveal != null ? 2 : 0);
        w.U8(flags);
        if (Commit != null) w.Bytes(Commit.AsSpan(0, Fair.CommitSize));
        Reveal?.Write(w);
    }

    public static RouletteView Read(ByteReader r)
    {
        var v = new RouletteView
        {
            Phase = (RoulettePhase)r.U8(),
            Round = r.VarL(),
            DeadlineSec = r.U8(),
            Result = r.U8(),
        };
        if (v.Result >= RouletteBets.Pockets && v.Result != RouletteBets.NoResult) throw new ProtocolException("Bad roulette result.");
        var history = r.U8();
        for (var i = 0; i < history; i++)
        {
            var n = r.U8();
            if (n >= RouletteBets.Pockets) throw new ProtocolException("Bad roulette result.");
            v.History.Add(n);
        }
        var seats = r.U8();
        for (var i = 0; i < seats; i++)
        {
            var s = new RouletteSeatView
            {
                Seat = r.U8(),
                Status = (RouletteSeatStatus)r.U8(),
                Bankroll = r.VarL(),
                RoundNet = r.VarS(),
            };
            var bets = r.U8();
            for (var b = 0; b < bets; b++)
            {
                var index = r.U8();
                if (!RouletteBets.IsValid(index)) throw new ProtocolException("Unknown roulette bet.");
                s.Bets.Add((index, r.VarL()));
            }
            v.Seats.Add(s);
        }
        var flags = r.U8();
        if ((flags & 1) != 0) v.Commit = r.Fixed(Fair.CommitSize);
        if ((flags & 2) != 0) v.Reveal = FairReveal.Read(r);
        return v;
    }
}
