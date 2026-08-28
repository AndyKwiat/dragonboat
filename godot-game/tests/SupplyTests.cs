using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

// The price engine and what is quoted off it: the curve, the caps, the
// per-unit ladder, the daily decay, the world average (src/trading/Supply.cs,
// WorldPrices.cs) and the quotes and readings the UI draws from them
// (Market.cs). Staging a trade against all this is MarketDealTests.
public class SupplyTests
{
    private static MarketTerms Terms => Economy.Terms;
    // Named Stall, not Market — `Market` is the class under test here.
    private static City Stall(string size, string id, double localBase) => Economy.Market(size, id, localBase);

    [Fact]
    public void SettlesAtLocalBaseAndSwingsToTheEnds()
    {
        var p = new Product { Id = "JEWELRY", BasePrice = 60 };
        var city = Stall("small", p.Id, 90);            // remote: settles at 1.5x base
        Assert.Equal(90, Supply.PriceAt(90, 60, 0, 20, Terms), 6);
        Assert.Equal(150, Supply.PriceAt(90, 60, -20, 20, Terms), 6);   // 2.5 x the WORLD base
        Assert.Equal(72, Supply.PriceAt(90, 60, 20, 20, Terms), 6);     // 0.8 x the LOCAL price
        // The shortage ceiling is the same everywhere, however remote.
        Assert.Equal(150, Supply.PriceAt(60, 60, -20, 20, Terms), 6);
        Assert.Equal(60, city.MarketSize);
    }

    [Fact]
    public void PriceFallsAsStockRises()
    {
        double last = double.MaxValue;
        for (int s = -200; s <= 200; s += 5)
        {
            double now = Supply.PriceAt(75, 50, s, 200, Terms);
            Assert.True(now < last, $"price did not fall at supply {s}");
            last = now;
        }
    }

    [Fact]
    public void PriceIsClampedOutsideTheMarketSize()
    {
        Assert.Equal(Supply.PriceAt(90, 60, -20, 20, Terms), Supply.PriceAt(90, 60, -999, 20, Terms), 6);
        Assert.Equal(Supply.PriceAt(90, 60, 20, 20, Terms), Supply.PriceAt(90, 60, 999, 20, Terms), 6);
    }

    [Fact]
    public void TheHouseWinsTheRounding()
    {
        // sell price rounds up, and the buy price marks that up again.
        Assert.Equal(91, Supply.SellPrice(90.01));
        Assert.Equal(101, Supply.BuyPrice(90.01, Terms));
        Assert.True(Supply.BuyPrice(90, Terms) > Supply.SellPrice(90));
    }

    // -- decay -------------------------------------------------------------

    [Fact]
    public void SupplyDecaysToZeroFromBothSides()
    {
        foreach (int start in new[] { 2000, -2000, 7, -7, 1, -1 })
        {
            int s = start, days = 0;
            while (s != 0)
            {
                int next = Supply.Decay(s, Terms);
                Assert.True(Math.Abs(next) < Math.Abs(s), $"decay stalled at {s}");
                Assert.True(next == 0 || Math.Sign(next) == Math.Sign(s), "decay overshot zero");
                s = next;
                Assert.True(++days < 500, "decay never reached zero");
            }
        }
        Assert.Equal(0, Supply.Decay(0, Terms));
        Assert.Equal(1800, Supply.Decay(2000, Terms));
    }

    // -- the caps ----------------------------------------------------------

    [Fact]
    public void TheMarketCannotBeDrainedOrGluttedPastItsSize()
    {
        var city = Stall("small", "WOOL", 15);
        Assert.Equal(60, Supply.MaxBuyable(city, "WOOL"));
        Assert.Equal(60, Supply.MaxSellable(city, "WOOL"));
        city.Supply["WOOL"] = -59;
        Assert.Equal(1, Supply.MaxBuyable(city, "WOOL"));    // one unit left in the place
        Assert.Equal(119, Supply.MaxSellable(city, "WOOL"));
        city.Supply["WOOL"] = 60;
        Assert.Equal(120, Supply.MaxBuyable(city, "WOOL"));
        Assert.Equal(0, Supply.MaxSellable(city, "WOOL"));   // it will not take another bale
    }

    // -- the ladder --------------------------------------------------------

