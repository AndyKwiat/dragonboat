using System;
using System.Collections.Generic;

// Who makes what, and what that does to prices. Run once at launch from
// GameData.SetCities, over the cities CityPlacer has just put on the coast.
//
// Deal() hands out production: a small city makes one staple, a medium one
// staple and one luxury, a large two luxuries — dealt round-robin from a
// shuffled bag per class, so every staple has roughly as many producers as
// every other staple and likewise for the luxuries. (The two classes cannot
// match each other: the size mix decides how many slots each class gets.)
//
// SetLocalPrices() then prices distance. For every product a city stocks it
// finds the LocalProducerCount nearest cities that make it — the city itself
// counts, at distance 0 — averages those distances, halves the result where
// the city is itself a producer, and normalises across the whole world into
// 0..1. That fraction marks the base price up to as much
// as (1 + LocalMaxMarkup) × base: a port sitting on its own producers sells
// at base price, one at the far end of the map settles at double. Fixed for
// the run; Supply.cs swings the live price around it.
public static class Production
{
    public const int LocalProducerCount = 3;
    public const double LocalMaxMarkup = 2.0;
    // A city that makes the product itself has its distance halved before
    // the world is normalised: the goods are not just near, they are on the
    // quay, so a producer sits well down the cheap end rather than merely
    // scoring one distance of 0 out of three.
    public const double LocalProducerDistanceFactor = 0.5;

    public static void Deal(List<City> cities, List<Product> products, Random rng, MarketTerms terms)
    {
        var staples = new List<string>();
        var luxuries = new List<string>();
        foreach (var p in products) (p.IsLuxury ? luxuries : staples).Add(p.Id);

        var stapleBag = new Bag(staples, rng);
        var luxuryBag = new Bag(luxuries, rng);
        // Shuffle the cities too, so the bags' leftovers don't always land on
        // the same corner of the coast.
        var order = new List<City>(cities);
        Shuffle(order, rng);
        foreach (var city in order)
        {
            city.Produces.Clear();
            switch (city.Size)
            {
                case "large": city.Produces.Add(luxuryBag.Take()); city.Produces.Add(luxuryBag.Take(city.Produces)); break;
                case "medium": city.Produces.Add(stapleBag.Take()); city.Produces.Add(luxuryBag.Take()); break;
                default: city.Produces.Add(stapleBag.Take()); break;
            }
        }
        // Stock: everything in a medium or large market; rice plus its own
        // staple in a small one — so a small city that makes rice sells rice
        // and nothing else.
        foreach (var city in cities)
        {
            city.Products.Clear();
            city.Supply.Clear();
            city.LocalBase.Clear();
            city.MarketSize = Supply.MarketSizeFor(city.Size, terms);
            if (city.Size == "small")
            {
                city.Products.Add(terms.UniversalProductId);
                foreach (var id in city.Produces)
                    if (!city.Products.Contains(id)) city.Products.Add(id);
            }
            else foreach (var p in products) city.Products.Add(p.Id);
            foreach (var id in city.Products) city.Supply[id] = 0;
        }
    }

    // Everything that would make the dealt world unplayable, as lines fit to
    // print. Main turns a non-empty list into a loud death rather than
    // sailing into a world with no economy.
    //
    // Two invariants, in one place because a city's trade is four collections
    // that have to agree — Products (what it stocks), Produces (what it
    // makes), Supply and LocalBase (a live level and a settled price for each
    // stocked id). Anything that deals or re-deals a market must leave all
    // four consistent; this is what says so out loud when it does not.
    public static List<string> Problems(List<City> cities, List<Product> products)
    {
        var problems = new List<string>();
        foreach (var p in products)
        {
            int n = 0;
            foreach (var c in cities) if (c.ProducesId(p.Id)) n++;
            if (n < LocalProducerCount)
                problems.Add($"{p.Id}: only {n} producing cit{(n == 1 ? "y" : "ies")}, need {LocalProducerCount}");
        }
        foreach (var c in cities)
        {
            foreach (var id in c.Products)
            {
                if (!c.Supply.ContainsKey(id)) problems.Add($"{c.Name}: stocks {id} with no supply level");
                if (!c.LocalBase.ContainsKey(id)) problems.Add($"{c.Name}: stocks {id} with no settled price");
            }
            foreach (var id in c.Produces)
                if (!c.Stocks(id)) problems.Add($"{c.Name}: makes {id} but does not stock it");
        }
        return problems;
    }

    public static void SetLocalPrices(List<City> cities, List<Product> products, float mapWidth)
    {
        // Raw remoteness first: mean distance to the nearest producers, as a
        // fraction of the map's width.
        var raw = new Dictionary<City, Dictionary<string, double>>();
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var city in cities)
        {
            var byId = new Dictionary<string, double>();
            foreach (var id in city.Products)
            {
                double d = MeanNearestProducerDistance(cities, city, id) / Math.Max(1f, mapWidth);
                if (city.ProducesId(id)) d *= LocalProducerDistanceFactor;
                byId[id] = d;
                if (d < lo) lo = d;
                if (d > hi) hi = d;
            }
            raw[city] = byId;
        }
        // Then stretch the run's own spread over the full 0..1, so the most
        // remote market in the world always pays the full markup however big
        // the map is or however many cities sit on it.
        double span = hi - lo;
        foreach (var city in cities)
            foreach (var kv in raw[city])
            {
                var product = Find(products, kv.Key);
                if (product == null) continue;
                double m = span <= 0 ? 0 : (kv.Value - lo) / span;
                city.LocalBase[kv.Key] = product.BasePrice * (1 + m * LocalMaxMarkup);
            }
    }

    // Mean distance to the LocalProducerCount nearest cities making this
    // product; the city itself is one of them if it makes it, at distance 0.
    public static double MeanNearestProducerDistance(List<City> cities, City from, string id)
    {
        var d = new List<double>();
        foreach (var c in cities)
            if (c.ProducesId(id)) d.Add(Math.Sqrt((c.X - from.X) * (double)(c.X - from.X) + (c.Y - from.Y) * (double)(c.Y - from.Y)));
        d.Sort();
        int n = Math.Min(LocalProducerCount, d.Count);
        if (n == 0) return 0;
        double sum = 0;
        for (int i = 0; i < n; i++) sum += d[i];
        return sum / n;
    }

    private static Product? Find(List<Product> products, string id)
    {
        foreach (var p in products) if (p.Id == id) return p;
        return null;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Deals ids evenly: a shuffled copy of the class is handed out one at a
    // time and only refilled once empty, so counts never drift apart by more
    // than one however many cities ask.
    private sealed class Bag
    {
        private readonly List<string> source;
        private readonly Random rng;
        private readonly List<string> left = new();

        public Bag(List<string> source, Random rng) { this.source = source; this.rng = rng; }

        // `avoid` is for the large cities, which take two luxuries and must
        // not be handed the same one twice.
        public string Take(List<string>? avoid = null)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                for (int i = 0; i < left.Count; i++)
                    if (avoid == null || !avoid.Contains(left[i]))
                    {
                        var id = left[i];
                        left.RemoveAt(i);
                        return id;
                    }
                Refill();
            }
            return left.Count > 0 ? left[0] : source[0];
        }

        private void Refill()
        {
            left.AddRange(source);
            Shuffle(left, rng);
        }
    }
}
