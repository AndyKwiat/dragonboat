using System;
using System.Collections.Generic;

// Bank loans: the rules behind the bank window in medium and large cities.
// No Godot types — unit-tested in tests/.
//
// Everything here counts whole DAYS (an absolute day index, day 0 = the
// clock's first day), not minutes: a loan falls due at a midnight and the
// late penalties are charged at midnights too, so Main converts the clock
// once (Loans.DayOf) and this file is plain integer arithmetic.

// The terms every bank lends on (Config.Bank).
public sealed class LoanTerms
{
    public int HonourMultiplier;   // credit limit = honour x this
    public int MaxMedium, MaxLarge;// per-city ceilings on top of the limit
    public double Interest;        // repaid = ceil(principal x (1 + Interest))
    public int TermDays;           // days from borrowing to the due date
    public int LatePenaltyDivisor; // honour lost per penalty = principal / this
    public int LatePenaltyEveryDays; // after the first (1 day late), every this many days
    public int OnTimeHonourDivisor;// honour earned for repaying on time = principal / this
    public int WarnDays;           // start warning this many days before it falls due
    public int Step;               // borrowing is rounded down to a multiple of this
}

// One outstanding loan, held by the city that lent it (one per city).
public sealed class Loan
{
    public City City = null!;
    public int Principal;    // what was handed over
    public int Owed;         // principal + interest — what has to come back
    public int DueDay;       // absolute day index of the midnight it falls due
    public int LastPenaltyLateDays;  // days-late the last penalty was charged for

    public int DaysLeft(int today) => DueDay - today;
    public int DaysLate(int today) => Math.Max(0, today - DueDay);
    public bool IsLate(int today) => today > DueDay;
}

// Something the world did to the player, for the event log (ui/EventLog.cs).
public readonly record struct LoanEvent(string Text, bool Bad);

public sealed class Loans
{
    private readonly LoanTerms terms;
    private readonly List<Loan> loans = new();
    private int lastDay = int.MinValue;

    public IReadOnlyList<Loan> All => loans;
    // Bumped by every borrow, repayment and penalty, so the UI (city labels,
    // the clock) can tell at a glance that it has to redraw.
    public int Version { get; private set; }

    public Loans(LoanTerms terms) => this.terms = terms;

    // The clock's total minutes as the absolute day index this file counts in.
    public static int DayOf(long totalMinutes) => (int)(totalMinutes / GameTimeMinutesPerDay);
    // GameTime is not compiled into the test project (it reaches Config, which
    // is a Godot file), so the one constant it shares lives here as well.
    public const int GameTimeMinutesPerDay = 24 * 60;

    // Money as it reads on the HUD: $12K, $1.5K, $600.
    public static string ShortAmount(int money)
    {
        if (money < 1000) return $"${money}";
        double k = money / 1000.0;
        return k >= 10 || k == Math.Floor(k) ? $"${Math.Round(k)}K" : $"${k:0.#}K";
    }

    public static bool HasBank(City city) => city.Size == "medium" || city.Size == "large";

    public Loan? LoanAt(City city)
    {
        foreach (var l in loans) if (l.City == city) return l;
        return null;
    }

    public bool AnyOutstanding => loans.Count > 0;

    // The most this city will lend right now: honour x the multiplier, capped
    // by the city's own ceiling and rounded down to a whole Step. Zero while
    // honour is nil or negative, or when this bank already holds a loan.
    public int LimitFor(City city, int honour)
    {
        if (!HasBank(city) || LoanAt(city) != null) return 0;
        long limit = Math.Max(0, (long)honour) * terms.HonourMultiplier;
        limit = Math.Min(limit, city.Size == "large" ? terms.MaxLarge : terms.MaxMedium);
        return (int)(limit / terms.Step * terms.Step);
    }

    public int OwedFor(int principal) => (int)Math.Ceiling(principal * (1 + terms.Interest));

    // What settling this loan today is worth in honour: a share of the
    // principal for paying up on or before the due date, nothing once it has
    // run late (the penalties have already been charged for that).
    public int OnTimeHonourFor(Loan loan, int today) =>
        loan.IsLate(today) ? 0 : Math.Max(1, loan.Principal / terms.OnTimeHonourDivisor);

    // Take out a loan: the amount is clamped to the city's limit and rounded
    // down to a whole Step. Returns the loan, or null if nothing was lent.
    public Loan? Borrow(City city, int amount, int honour, int today)
    {
        int max = LimitFor(city, honour);
        int principal = Math.Min(Math.Max(0, amount), max) / terms.Step * terms.Step;
        if (principal <= 0) return null;
        var loan = new Loan
        {
            City = city,
            Principal = principal,
            Owed = OwedFor(principal),
            DueDay = today + terms.TermDays,
        };
        loans.Add(loan);
        Version++;
        return loan;
    }

    // Pay a loan off — in full, from the city that lent it, or not at all.
    // Returns the honour earned by settling it on time (0 if it was late, or
    // if this loan was not outstanding); the caller banks it.
    public int Repay(Loan loan, int today)
    {
        int honour = OnTimeHonourFor(loan, today);
        if (!loans.Remove(loan)) return 0;
        Version++;
        return honour;
    }

    // Call every frame with the clock's day index: free until the day turns
    // over, then one pass that warns about loans coming due and charges the
    // honour penalties for the late ones. Honour lost is returned through
    // `loseHonour` (it may take the player negative), and everything the
    // player should be told about comes back as events.
    public List<LoanEvent> Update(int today, Action<int> loseHonour)
    {
        var news = new List<LoanEvent>();
        if (today == lastDay) return news;
        bool first = lastDay == int.MinValue;
        lastDay = today;
        if (first) return news;   // the first call only anchors the day
        foreach (var loan in loans)
        {
            int late = loan.DaysLate(today);
            if (late == 0)
            {
                int left = loan.DaysLeft(today);
                if (left <= terms.WarnDays)
                    news.Add(new LoanEvent(left == 0
                        ? $"Loan due TODAY in {loan.City.Name} — {loan.Owed} to repay!"
                        : $"Loan coming due in {loan.City.Name} in {left} day{(left == 1 ? "" : "s")}!!", false));
                continue;
            }
            // A penalty a day late, then one every LatePenaltyEveryDays after
            // that — caught up in one go if the clock jumped several days.
            while (NextPenaltyAt(loan.LastPenaltyLateDays) <= late)
            {
                loan.LastPenaltyLateDays = NextPenaltyAt(loan.LastPenaltyLateDays);
                int lost = Math.Max(1, loan.Principal / terms.LatePenaltyDivisor);
                loseHonour(lost);
                Version++;
                news.Add(new LoanEvent(
                    $"Late on your {loan.City.Name} loan ({loan.LastPenaltyLateDays}d) — honour {-lost}", true));
            }
        }
        return news;
    }

    // Penalties land 1 day late, then on every multiple of LatePenaltyEveryDays.
    private int NextPenaltyAt(int lastLateDays) =>
        lastLateDays == 0 ? 1 : (lastLateDays / terms.LatePenaltyEveryDays + 1) * terms.LatePenaltyEveryDays;
}
