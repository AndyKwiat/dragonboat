using System;
using System.Collections.Generic;

// Monasteries: the religion system's rules — where the houses are, giving to
// the poor for piety, and the monk errands they hand out. Whole days, like
// the loans and the VIP passengers (core/News.cs). No Godot types: this file
// is unit-tested in tests/.

public sealed class ReligionTerms
{
    public int SmallCitiesPerMonastery;// one small city in this many has a house
    public int GiveMin, GiveMax;       // the alms slider's range
    public int GiveStep;               // rounded down to a multiple of this
    public int GivePietyDivisor;       // piety earned = money given / this
    public int GiveCooldownDays;       // how long a house waits before taking again
    public int MonkTermDays;           // days to land a monk at his destination
    public int MonkPiety;              // paid on arrival in time
    public int MonkPenalty;            // lost when he gives up and walks away
    public int MonkWarnDays;           // start warning this many days before that
    // One day in this many, a house without an errand finds one — and an
    // errand no one has taken lapses. Equal odds either way, so a house is
    // waiting to be asked about half the time.
    public int MonkOfferOddsToOne;
}

// A house of monks in a small city. It remembers when it last took alms and
// the errand (if any) waiting there — neither is re-rolled when the player
// walks in, so there is nothing to be gained by leaving and coming back.
public sealed class Monastery
{
    public City City = null!;
    public int LastGiftDay = int.MinValue;
    public MonkMission? Errand;        // waiting to be accepted, if any

    public bool HasGiven => LastGiftDay != int.MinValue;

    // 0 when the poor can be given to today, else the days still to wait.
    public int DaysUntilGiveAllowed(int today, ReligionTerms t) =>
        !HasGiven ? 0 : Math.Max(0, t.GiveCooldownDays - (today - LastGiftDay));

    public bool CanGive(int today, ReligionTerms t) => DaysUntilGiveAllowed(today, t) == 0;
}

// A monk to be carried from his own house to a large city. Once accepted he
// is aboard until landed or until DueDay passes, when he walks away.
public sealed class MonkMission
{
    public City From = null!;
    public City Destination = null!;
    public int DueDay;                 // set when the errand is accepted

    public int DaysLeft(int today) => DueDay - today;
}

public sealed class Monasteries
{
    private readonly ReligionTerms terms;
    private readonly Random rng;
    private readonly List<Monastery> houses = new();
    private readonly List<City> large = new();
    private readonly List<MonkMission> aboard = new();
    private readonly DayTurn midnight = new();

    public IReadOnlyList<Monastery> Houses => houses;
    public IReadOnlyList<MonkMission> Aboard => aboard;
    // Bumped by every gift, errand taken, monk landed or lost, so the UI can
    // tell that its labels and lines need rebuilding.
    public int Version { get; private set; }

    // Houses go to one small city in every SmallCitiesPerMonastery, picked
    // from a shuffle of them — so which ones is fixed by the world's seed,
    // not by anything the player does. Half of them start with an errand
    // waiting, which is the share the daily roll then holds them at.
    public Monasteries(List<City> cities, ReligionTerms terms, Random rng)
    {
        this.terms = terms;
        this.rng = rng;
        var small = new List<City>();
        foreach (var c in cities)
        {
            if (c.Size == "small") small.Add(c);
            if (c.Size == "large") large.Add(c);
        }
        for (int i = small.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (small[i], small[j]) = (small[j], small[i]);
        }
        int wanted = small.Count / Math.Max(1, terms.SmallCitiesPerMonastery);
        for (int i = 0; i < wanted; i++)
        {
            var house = new Monastery { City = small[i] };
            if (rng.Next(2) == 0) house.Errand = RollErrand(house);
            houses.Add(house);
        }
    }

    public Monastery? At(City city)
    {
        foreach (var h in houses) if (h.City == city) return h;
        return null;
    }

    public bool HasMonastery(City city) => At(city) != null;

    // The piety a gift of this much money is worth.
    public int PietyFor(int money) => Math.Max(0, money) / terms.GivePietyDivisor;

