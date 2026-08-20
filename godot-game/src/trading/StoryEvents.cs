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
}

public sealed class ProductPriceStoryEvent : StoryEvent
{
    public Product Product = null!;
    public double PriceMultiplier;   // of base price: low or high, never 1
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

    private void TopUp()
    {
        while (events.Count < Config.StoryEventCount)
        {
            var e = RollPriceEvent();
            if (e == null) break;   // no distinct (city, product) slot left
            events.Add(e);
        }
    }

    // A random city, one of the products it actually trades (rarity-weighted),
    // low or high with even odds — but never a second price event on the same
    // product in the same city, so a quote always has one unambiguous
    // multiplier.
    private ProductPriceStoryEvent? RollPriceEvent()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            var city = GameData.Cities[rng.Next(GameData.Cities.Count)];
            if (city.Products.Count == 0) continue;
            var product = GameData.ProductById(PickByRarity(city.Products));
            if (product == null || PriceEventFor(city, product.Id) != null) continue;
            int days = rng.Next(Config.StoryEventMinDays, Config.StoryEventMaxDays + 1);
            return new ProductPriceStoryEvent
            {
                City = city,
                Product = product,
                PriceMultiplier = rng.Next(2) == 0 ? Config.StoryEventLowMultiplier : Config.StoryEventHighMultiplier,
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
