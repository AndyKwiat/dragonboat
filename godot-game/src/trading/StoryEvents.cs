using System;
using System.Collections.Generic;

// Story events: happenings out in the world, always tied to a city. The only
// kind so far is a supply shock: one product a city trades is suddenly short
// (dear) or glutted (cheap) — the event shoves the city's stock level hard
// one way when it rolls, and the market's own 10%-a-day decay walks it back
// while the event runs, so the swing fades instead of switching off. The
// world always carries game-config.yml's priceEvents of them (and vipEvents
// VIP offers); when one lapses a fresh one is rolled at a random city. Expiries always land on a midnight,
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
    public bool Shortage;   // true = stock shoved negative (dear), false = a glut (cheap)

    public override string Description =>
        $"{Product.Name} prices are very {(Shortage ? "high" : "low")} in {City.Name}";
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

    // The world holds game-config.yml's priceEvents / vipEvents at all
    // times; each kind is topped up separately (0 = that kind never spawns).
    private void TopUp()
    {
        int prices = 0, vips = 0;
        foreach (var e in events) { if (e is VipDeliveryStoryEvent) vips++; else prices++; }
        while (prices < GameConfig.Current.PriceEvents)
        {
            var e = RollPriceEvent();
            if (e == null) break;   // no distinct (city, product) slot left
            events.Add(e); prices++;
        }
        while (vips < GameConfig.Current.VipEvents)
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

    // A random city, one of the products it actually stocks (rarity-weighted),
    // a shortage StoryEventShortageOddsToOne times as often as a glut — but
    // never a second event on the same product in the same city, so one
    // shock's decay is never fighting another's. Rolling the event IS the
    // shock: the city's stock level jumps by StoryEventShockFraction of its
    // market size there and then, and decays back from that day on.
    private ProductPriceStoryEvent? RollPriceEvent()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            var city = GameData.Cities[rng.Next(GameData.Cities.Count)];
            if (city.Products.Count == 0) continue;
            var product = GameData.ProductById(PickByRarity(city.Products));
            if (product == null) continue;
            if (PriceEventFor(city, product.Id) != null) continue;
            int days = rng.Next(Config.StoryEventMinDays, Config.StoryEventMaxDays + 1);
            bool shortage = rng.Next(Config.StoryEventShortageOddsToOne + 1) != 0;
            int shock = (int)Math.Round(city.MarketSize * Config.StoryEventShockFraction);
            city.Supply[product.Id] = Supply.Clamp(
                city.SupplyOf(product.Id) + (shortage ? -shock : shock), city.MarketSize);
            return new ProductPriceStoryEvent
            {
                City = city,
                Product = product,
                Shortage = shortage,
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
