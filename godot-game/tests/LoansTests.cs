using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

// The bank rules: credit limits, interest, the due date and the honour
// penalties a late loan costs (src/banking/Loans.cs).
public class LoansTests
{
    private static LoanTerms Terms() => new()
    {
        HonourMultiplier = 10, MaxMedium = 5000, MaxLarge = 50000, Interest = 0.2,
        TermDays = 30, LatePenaltyDivisor = 10, LatePenaltyEveryDays = 5, WarnDays = 3, Step = 100,
        OnTimeHonourDivisor = 50,
    };

    private static City City(string size, string name = "Port") => new() { Name = name, Size = size };

    [Fact]
    public void OnlyMediumAndLargeCitiesHaveBanks()
    {
        Assert.False(Loans.HasBank(City("small")));
        Assert.True(Loans.HasBank(City("medium")));
        Assert.True(Loans.HasBank(City("large")));
    }

    [Fact]
    public void CreditIsTenTimesHonourCappedByTheCitySize()
    {
        var loans = new Loans(Terms());
        Assert.Equal(1000, loans.LimitFor(City("medium"), 100));
        Assert.Equal(5000, loans.LimitFor(City("medium"), 100_000));   // medium ceiling
        Assert.Equal(50_000, loans.LimitFor(City("large"), 100_000));  // large ceiling
        Assert.Equal(0, loans.LimitFor(City("large"), 0));
        Assert.Equal(0, loans.LimitFor(City("large"), -50));           // honour may go negative
        Assert.Equal(0, loans.LimitFor(City("small"), 100));           // no bank at all
        Assert.Equal(900, loans.LimitFor(City("medium"), 99));         // rounded down to whole hundreds
    }

    [Fact]
    public void BorrowingAddsTheInterestAndSetsTheDueDate()
    {
        var loans = new Loans(Terms());
        var city = City("medium");
        var loan = loans.Borrow(city, 1000, honour: 100, today: 5);
        Assert.NotNull(loan);
        Assert.Equal(1000, loan!.Principal);
        Assert.Equal(1200, loan.Owed);
        Assert.Equal(35, loan.DueDay);
        Assert.Equal(30, loan.DaysLeft(5));
        Assert.Same(loan, loans.LoanAt(city));
    }

    [Fact]
    public void BorrowingIsClampedToTheLimitAndOneLoanPerCity()
    {
        var loans = new Loans(Terms());
        var city = City("medium");
        var loan = loans.Borrow(city, 99_999, honour: 100, today: 0);
        Assert.Equal(1000, loan!.Principal);
        Assert.Null(loans.Borrow(city, 100, honour: 100, today: 0));   // already lent here
        Assert.Equal(0, loans.LimitFor(city, 100));
        // Another city's bank keeps its own books.
        var other = City("large", "Other");
        Assert.Equal(1000, loans.Borrow(other, 1000, honour: 100, today: 0)!.Principal);
        Assert.Equal(2, loans.All.Count);
    }

    [Fact]
    public void NothingIsLentWithoutHonourOrBelowAWholeStep()
    {
        var loans = new Loans(Terms());
        Assert.Null(loans.Borrow(City("medium"), 1000, honour: 0, today: 0));
        Assert.Null(loans.Borrow(City("medium"), 50, honour: 100, today: 0));
        Assert.Null(loans.Borrow(City("small"), 1000, honour: 100, today: 0));
    }

    [Fact]
    public void RepayingClearsTheDebt()
    {
        var loans = new Loans(Terms());
        var city = City("medium");
        var loan = loans.Borrow(city, 1000, honour: 100, today: 0)!;
        Assert.Equal(20, loans.Repay(loan, today: 10));
        Assert.Null(loans.LoanAt(city));
        Assert.False(loans.AnyOutstanding);
        Assert.Equal(0, loans.Repay(loan, today: 10));   // not twice
    }

    [Fact]
    public void PayingUpOnTimeEarnsHonour()
    {
        var loans = new Loans(Terms());
        var loan = loans.Borrow(City("large"), 2000, honour: 500, today: 0)!;
        Assert.Equal(40, loans.OnTimeHonourFor(loan, 0));    // principal / 50
        Assert.Equal(40, loans.OnTimeHonourFor(loan, 30));   // the due day still counts
        Assert.Equal(40, loans.Repay(loan, today: 30));
    }