    // What the alms slider may offer: whole GiveSteps between GiveMin and
    // GiveMax, and never more than the purse holds.
    public int MaxGift(int money) => Math.Min(terms.GiveMax, money) / terms.GiveStep * terms.GiveStep;

    // Give to the poor: the money goes, the piety comes back (the caller
    // moves both on the player). Refused while the house is still inside its
    // cooldown, or below the smallest gift it will take.
    public int Give(Monastery house, int money, int today)
    {
        if (!house.CanGive(today, terms)) return 0;
        int gift = Math.Min(money, terms.GiveMax) / terms.GiveStep * terms.GiveStep;
        if (gift < terms.GiveMin) return 0;
        house.LastGiftDay = today;
        Version++;
        return PietyFor(gift);
    }

    // Put an errand in this house now, whatever the daily roll has been
    // doing — for scripted runs and tests, never for play.
    public MonkMission? PlaceErrand(Monastery house)
    {
        house.Errand ??= RollErrand(house);
        if (house.Errand != null) Version++;
        return house.Errand;
    }

    // Take the house's errand: the monk comes aboard with MonkTermDays to
    // reach his destination.
    public MonkMission? Accept(Monastery house, int today)
    {
        if (house.Errand is not MonkMission monk) return null;
        house.Errand = null;
        monk.DueDay = today + terms.MonkTermDays;
        aboard.Add(monk);
        Version++;
        return monk;
    }

    // The monk aboard bound for this city, if any — for the drop-off tag
    // under its map label.
    public MonkMission? MonkFor(City destination)
    {
        foreach (var m in aboard) if (m.Destination == destination) return m;
        return null;
    }

    // Landing monks: everyone aboard bound here steps ashore, MonkPiety
    // apiece. Returns how many and what they were worth.
    public (int Count, int Piety) DeliverMonks(City city)
    {
        int n = aboard.RemoveAll(m => m.Destination == city);
        if (n > 0) Version++;
        return (n, n * terms.MonkPiety);
    }

    // Call every frame with the clock's day index; free until the day turns
    // over. Then: monks out of time walk away (costing piety, which may go
    // negative), the ones getting close say so, and the houses roll their
    // errands — one appearing, or an untaken one lapsing, at the same odds,
    // so about half of them are waiting to be asked at any moment.
    public List<NewsEvent> Update(int today, Action<int> losePiety)
    {
        var news = new List<NewsEvent>();
        if (!midnight.Turned(today)) return news;
        for (int i = aboard.Count - 1; i >= 0; i--)
        {
            var monk = aboard[i];
            int left = monk.DaysLeft(today);
            if (left <= 0)
            {
                aboard.RemoveAt(i);
                losePiety(terms.MonkPenalty);
                Version++;
                news.Add(new NewsEvent(
                    $"The monk for {monk.Destination.Name} gave up and left — piety -{terms.MonkPenalty}", true));
            }
            else if (left <= terms.MonkWarnDays)
                news.Add(new NewsEvent(
                    $"The monk for {monk.Destination.Name} must land in {left} day{(left == 1 ? "" : "s")}!", false));
        }
        news.Reverse();
        foreach (var house in houses)
        {
            if (rng.Next(terms.MonkOfferOddsToOne) != 0) continue;
            if (house.Errand != null) { house.Errand = null; Version++; }       // untaken: lapses
            else if (!CarryingFrom(house.City)) { house.Errand = RollErrand(house); Version++; }
        }
        return news;
    }

    // One monk from a house at a time: no new errand while his brother is
    // still aboard.
    private bool CarryingFrom(City city)
    {
        foreach (var m in aboard) if (m.From == city) return true;
        return false;
    }

    // Monks travel to large cities only, never the house's own city (which
    // is small anyway).
    private MonkMission? RollErrand(Monastery house)
    {
        if (large.Count == 0) return null;
        var dest = large[rng.Next(large.Count)];
        return dest == house.City ? null : new MonkMission { From = house.City, Destination = dest };
    }
}
