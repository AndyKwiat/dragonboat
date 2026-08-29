using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

// Who makes what, and what distance from the producers does to a city's
// settled price (src/trading/Production.cs).
public class ProductionTests
{
    private static MarketTerms Terms => Economy.Terms;
    private static List<Product> Products() => Economy.Products();
    private static List<City> Cities(int small, int medium, int large) => Economy.Cities(small, medium, large);

    [Fact]
    public void EachSizeMakesWhatItsSizeMakes()
    {
        var products = Products();
        var cities = Cities(18, 18, 5);
        Production.Deal(cities, products, new Random(7), Terms);
        var luxury = new HashSet<string>();
        foreach (var p in products) if (p.IsLuxury) luxury.Add(p.Id);
        foreach (var c in cities)
        {
            int lux = 0;
            foreach (var id in c.Produces) if (luxury.Contains(id)) lux++;
            switch (c.Size)
            {
                case "small": Assert.Single(c.Produces); Assert.Equal(0, lux); break;
                case "medium": Assert.Equal(2, c.Produces.Count); Assert.Equal(1, lux); break;
                default: Assert.Equal(2, c.Produces.Count); Assert.Equal(2, lux); break;
            }
            Assert.Equal(c.Produces.Count, new HashSet<string>(c.Produces).Count);
        }
    }

    [Fact]
    public void ProducersAreSpreadEvenlyWithinEachClass()
    {
        var products = Products();
        for (int seed = 0; seed < 20; seed++)
        {
            var cities = Cities(18, 18, 5);
            Production.Deal(cities, products, new Random(seed), Terms);
            var count = new Dictionary<string, int>();
            foreach (var p in products) count[p.Id] = 0;
            foreach (var c in cities) foreach (var id in c.Produces) count[id]++;
            foreach (bool luxury in new[] { false, true })
            {
                int lo = int.MaxValue, hi = 0;
                foreach (var p in products)
                    if (p.IsLuxury == luxury) { lo = Math.Min(lo, count[p.Id]); hi = Math.Max(hi, count[p.Id]); }
                // The bag is only refilled once empty, so counts can differ by
                // at most one full round of dealing.
                Assert.True(hi - lo <= 2, $"seed {seed} {(luxury ? "luxury" : "staple")} spread {lo}..{hi}");
                Assert.True(lo >= Production.LocalProducerCount, $"seed {seed}: only {lo} producers of something");
            }
        }
    }

    [Fact]
    public void SmallCitiesStockRiceAndTheirOwnStapleOnly()
    {
        var products = Products();
        var cities = Cities(18, 18, 5);
        Production.Deal(cities, products, new Random(3), Terms);
        foreach (var c in cities)
        {
            if (c.Size == "small")
            {
                Assert.Contains("RICE", c.Products);
                if (c.Produces[0] == "RICE") Assert.Single(c.Products); else Assert.Equal(2, c.Products.Count);
                foreach (var id in c.Produces) Assert.Contains(id, c.Products);
                Assert.Equal(60, c.MarketSize);
            }
            else
            {
                Assert.Equal(products.Count, c.Products.Count);
                Assert.Equal(c.Size == "large" ? 6000 : 600, c.MarketSize);
            }
            foreach (var id in c.Products) Assert.Equal(0, c.SupplyOf(id));   // markets open settled
        }
    }

    // Production.Problems is the one gate between a dealt world and a run:
    // too few producers of a good, or a market whose four collections have
    // drifted apart.
    [Fact]
    public void AWellDealtWorldHasNoProblems()
    {
        var products = Products();
        var cities = Cities(18, 18, 5);
        Production.Deal(cities, products, new Random(1), Terms);
        Production.SetLocalPrices(cities, products, 8192);
        Assert.Empty(Production.Problems(cities, products));
    }

    [Fact]
    public void TooFewProducersIsReported()
    {
        var products = Products();
        // Two cities cannot possibly make everything three times over.
        var tiny = Cities(1, 1, 0);
        Production.Deal(tiny, products, new Random(1), Terms);
        Production.SetLocalPrices(tiny, products, 8192);
        var problems = Production.Problems(tiny, products);
        Assert.NotEmpty(problems);
        Assert.All(problems, p => Assert.Contains("producing cit", p));
    }