    [Fact]
    public void ALateRepaymentEarnsNothing()
    {
        var loans = new Loans(Terms());
        var loan = loans.Borrow(City("large"), 2000, honour: 500, today: 0)!;
        Assert.Equal(0, loans.OnTimeHonourFor(loan, 31));
        Assert.Equal(0, loans.Repay(loan, today: 31));
    }

    private static (List<LoanEvent> news, int lost) Run(Loans loans, params int[] days)
    {
        var news = new List<LoanEvent>();
        int lost = 0;
        foreach (var d in days) news.AddRange(loans.Update(d, n => lost += n));
        return (news, lost);
    }

    [Fact]
    public void TheDayIsOnlyProcessedOnceAndTheFirstCallJustAnchorsIt()
    {
        var loans = new Loans(Terms());
        loans.Borrow(City("medium"), 1000, honour: 100, today: 0);
        var (news, lost) = Run(loans, 0, 0, 26);
        Assert.Empty(news);       // day 0 anchors, day 26 is still a day short of a warning
        Assert.Equal(0, lost);
    }

    [Fact]
    public void WarningsStartThreeDaysOut()
    {
        var loans = new Loans(Terms());
        loans.Borrow(City("medium", "Sword Hill"), 1000, honour: 100, today: 0);
        var (news, lost) = Run(loans, 0, 27, 28, 29, 30);
        Assert.Equal(0, lost);
        Assert.Equal(4, news.Count);
        Assert.Contains("Sword Hill", news[0].Text);
        Assert.Contains("3 days", news[0].Text);
        Assert.Contains("1 day", news[2].Text);
        Assert.Contains("TODAY", news[3].Text);
        Assert.All(news, e => Assert.False(e.Bad));
    }

    [Fact]
    public void ADayLateCostsATenthOfTheLoanInHonour()
    {
        var loans = new Loans(Terms());
        loans.Borrow(City("medium"), 1000, honour: 100, today: 0);
        var (news, lost) = Run(loans, 0, 31);
        Assert.Equal(100, lost);
        Assert.True(news[^1].Bad);
        // Nothing more until the fifth day late, then every five.
        (_, lost) = Run(loans, 32, 33, 34);
        Assert.Equal(0, lost);
        (_, lost) = Run(loans, 35);
        Assert.Equal(100, lost);
        (_, lost) = Run(loans, 36, 37, 38, 39);
        Assert.Equal(0, lost);
        (_, lost) = Run(loans, 40, 45);
        Assert.Equal(200, lost);
    }

    [Fact]
    public void ALongJumpChargesEveryPenaltyItSkipped()
    {
        var loans = new Loans(Terms());
        loans.Borrow(City("large"), 5000, honour: 500, today: 0);
        // Straight from the due date to 15 days late: penalties at 1, 5, 10, 15.
        var (_, lost) = Run(loans, 0, 45);
        Assert.Equal(4 * 500, lost);
    }

    [Fact]
    public void PenaltiesStopOnceTheLoanIsRepaid()
    {
        var loans = new Loans(Terms());
        var city = City("medium");
        var loan = loans.Borrow(city, 1000, honour: 100, today: 0)!;
        Run(loans, 0, 31);
        loans.Repay(loan, today: 31);
        var (news, lost) = Run(loans, 40);
        Assert.Empty(news);
        Assert.Equal(0, lost);
    }

    [Fact]
    public void TheSmallestPenaltyIsOneHonour()
    {
        var terms = Terms();
        terms.Step = 1;
        var loans = new Loans(terms);
        loans.Borrow(City("medium"), 5, honour: 1, today: 0);
        var (_, lost) = Run(loans, 0, 31);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void MoneyReadsShortOnTheHud()
    {
        Assert.Equal("$600", Loans.ShortAmount(600));
        Assert.Equal("$1.2K", Loans.ShortAmount(1200));
        Assert.Equal("$12K", Loans.ShortAmount(12000));
        Assert.Equal("$2K", Loans.ShortAmount(2000));
    }

    [Fact]
    public void DaysComeFromTheClocksMinutes()
    {
        Assert.Equal(0, Loans.DayOf(0));
        Assert.Equal(0, Loans.DayOf(24 * 60 - 1));
        Assert.Equal(1, Loans.DayOf(24 * 60));
    }
}
