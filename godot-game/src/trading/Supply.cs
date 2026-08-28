using System;
using System.Collections.Generic;

// The price engine. Every quote in the game comes out of two numbers held by
// the city: the product's LocalBase (what it settles at when nothing is out
// of the ordinary — see Production.SetLocalPrices) and its live Supply.
//
// Supply is stock relative to normal, clamped to ±MarketSize:
//   -MarketSize  a severe shortage — the price is at max, BasePrice × MaxOfBase
//    0           settled          — the price is the city's LocalBase
//   +MarketSize  a glut           — the price is LocalBase × GlutFactor
// Note the shortage ceiling is the world-wide base price scaled, NOT the
// city's local one: distance makes a place dear at rest, it does not raise
// what panic can charge there.
//
// This file is the arithmetic only — the curve, the caps, the ladder, the
// decay. Staging a trade against it (anchoring, refunds, cost basis, the
// slider's limits) is MarketDeal.cs; quoting it to the UI is Market.cs.
//
// The player is the main thing that moves supply — buying pushes it down
// (dearer), selling pushes it up (cheaper), one unit at a time, so you cannot
// buy a market out at its floor price. Story events shock it (StoryEvents),
// and every midnight its magnitude decays toward 0, standing in for all the
// other traders working the same coast (Supply.Decay).
public sealed record MarketTerms
{
    public int SmallSize, MediumSize, LargeSize;
    public double MaxOfBase;      // shortage ceiling, × the product's base price
    public double GlutFactor;     // glut floor, × the city's LocalBase
    public double BuyMarkup;      // what the player pays over what the city pays
    public double DecayPerDay;    // supply magnitude left after a midnight
    // The one good every city stocks whatever its size (rice).
    public string UniversalProductId = "";
}

public static class Supply
{
    public static int MarketSizeFor(string size, MarketTerms t) => size switch
    {
        "large" => t.LargeSize,
        "medium" => t.MediumSize,
        _ => t.SmallSize,
    };

    public static int Clamp(int supply, int size) => Math.Max(-size, Math.Min(size, supply));

    // A midnight's worth of other people's trading: the magnitude drops by a
    // tenth, truncated toward zero so a market actually reaches 0 instead of
    // creeping at it forever.
    public static int Decay(int supply, MarketTerms t) => (int)(supply * t.DecayPerDay);

    // The unrounded price at a stock level. Linear from the shortage ceiling
    // up through LocalBase at 0 and down to the glut floor.
    public static double PriceAt(double localBase, double basePrice, int supply, int size, MarketTerms t)
    {
        double r = size <= 0 ? 0 : (double)Math.Max(-size, Math.Min(size, supply)) / size;
        double max = basePrice * t.MaxOfBase;
        return r <= 0
            ? localBase + -r * (max - localBase)
            : localBase - r * localBase * (1 - t.GlutFactor);
    }

    // Both sides of a quote, always rounded UP so the house wins the rounding.
    public static int SellPrice(double price) => (int)Math.Ceiling(price);
    public static int BuyPrice(double price, MarketTerms t) => (int)Math.Ceiling(SellPrice(price) * t.BuyMarkup);

    // One unit's price is taken at the SCARCER of the two stock levels it
    // moves the market between — `lower` is that level. Using the same point
    // for both directions is what makes a bought unit sell back for no more
    // than it cost, so a slider that goes out and comes back is free.
    public static double UnitPriceAt(City city, Product p, int lower, MarketTerms t) =>
        PriceAt(city.LocalBaseOf(p.Id), p.BasePrice, lower, city.MarketSize, t);

    // What the market will part with / take on, before the hold or the purse
    // get a say: supply may never leave ±MarketSize.
    public static int MaxBuyableAt(int supply, int size) => supply + size;
    public static int MaxSellableAt(int supply, int size) => size - supply;

    public static int MaxBuyable(City city, string id) => MaxBuyableAt(city.SupplyOf(id), city.MarketSize);
    public static int MaxSellable(City city, string id) => MaxSellableAt(city.SupplyOf(id), city.MarketSize);

    // Walking the ladder. `units` bought moves supply from s0 down to
    // s0 - units, each unit priced one step lower than the last, so a big
    // order climbs its own price. Sells walk the other way.
    public static int BuyCost(City city, Product p, int s0, int units, MarketTerms t)
    {
        int total = 0;
        for (int i = 0; i < units; i++) total += BuyPrice(UnitPriceAt(city, p, s0 - i - 1, t), t);
        return total;
    }

    public static int SellRevenue(City city, Product p, int s0, int units, MarketTerms t)
    {
        int total = 0;
        for (int i = 0; i < units; i++) total += SellPrice(UnitPriceAt(city, p, s0 + i, t));
        return total;
    }

    // Signed money for a staged position: buying costs, selling pays.
    public static int CostOf(City city, Product p, int s0, int delta, MarketTerms t) =>
        delta >= 0 ? BuyCost(city, p, s0, delta, t) : -SellRevenue(city, p, s0, -delta, t);

    // What a product goes for across the whole world right now: the mean of
    // every stocking city's sell price, WEIGHTED BY MARKET SIZE — the average
    // price trade actually happens at, not the average of the signs in the
    // windows. A large city moves a hundred times what a small one does and
    // counts for a hundred times as much. Recomputed at midnight
    // and held by WorldPrices; it is what the market UI shows its
    // percentages against, so "120%" means dear compared to the rest of the
    // world today, not compared to a number in a data file.
    public static double WorldAverage(List<City> cities, Product p, MarketTerms t)
    {
        double paid = 0, weight = 0;
        foreach (var c in cities)
        {
            if (!c.Stocks(p.Id)) continue;
            double price = PriceAt(c.LocalBaseOf(p.Id), p.BasePrice, c.SupplyOf(p.Id), c.MarketSize, t);
            paid += SellPrice(price) * (double)c.MarketSize;
            weight += c.MarketSize;
        }
        return weight <= 0 ? p.BasePrice : paid / weight;
    }

    // How many units of the ladder `money` covers, never more than `cap`.
    public static int Affordable(City city, Product p, int s0, int cap, int money, MarketTerms t)
    {
        int n = 0, spent = 0;
        while (n < cap)
        {
            int next = spent + BuyPrice(UnitPriceAt(city, p, s0 - n - 1, t), t);
            if (next > money) break;
            spent = next; n++;
        }
        return n;
    }
}
