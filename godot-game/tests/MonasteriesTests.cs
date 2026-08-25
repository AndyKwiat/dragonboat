using System;
using System.Collections.Generic;
using Xunit;

// Monasteries: where the houses are, alms and their weekly cooldown, and the
// monk errands (src/religion/Monasteries.cs).
public class MonasteriesTests
{
    private static ReligionTerms Terms() => new()
    {
        SmallCitiesPerMonastery = 5,
        GiveMin = 100, GiveMax = 2000, GiveStep = 100, GivePietyDivisor = 10, GiveCooldownDays = 7,
        MonkTermDays = 30, MonkPiety = 1000, MonkPenalty = 1000, MonkWarnDays = 3, MonkOfferOddsToOne = 10,
    };

    // A world of `small` small cities and `large` large ones.
    private static List<City> World(int small, int large)
    {
        var cities = new List<City>();
        for (int i = 0; i < small; i++) cities.Add(new City { Name = $"Small{i}", Size = "small" });
        for (int i = 0; i < large; i++) cities.Add(new City { Name = $"Large{i}", Size = "large" });
        cities.Add(new City { Name = "Middling", Size = "medium" });
        return cities;
    }

    private static Monasteries Make(out List<City> cities, int small = 20, int large = 4, int seed = 1)
    {
        cities = World(small, large);
        return new Monasteries(cities, Terms(), new Random(seed));
    }

    [Fact]
    public void OneSmallCityInFiveGetsAHouse()
    {
        var religion = Make(out var cities, small: 20, large: 4);
        Assert.Equal(4, religion.Houses.Count);
        foreach (var h in religion.Houses) Assert.Equal("small", h.City.Size);
        // And only small ones: no medium or large city has one.
        foreach (var c in cities)
            if (c.Size != "small") Assert.False(religion.HasMonastery(c));
    }

    [Fact]
    public void TheSameSeedPutsThemInTheSamePlaces()
    {
        var a = Make(out _, seed: 7);
        var b = Make(out _, seed: 7);
        for (int i = 0; i < a.Houses.Count; i++)
            Assert.Equal(a.Houses[i].City.Name, b.Houses[i].City.Name);
    }

    [Fact]
    public void AlmsBuyPietyWorthATenthOfTheGift()
    {
        var religion = Make(out _);
        Assert.Equal(20, religion.PietyFor(200));
        Assert.Equal(200, religion.PietyFor(2000));
        Assert.Equal(0, religion.PietyFor(0));
    }

    [Fact]
    public void TheSliderNeverOffersMoreThanThePurseOrTheCeiling()
    {
        var religion = Make(out _);
        Assert.Equal(2000, religion.MaxGift(99_999));
        Assert.Equal(500, religion.MaxGift(500));
        Assert.Equal(500, religion.MaxGift(560));    // whole hundreds
        Assert.Equal(0, religion.MaxGift(50));
    }

    [Fact]
    public void AHouseTakesAlmsOnlyOnceAWeek()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        Assert.True(house.CanGive(0, Terms()));
        Assert.Equal(20, religion.Give(house, 200, today: 0));
        Assert.False(house.CanGive(3, Terms()));
        Assert.Equal(4, house.DaysUntilGiveAllowed(3, Terms()));
        Assert.Equal(0, religion.Give(house, 200, today: 3));   // refused inside the week
        Assert.Equal(0, house.DaysUntilGiveAllowed(7, Terms()));
        Assert.Equal(200, religion.Give(house, 2000, today: 7));
        // Each house keeps its own week.
        Assert.True(religion.Houses[1].CanGive(7, Terms()));
    }

    [Fact]
    public void GiftsAreClampedAndTooSmallOnesRefused()
    {
        var religion = Make(out _);
        Assert.Equal(200, religion.Give(religion.Houses[0], 99_999, today: 0));   // capped at 2000
        Assert.Equal(0, religion.Give(religion.Houses[1], 50, today: 0));         // under the minimum
        Assert.False(religion.Houses[1].HasGiven);
    }

    [Fact]
    public void AnErrandCarriesAMonkToALargeCity()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        religion.PlaceErrand(house);
        var offer = house.Errand!;
        Assert.Equal("large", offer.Destination.Size);
        Assert.Same(house.City, offer.From);

        var monk = religion.Accept(house, today: 4);
        Assert.Same(offer, monk);
        Assert.Equal(34, monk!.DueDay);           // 30 days to land him
        Assert.Null(house.Errand);                 // taken, not still on offer
        Assert.Single(religion.Aboard);
        Assert.Null(religion.Accept(house, today: 4));   // nothing left to take
    }

    [Fact]
    public void LandingHimPaysPiety()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        religion.PlaceErrand(house);
        var monk = religion.Accept(house, today: 0)!;
        Assert.Same(monk, religion.MonkFor(monk.Destination));
        var landed = religion.DeliverMonks(monk.Destination);
        Assert.Equal(1, landed.Count);
        Assert.Equal(1000, landed.Piety);
        Assert.Empty(religion.Aboard);
        Assert.Equal((0, 0), religion.DeliverMonks(monk.Destination));
    }

    private static (List<NewsEvent> news, int lost) Run(Monasteries religion, params int[] days)
    {
        var news = new List<NewsEvent>();
        int lost = 0;
        foreach (var d in days) news.AddRange(religion.Update(d, n => lost += n));
        return (news, lost);
    }

    [Fact]
    public void AMonkOutOfTimeWalksAwayAndCostsPiety()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        religion.PlaceErrand(house);
        var monk = religion.Accept(house, today: 0)!;
        // Quiet until the last three days, then warnings, then he goes.
        Assert.Empty(Run(religion, 0, 26).news);
        var (warned, nothingLost) = Run(religion, 27);
        Assert.Equal(0, nothingLost);
        Assert.Contains("must land in 3 days", warned[0].Text);
        Assert.False(warned[0].Bad);
        var (gone, lost) = Run(religion, 30);
        Assert.Equal(1000, lost);
        Assert.True(gone[0].Bad);
        Assert.Contains("gave up and left", gone[0].Text);
        Assert.Empty(religion.Aboard);
        Assert.Equal(0, Run(religion, 40).lost);
    }

    [Fact]
    public void TheDailyPassRunsOnceADayAndNotOnTheFirstCall()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        religion.PlaceErrand(house);
        religion.Accept(house, today: 0);
        Assert.Empty(Run(religion, 0, 0, 0).news);        // anchors, then nothing new
        Assert.Empty(Run(religion, 27, 27, 27).news[1..]); // one warning for the day, not three
    }

    [Fact]
    public void WalkingInAndOutOfAHouseNeverRerollsItsErrand()
    {
        var religion = Make(out _);
        var house = religion.Houses[0];
        religion.PlaceErrand(house);
        var offer = house.Errand;
        // Nothing but the daily pass touches it — reading it is free.
        for (int i = 0; i < 50; i++) Assert.Same(offer, religion.At(house.City)!.Errand);
    }

    [Fact]
    public void AboutHalfTheHousesHaveAnErrandWaiting()
    {
        var religion = Make(out _, small: 200, large: 6, seed: 3);
        int waiting = 0, samples = 0;
        for (int day = 1; day <= 200; day++)
        {
            religion.Update(day, _ => { });
            if (day % 10 != 0) continue;
            foreach (var h in religion.Houses) { samples++; if (h.Errand != null) waiting++; }
        }
        double share = (double)waiting / samples;
        Assert.InRange(share, 0.4, 0.6);
    }
}
