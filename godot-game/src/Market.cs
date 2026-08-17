using System;
using System.Collections.Generic;

// City markets: prices derived from basePrice × size multiplier × local
// override, always rounded UP so the house wins the rounding.
public sealed record Quote(Product Product, int SellPrice, int BuyPrice, double PriceMultiplier);

public static class Market
{
    // Doubles throughout, in the same order as the JS, so the rounding matches.
    public static double SizeMultiplier(string size) => Config.MarketSizeMultiplier.TryGetValue(size, out var m) ? m : 1.0;

    public static double OverrideFor(City city, string id) => city.PriceOverrides.TryGetValue(id, out var o) ? o : 1.0;

    public static int SellPriceFor(Product p, string size, double o = 1.0) =>
        (int)Math.Ceiling(p.BasePrice * SizeMultiplier(size) * o);

    public static int BuyPriceFor(Product p, string size, double o = 1.0) =>
        (int)Math.Ceiling(SellPriceFor(p, size, o) * Config.MarketBuyMarkup);

    public static Quote QuoteFor(Product p, string size, double o = 1.0) =>
        new Quote(p, SellPriceFor(p, size, o), BuyPriceFor(p, size, o), o);

    // The city's market in cities.json order; unknown ids are skipped.
    public static List<Quote> MarketFor(City city)
    {
        var quotes = new List<Quote>();
        foreach (var id in city.Products)
        {
            var product = GameData.ProductById(id);
            if (product != null) quotes.Add(QuoteFor(product, city.Size, OverrideFor(city, id)));
        }
        return quotes;
    }
}