    [Fact]
    public void EveryUnitIsPricedOneStepFurtherAlong()
    {
        var p = new Product { Id = "WOOL", BasePrice = 15 };
        var city = Stall("small", p.Id, 15);
        // Buying the whole market out costs strictly more per unit as it goes.
        int prev = 0;
        for (int n = 1; n <= 60; n++)
        {
            int cost = Supply.BuyCost(city, p, 0, n, Terms);
            Assert.True(cost - prev >= 0, "a unit was free");
            if (n > 1) Assert.True(cost - prev >= Supply.BuyCost(city, p, 0, n - 1, Terms) - Supply.BuyCost(city, p, 0, n - 2, Terms));
            prev = cost;
        }
        // The last unit before the market is empty costs the ceiling price.
        Assert.Equal(Supply.BuyPrice(15 * 2.5, Terms), Supply.BuyCost(city, p, 0, 60, Terms) - Supply.BuyCost(city, p, 0, 59, Terms));
    }

    [Fact]
    public void BuyingThenSellingBackNeverProfits()
    {
        var p = new Product { Id = "JEWELRY", BasePrice = 60 };
        var city = Stall("medium", p.Id, 84);
        for (int s0 = -150; s0 <= 150; s0 += 25)
            for (int n = 1; n <= 30; n++)
            {
                int paid = Supply.BuyCost(city, p, s0, n, Terms);
                int back = Supply.SellRevenue(city, p, s0 - n, n, Terms);
                Assert.True(back <= paid, $"round trip profited at s0={s0}, n={n}: paid {paid}, got {back}");
            }
    }

    [Fact]
    public void SellingThenBuyingBackNeverProfits()
    {
        var p = new Product { Id = "FISH", BasePrice = 20 };
        var city = Stall("medium", p.Id, 30);
        for (int s0 = -150; s0 <= 150; s0 += 25)
            for (int n = 1; n <= 30; n++)
            {
                int got = Supply.SellRevenue(city, p, s0, n, Terms);
                int paid = Supply.BuyCost(city, p, s0 + n, n, Terms);
                Assert.True(got <= paid, $"round trip profited at s0={s0}, n={n}: got {got}, paid back {paid}");
            }
    }

    // Restaging a position is priced off a ladder anchored where the market
    // stood when the row opened, so sliding out and back nets exactly zero.
    [Fact]
    public void CostOfAStagedPositionOnlyDependsOnTheAnchor()
    {
        var p = new Product { Id = "WOOD", BasePrice = 10 };
        var city = Stall("small", p.Id, 14);
        Assert.Equal(0, Supply.CostOf(city, p, 5, 0, Terms));
        for (int d = -20; d <= 20; d++)
        {
            int once = Supply.CostOf(city, p, 5, d, Terms);
            city.Supply[p.Id] = 5 - d;                     // as the slider leaves it
            Assert.Equal(once, Supply.CostOf(city, p, 5, d, Terms));
            Assert.Equal(d > 0, once > 0);
            Assert.Equal(d < 0, once < 0);
        }
    }

    [Fact]
    public void AffordableStopsAtThePurseAndTheCap()
    {
        var p = new Product { Id = "JEWELRY", BasePrice = 60 };
        var city = Stall("small", p.Id, 60);
        Assert.Equal(0, Supply.Affordable(city, p, 0, 60, 10, Terms));
        Assert.Equal(3, Supply.Affordable(city, p, 0, 3, 1_000_000, Terms));   // capped by the cap
        int n = Supply.Affordable(city, p, 0, 60, 500, Terms);
        Assert.True(Supply.BuyCost(city, p, 0, n, Terms) <= 500);
        Assert.True(Supply.BuyCost(city, p, 0, n + 1, Terms) > 500);
    }

    // -- the world average -------------------------------------------------

    [Fact]
    public void TheWorldAverageIsWeightedByMarketSize()
    {
        var p = new Product { Id = "WOOL", BasePrice = 15 };
        var small = Stall("small", p.Id, 10);     // 60 units of clout, price 10
        var large = Stall("large", p.Id, 20);     // 6000 units of clout, price 20
        var avg = Supply.WorldAverage(new List<City> { small, large }, p, Terms);
        // A straight mean would be 15; the large city moves a hundred times
        // the volume and drags it almost all the way to its own price.
        Assert.Equal((10 * 60.0 + 20 * 6000.0) / 6060, avg, 6);
        Assert.True(avg > 19.5);
    }