    [Fact]
    public void AMarketWhoseCollectionsDisagreeIsReported()
    {
        var products = Products();
        var cities = Cities(18, 18, 5);
        Production.Deal(cities, products, new Random(1), Terms);
        Production.SetLocalPrices(cities, products, 8192);
        // Stocking something without giving it a stock level or a price.
        cities[0].Products.Add("JEWELRY");
        var problems = Production.Problems(cities, products);
        Assert.Contains(problems, p => p.Contains("no supply level"));
        Assert.Contains(problems, p => p.Contains("no settled price"));
        // Making something the market does not carry.
        var other = Cities(1, 0, 0)[0];
        other.Produces.Add("WOOL");
        Assert.Contains(Production.Problems(new List<City> { other }, products),
            p => p.Contains("does not stock it"));
    }


    [Fact]
    public void DistanceFromTheProducersSetsTheSettledPrice()
    {
        var products = Products();
        var cities = Cities(18, 18, 5);
        Production.Deal(cities, products, new Random(11), Terms);
        Production.SetLocalPrices(cities, products, 8192);
        double lo = double.MaxValue, hi = 0;
        foreach (var c in cities)
            foreach (var id in c.Products)
            {
                var p = products.Find(x => x.Id == id)!;
                double local = c.LocalBaseOf(id);
                // Never below the world base price, never above double it.
                Assert.InRange(local, p.BasePrice - 1e-9, p.BasePrice * (1 + Production.LocalMaxMarkup) + 1e-9);
                double ratio = local / p.BasePrice;
                lo = Math.Min(lo, ratio); hi = Math.Max(hi, ratio);
            }
        // Normalised across the run, so the range is actually used: the
        // closest market to its producers pays base, the furthest pays double.
        Assert.Equal(1.0, lo, 6);
        Assert.Equal(1 + Production.LocalMaxMarkup, hi, 6);
    }

    // Two cities the same distance from the same three producers, one of
    // which is a producer itself: its distance is halved, so it settles lower.
    [Fact]
    public void AProducerIsPricedAsIfItWereHalfAsFarFromTheGoods()
    {
        var products = new List<Product> { new() { Id = "WOOL", Name = "Wool", BasePrice = 15 } };
        var cities = new List<City>();
        for (int i = 0; i < 4; i++)
            cities.Add(new City { Name = $"p{i}", Size = "medium", X = i * 500, Y = 0 });
        // The first three make wool; the fourth does not.
        for (int i = 0; i < 3; i++) cities[i].Produces.Add("WOOL");
        foreach (var c in cities)
        {
            c.Products.Add("WOOL");
            c.MarketSize = Supply.MarketSizeFor(c.Size, Terms);
        }
        // Raw means: p0 500, p1 1000/3, p2 500, p3 1000. Halving p0..p2 puts
        // every producer below the one city that makes none of it.
        Production.SetLocalPrices(cities, products, 8192);
        for (int i = 0; i < 3; i++)
            Assert.True(cities[i].LocalBaseOf("WOOL") < cities[3].LocalBaseOf("WOOL"),
                $"producer p{i} did not settle below the non-producer");
        // p1 is the nearest to the goods of all, so it is the run's floor;
        // p3 is the furthest, so it takes the full markup.
        Assert.Equal(15, cities[1].LocalBaseOf("WOOL"), 6);
        Assert.Equal(15 * (1 + Production.LocalMaxMarkup), cities[3].LocalBaseOf("WOOL"), 6);
        // p0 and p2 sit at the same halved distance, so at the same price.
        Assert.Equal(cities[0].LocalBaseOf("WOOL"), cities[2].LocalBaseOf("WOOL"), 6);
    }

    [Fact]
    public void ACityIsItsOwnNearestProducer()
    {
        var cities = new List<City>
        {
            new() { Name = "a", Size = "medium", X = 0, Y = 0 },
            new() { Name = "b", Size = "medium", X = 1000, Y = 0 },
            new() { Name = "c", Size = "medium", X = 2000, Y = 0 },
        };
        cities[0].Produces.Add("WOOL");
        cities[1].Produces.Add("WOOL");
        cities[2].Produces.Add("WOOL");
        Assert.Equal(1000, Production.MeanNearestProducerDistance(cities, cities[0], "WOOL"), 6);
        Assert.Equal(2000.0 / 3, Production.MeanNearestProducerDistance(cities, cities[1], "WOOL"), 6);
        // Somewhere with no producers at all is not a distance question.
        Assert.Equal(0, Production.MeanNearestProducerDistance(cities, cities[0], "JEWELRY"), 6);
    }
}
