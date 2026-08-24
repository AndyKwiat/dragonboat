using System;
using System.Collections.Generic;

// Story events: happenings out in the world, always tied to a city. The only
// kind so far is a price swing: one product a city trades goes low or high
// (a multiplier on the product's BASE price) until the event expires. The
// world always carries Config.StoryEventCount of them; when one lapses a
// fresh one is rolled at a random city. Expiries always land on a midnight,
// so Update() is a single compare until the clock crosses one — only then
// does a pass drop the lapsed events and roll replacements.
public abstract class StoryEvent
{
    public City City = null!;
    public long ExpiresAtMinutes;    // a midnight, in GameTime total minutes
    // Set when the player opens the city's popup while the event is live;
    // gates the event's price intel under the city's map label.
    public bool Discovered;

    public int DaysLeftAt(long nowMinutes) =>
        (int)Math.Ceiling((ExpiresAtMinutes - nowMinutes) / (double)GameTime.MinutesPerDay);

    // How the event reads as a rumour in another city's market.
    public abstract string Description { get; }
}

public sealed class ProductPriceStoryEvent : StoryEvent
{
    public Product Product = null!;
    public double PriceMultiplier;   // of base price: low or high, never 1

    public override string Description =>
        $"{Product.Name} prices are very {(PriceMultiplier > 1 ? "high" : "low")} in {City.Name}";
}

// A VIP waits in City for passage to Destination — a city ranked
// Config.VipMinRank..VipMaxRank by distance (never the 5 closest, never
// beyond the 10th). Accepting (StoryEvents.Accept) puts the passenger on
// the boat and retires the offer; the delivery itself never expires.
public sealed class VipDeliveryStoryEvent : StoryEvent
{
    public City Destination = null!;

    public override string Description =>
        $"An important passenger in {City.Name} seeks passage to {Destination.Name}";
}

public sealed class StoryEvents
{
    private readonly GameTime time;
    private readonly Random rng;
    private readonly List<StoryEvent> events = new();
    // Product id → 1 / (cities trading it): events favour the rarer goods,
    // but rice — sold everywhere — keeps a small chance.
    private readonly Dictionary<string, double> rarity = new();
    private long nextMidnight;

    public IReadOnlyList<StoryEvent> All => events;

    public StoryEvents(GameTime time, Random? rng = null)
    {
        this.time = time;
        this.rng = rng ?? new Random();
        foreach (var c in GameData.Cities)
            foreach (var id in c.Products)
                rarity[id] = rarity.GetValueOrDefault(id) + 1;
        foreach (var id in new List<string>(rarity.Keys)) rarity[id] = 1.0 / rarity[id];
        nextMidnight = GameTime.MidnightAfter(time.TotalMinutes);
        TopUp();
    }

    // The player has seen this city's market: every event live there is now
    // known (the popup shows its price and expiry).
    public void DiscoverAt(City city)
    {
        foreach (var e in events)
            if (e.City == city) e.Discovered = true;
    }

    // What's whispered in this city's market: the count nearest events that
    // are NOT the city's own. Hearing a rumour is up to the caller (the
    // popup marks them discovered).
    public List<StoryEvent> RumoursFor(City city, int count)
    {
        var others = new List<StoryEvent>();
        foreach (var e in events)
            if (e.City != city) others.Add(e);
        others.Sort((a, b) => DistanceSq(city, a.City).CompareTo(DistanceSq(city, b.City)));
        if (others.Count > count) others.RemoveRange(count, others.Count - count);
        return others;
    }