    [Fact]
    public void TheWorldAverageFollowsSupplyAndSkipsCitiesThatDoNotStockIt()
    {
        var p = new Product { Id = "FISH", BasePrice = 20 };
        var stocking = Stall("medium", p.Id, 20);
        var elsewhere = new City { Name = "none", Size = "medium", MarketSize = 600 };
        var cities = new List<City> { stocking, elsewhere };
        Assert.Equal(20, Supply.WorldAverage(cities, p, Terms), 6);
        stocking.Supply[p.Id] = -600;                       // the one market runs dry
        Assert.Equal(50, Supply.WorldAverage(cities, p, Terms), 6);   // 2.5 x base
        stocking.Supply[p.Id] = 600;                        // and is then glutted
        Assert.Equal(16, Supply.WorldAverage(cities, p, Terms), 6);   // 0.8 x local
        // Nowhere stocks it at all: the base price is the only figure left.
        Assert.Equal(20, Supply.WorldAverage(new List<City> { elsewhere }, p, Terms), 6);
    }


    // WorldPrices is the daily fixing of that average the whole UI quotes
    // against; it is built already populated so it can never quietly answer
    // with base prices.
    [Fact]
    public void WorldPricesArePopulatedOnConstruction()
    {
        var products = Economy.Products();
        var wool = products.Find(p => p.Id == "WOOL")!;
        var cities = new List<City> { Stall("large", "WOOL", 30) };
        var prices = new WorldPrices(cities, products, Terms);
        Assert.Equal(30, prices.Of(wool), 6);
        // Something nowhere stocks still answers with its base price, so
        // callers can divide by it without guarding.
        Assert.Equal(60, prices.Of(products.Find(p => p.Id == "JEWELRY")!), 6);

        cities[0].Supply["WOOL"] = -cities[0].MarketSize;
        Assert.Equal(30, prices.Of(wool), 6);       // stale until refreshed...
        prices.Refresh(cities, products, Terms);
        // 15 x 2.5 = 37.5, quoted (and so averaged) as the ceil'd 38 — the
        // average is over prices actually asked, not the raw curve.
        Assert.Equal(38, prices.Of(wool), 6);       // ...then it has moved
    }

    // Market.Read is the one definition of "how does this price compare with
    // the rest of the world" — the market panel and the map labels both go
    // through it, so a bug here would show up in both at once.
    [Fact]
    public void APriceReadsAgainstTheWorldAverage()
    {
        var dear = Market.Read(150, 100);
        Assert.Equal(150, dear.Percent);
        Assert.True(dear.Dear);
        Assert.True(dear.OffAverage);

        var cheap = Market.Read(80, 100);
        Assert.Equal(80, cheap.Percent);
        Assert.False(cheap.Dear);
        Assert.True(cheap.OffAverage);

        // Near enough the average to leave untinted.
        var level = Market.Read(100, 100);
        Assert.Equal(100, level.Percent);
        Assert.False(level.OffAverage);
        Assert.False(Market.Read(100, 100.4).OffAverage);

        // No average to speak of: reads as level rather than dividing by 0.
        var nothing = Market.Read(42, 0);
        Assert.Equal(100, nothing.Percent);
        Assert.False(nothing.OffAverage);
    }

    // A market's quote is the price of the NEXT unit each way, so the buy
    // side is always dearer and both move the moment stock changes.
    [Fact]
    public void AQuoteIsTheNextUnitEachWay()
    {
        var p = new Product { Id = "WOOL", Name = "Wool", BasePrice = 15 };
        var city = Stall("small", p.Id, 15);
        city.Produces.Add(p.Id);
        var before = Market.QuoteFor(city, p, Terms);
        Assert.True(before.BuyPrice > before.SellPrice, "the house did not take its markup");
        Assert.Equal(0, before.Supply);
        Assert.Equal(city.MarketSize, before.MarketSize);
        Assert.True(before.Produces);

        city.Supply[p.Id] = -city.MarketSize / 2;      // half drained
        var after = Market.QuoteFor(city, p, Terms);
        Assert.True(after.SellPrice > before.SellPrice, "a shortage did not raise the price");

        Assert.Single(Market.MarketFor(city, new List<Product> { p }, Terms));
    }

    [Fact]
    public void DecayAllWalksEveryMarketBackTowardNormal()
    {
        var p = new Product { Id = "WOOL", Name = "Wool", BasePrice = 15 };
        var a = Stall("small", p.Id, 15);
        var b = Stall("large", p.Id, 15);
        a.Supply[p.Id] = -a.MarketSize;
        b.Supply[p.Id] = b.MarketSize;
        Market.DecayAll(new List<City> { a, b }, Terms);
        Assert.Equal(Supply.Decay(-a.MarketSize, Terms), a.SupplyOf(p.Id));
        Assert.Equal(Supply.Decay(b.MarketSize, Terms), b.SupplyOf(p.Id));
        Assert.True(a.SupplyOf(p.Id) < 0 && b.SupplyOf(p.Id) > 0, "decay overshot zero");
    }
}
