using System;
using System.Collections.Generic;

namespace DragonboatGame.Tests;

// Shared scaffolding for the economy tests (Supply, Production, MarketDeal).
// The terms mirror Config.Market, which the tests cannot see — Config reaches
// Godot. Keep the two in step when the tuning changes.
internal static class Economy
{
    public static readonly MarketTerms Terms = new()
    {
        SmallSize = 60, MediumSize = 600, LargeSize = 6000,
        MaxOfBase = 2.5, GlutFactor = 0.8, BuyMarkup = 1.1, DecayPerDay = 0.9,
        UniversalProductId = "RICE",
    };

    public static List<Product> Products()
    {
        var list = new List<Product>();
        foreach (var (id, price) in new[] { ("RICE", 5.0), ("FISH", 20.0), ("FRUIT", 10.0), ("WOOD", 10.0), ("WOOL", 15.0) })
            list.Add(new Product { Id = id, Name = id, BasePrice = price });
        foreach (var (id, price) in new[] { ("CLOTHES", 30.0), ("SAFFRON", 38.0), ("FURNITURE", 45.0), ("JEWELRY", 60.0) })
            list.Add(new Product { Id = id, Name = id, BasePrice = price, IsLuxury = true });
        return list;
    }

    // Cities strung along a line, so distances are easy to reason about.
    public static List<City> Cities(int small, int medium, int large)
    {
        var cities = new List<City>();
        int n = 0;
        void Add(string size, int count)
        {
            for (int i = 0; i < count; i++, n++)
                cities.Add(new City { Name = $"{size}{i}", Size = size, X = n * 300, Y = 0 });
        }
        Add("small", small); Add("medium", medium); Add("large", large);
        return cities;
    }

    // One city stocking one product at a known settled price, supply 0.
    public static City Market(string size, string id, double localBase)
    {
        var city = new City { Name = "T", Size = size, MarketSize = Supply.MarketSizeFor(size, Terms) };
        city.Products.Add(id);
        city.Supply[id] = 0;
        city.LocalBase[id] = localBase;
        return city;
    }

    // A stand-in for Player: the same purse, hold and book-keeping contract
    // (ITrader) with none of Config's Godot dependencies behind it.
    public sealed class Trader : ITrader
    {
        public int Money { get; set; } = 1_000_000;
        public int Space = 1000;
        public readonly Dictionary<string, int> Held_ = new();
        public readonly Dictionary<string, double> Basis = new();

        public int CargoSpace() => Space - Total();
        public int Total()
        {
            int n = 0;
            foreach (var v in Held_.Values) n += v;
            return n;
        }

        public int Held(string id) => Held_.TryGetValue(id, out var n) ? n : 0;
        public double BasisOf(string id) => Basis.TryGetValue(id, out var v) ? v : 0;

        public void Put(string id, int units, double paid) { Held_[id] = units; Basis[id] = paid; }

        public void ApplyStaged(string id, int units, int cost, double basis)
        {
            Held_[id] = Held(id) + units;
            if (Held_[id] < 0) throw new InvalidOperationException("sold units that were never aboard");
            Money -= cost;
            Basis[id] = basis;
        }
    }
}