    private static float DistanceSq(City a, City b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    // The active price event for this product at this city, if any.
    public ProductPriceStoryEvent? PriceEventFor(City city, string productId)
    {
        foreach (var e in events)
            if (e is ProductPriceStoryEvent p && p.City == city && p.Product.Id == productId) return p;
        return null;
    }

    // Call every frame; free until the clock crosses midnight. Returns true
    // when a midnight pass ran, so an open market menu knows its prices and
    // days-left readouts are stale.
    public bool Update()
    {
        if (time.TotalMinutes < nextMidnight) return false;
        nextMidnight = GameTime.MidnightAfter(time.TotalMinutes);
        events.RemoveAll(e => e.ExpiresAtMinutes <= time.TotalMinutes);
        TopUp();
        return true;
    }

    // The world holds StoryEventCount price events and VipEventCount VIP
    // offers at all times; each kind is topped up separately.
    private void TopUp()
    {
        int prices = 0, vips = 0;
        foreach (var e in events) { if (e is VipDeliveryStoryEvent) vips++; else prices++; }
        while (prices < Config.StoryEventCount)
        {
            var e = RollPriceEvent();
            if (e == null) break;   // no distinct (city, product) slot left
            events.Add(e); prices++;
        }
        while (vips < Config.VipEventCount)
        {
            var e = RollVipEvent();
            if (e == null) break;   // no city free of a VIP offer left
            events.Add(e); vips++;
        }
    }

    // The live VIP offer waiting in this city, if any (one per city).
    public VipDeliveryStoryEvent? VipEventFor(City city)
    {
        foreach (var e in events)
            if (e is VipDeliveryStoryEvent v && v.City == city) return v;
        return null;
    }

    // The player takes the VIP aboard: the offer leaves the world and a
    // fresh one is rolled elsewhere so the count holds.
    public void Accept(VipDeliveryStoryEvent e)
    {
        events.Remove(e);
        TopUp();
    }

    // A random city without a VIP offer; the destination is drawn from the
    // cities ranked VipMinRank..VipMaxRank by distance from it.
    private VipDeliveryStoryEvent? RollVipEvent()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            var city = GameData.Cities[rng.Next(GameData.Cities.Count)];
            if (VipEventFor(city) != null) continue;
            var others = new List<City>();
            foreach (var c in GameData.Cities) if (c != city) others.Add(c);
            if (others.Count < Config.VipMinRank) continue;
            others.Sort((a, b) => DistanceSq(city, a).CompareTo(DistanceSq(city, b)));
            int hi = Math.Min(others.Count, Config.VipMaxRank);
            var dest = others[rng.Next(Config.VipMinRank - 1, hi)];
            int days = rng.Next(Config.StoryEventMinDays, Config.StoryEventMaxDays + 1);
            return new VipDeliveryStoryEvent
            {
                City = city,
                Destination = dest,
                ExpiresAtMinutes = GameTime.StartOfDay(time.TotalMinutes) + (long)days * GameTime.MinutesPerDay,
            };
        }
        return null;
    }

    // A random city, one of the products it actually trades (rarity-weighted),
    // high StoryEventHighOddsToOne times as often as low — but never a second
    // price event on the same product in the same city, so a quote always has
    // one unambiguous multiplier, and never on a city's speciality, whose
    // discount is promised to be permanent.
    private ProductPriceStoryEvent? RollPriceEvent()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            var city = GameData.Cities[rng.Next(GameData.Cities.Count)];
            if (city.Products.Count == 0) continue;
            var product = GameData.ProductById(PickByRarity(city.Products));
            if (product == null || city.Specialities.Contains(product.Id)) continue;
            if (PriceEventFor(city, product.Id) != null) continue;
            int days = rng.Next(Config.StoryEventMinDays, Config.StoryEventMaxDays + 1);
            return new ProductPriceStoryEvent
            {
                City = city,
                Product = product,
                PriceMultiplier = rng.Next(Config.StoryEventHighOddsToOne + 1) == 0
                    ? Config.StoryEventLowMultiplier : Config.StoryEventHighMultiplier,
                // "days into the future", landing on that day's midnight.
                ExpiresAtMinutes = GameTime.StartOfDay(time.TotalMinutes) + (long)days * GameTime.MinutesPerDay,
            };
        }
        return null;
    }

    // Roulette-wheel pick over the city's products, each weighted by rarity.
    private string PickByRarity(List<string> ids)
    {
        double total = 0;
        foreach (var id in ids) total += rarity.GetValueOrDefault(id, 1);
        double roll = rng.NextDouble() * total;
        foreach (var id in ids)
        {
            roll -= rarity.GetValueOrDefault(id, 1);
            if (roll <= 0) return id;
        }
        return ids[^1];
    }
}
