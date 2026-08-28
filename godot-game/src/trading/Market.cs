using System.Collections.Generic;

// City markets as the UI reads them: one Quote per stocked product, plus the
// one way of saying how a price compares with the rest of the world
// (PriceReading), shared by the market panel and the map labels so they can
// never disagree.
//
// Every price comes from Supply.cs — the city's LocalBase for the product,
// bent by how much stock the market is holding — and is always rounded UP so
// the house wins the rounding. Nothing here is stored; a quote is the price
// of the NEXT single unit and moves as soon as one changes hands. The three
// files, from the bottom up: Supply.cs is the arithmetic, MarketDeal.cs
// stages a trade against it, Market.cs presents it. None of the three know
// about GameData or Godot — the product catalogue is handed in.
// SellPrice is what the city pays for the next unit, BuyPrice what it wants
// for it; Supply/MarketSize are the stock level behind both, and Produces is
// whether this is one of the city's own goods.
public sealed record Quote(Product Product, int SellPrice, int BuyPrice, int Supply, int MarketSize, bool Produces);

public static class Market
{
    // The price of the next unit the player would BUY: it moves the market
    // one step scarcer, so it is quoted at that lower step (Supply.UnitPriceAt).
    public static double NextBuyLevel(City city, Product p, MarketTerms t) =>
        Supply.UnitPriceAt(city, p, city.SupplyOf(p.Id) - 1, t);

    // The price of the next unit the player would SELL: it is quoted at the
    // level the market is at now, which is the scarcer of the two it moves
    // between.
    public static double NextSellLevel(City city, Product p, MarketTerms t) =>
        Supply.UnitPriceAt(city, p, city.SupplyOf(p.Id), t);

    public static Quote QuoteFor(City city, Product p, MarketTerms t) => new(
        p,
        Supply.SellPrice(NextSellLevel(city, p, t)),
        Supply.BuyPrice(NextBuyLevel(city, p, t), t),
        city.SupplyOf(p.Id),
        city.MarketSize,
        city.ProducesId(p.Id));

    // The city's stocked products in their dealt order; unknown ids skipped.
    // The catalogue is passed in rather than reached for, so nothing in this
    // folder has to know about GameData (and so this file stays testable).
    public static List<Quote> MarketFor(City city, IReadOnlyList<Product> catalogue, MarketTerms t)
    {
        var quotes = new List<Quote>();
        foreach (var id in city.Products)
        {
            var product = Find(catalogue, id);
            if (product != null) quotes.Add(QuoteFor(city, product, t));
        }
        return quotes;
    }

    public static Product? Find(IReadOnlyList<Product> catalogue, string id)
    {
        foreach (var p in catalogue) if (p.Id == id) return p;
        return null;
    }

    // How a price reads against what the product fetches across the world
    // (WorldPrices.Of): the percentage the UI prints, and whether that is
    // dear, cheap, or near enough the average to leave untinted. One
    // definition, so the market panel and the map labels always agree.
    public readonly record struct PriceReading(double WorldAverage, int Percent, bool Dear, bool OffAverage);

    public static PriceReading Read(int sellPrice, double worldAverage)
    {
        double world = worldAverage > 0 ? worldAverage : sellPrice;
        int percent = (int)System.Math.Round(sellPrice / world * 100);
        return new PriceReading(world, percent, sellPrice > world, percent != 100);
    }

    // Every stocked market's supply decays a step toward 0 — the rest of the
    // coast's traders, quietly restocking and drawing down. Runs at midnight.
    public static void DecayAll(List<City> cities, MarketTerms t)
    {
        foreach (var city in cities)
            foreach (var id in new List<string>(city.Supply.Keys))
                city.Supply[id] = Supply.Decay(city.Supply[id], t);
    }
}
