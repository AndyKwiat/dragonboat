using System;
using System.Collections.Generic;
using Xunit;

// The VIP passenger clock: the reward decays with every day aboard, and a
// passenger kept too long jumps ship (src/trading/Vips.cs).
public class VipsTests
{
    private static VipTerms Terms() => new()
    {
        SpeedHonour = 200, SpeedHonourPerDay = 20, AbandonDays = 15, AbandonHonour = 1000, WarnDays = 3,
    };

    private static VipPassenger Aboard(int pickedUpDay = 0, string dest = "Goldwater") =>
        new() { Destination = new City { Name = dest, Size = "large" }, PickedUpDay = pickedUpDay };

    [Fact]
    public void AFastPassagePaysTheFullBonus()
    {
        var t = Terms();
        Assert.Equal(200, Vips.SpeedHonourFor(0, t));   // landed the day they came aboard
        Assert.Equal(200, Vips.SpeedHonourFor(1, t));
        Assert.Equal(180, Vips.SpeedHonourFor(2, t));
        Assert.Equal(100, Vips.SpeedHonourFor(6, t));
    }

    [Fact]
    public void TheBonusRunsOutAndNeverGoesNegative()
    {
        var t = Terms();
        Assert.Equal(20, Vips.SpeedHonourFor(10, t));
        Assert.Equal(0, Vips.SpeedHonourFor(11, t));
        Assert.Equal(0, Vips.SpeedHonourFor(14, t));
    }

    [Fact]
    public void DaysAboardCountFromThePickupDay()
    {
        var vip = Aboard(pickedUpDay: 7);
        Assert.Equal(0, vip.DaysAboard(7));
        Assert.Equal(3, vip.DaysAboard(10));
        Assert.Equal(0, vip.DaysAboard(6));   // never negative
    }

    private static (List<NewsEvent> news, int lost) Run(List<VipPassenger> aboard, int today)
    {
        int lost = 0;
        var news = Vips.Update(aboard, today, Terms(), n => lost += n);
        return (news, lost);
    }

    [Fact]
    public void NothingHappensWhileThePassageIsGoingWell()
    {
        var aboard = new List<VipPassenger> { Aboard() };
        var (news, lost) = Run(aboard, 11);
        Assert.Empty(news);
        Assert.Equal(0, lost);
        Assert.Single(aboard);
    }

    [Fact]
    public void ThePassengerWarnsBeforeTheyJump()
    {
        var aboard = new List<VipPassenger> { Aboard() };
        var (news, lost) = Run(aboard, 12);
        Assert.Equal(0, lost);
        var line = Assert.Single(news);
        Assert.False(line.Bad);
        Assert.Contains("Goldwater", line.Text);
        Assert.Contains("3 days", line.Text);
        Assert.Single(aboard);
        Assert.Contains("in 1 day!", Run(aboard, 14).news[0].Text);
    }

    [Fact]
    public void FifteenDaysAboardAndTheyJumpShip()
    {
        var aboard = new List<VipPassenger> { Aboard() };
        var (news, lost) = Run(aboard, 15);
        Assert.Equal(1000, lost);
        Assert.Empty(aboard);
        Assert.True(news[0].Bad);
        Assert.Contains("jumped ship", news[0].Text);
        // Gone for good: nothing more is charged afterwards.
        Assert.Equal(0, Run(aboard, 20).lost);
    }

    [Fact]
    public void PassengersAreJudgedOneByOne()
    {
        var aboard = new List<VipPassenger> { Aboard(0, "Goldwater"), Aboard(2, "Seastone") };
        var (news, lost) = Run(aboard, 15);
        Assert.Equal(1000, lost);                    // only the first one has run out of patience
        Assert.Single(aboard);
        Assert.Equal("Seastone", aboard[0].Destination.Name);
        Assert.Equal(2, news.Count);
        Assert.Contains("jumped ship", news[0].Text);   // in the order they came aboard
        Assert.Contains("is impatient", news[1].Text);
    }
}
