using System;
using System.Collections.Generic;

// City markets: prices derived from basePrice × a multiplier, always rounded
// UP so the house wins the rounding. A live price story event supplies the
// multiplier outright (and rides along on the quote so the UI can show its
// expiry); otherwise the city's structural discount applies.
public sealed record Quote(Product Product, int SellPrice, int BuyPrice, ProductPriceStoryEvent? Event = null);

public static class Market
{
    // Small cities discount their one non-rice staple; medium cities discount
    // their one luxury. Everything else trades at base price.
    public static double MultiplierFor(City city, Product p) =>
        city.Size == "small" && p.Id != "RICE" ? Config.MarketSmallStapleDiscount :
        city.Size == "medium" && Config.LuxuryProductIds.Contains(p.Id) ? Config.MarketMediumLuxuryDiscount :
        1.0;

    public static int SellPriceFor(Product p, double multiplier = 1.0) =>
        (int)Math.Ceiling(p.BasePrice * multiplier);

    public static int BuyPriceFor(Product p, double multiplier = 1.0) =>
        (int)Math.Ceiling(SellPriceFor(p, multiplier) * Config.MarketBuyMarkup);

    public static Quote QuoteFor(City city, Product p, ProductPriceStoryEvent? ev = null)
    {
        var m = ev?.PriceMultiplier ?? MultiplierFor(city, p);
        return new Quote(p, SellPriceFor(p, m), BuyPriceFor(p, m), ev);
    }

    // The city's market in cities.json order; unknown ids are skipped.
    public static List<Quote> MarketFor(City city, StoryEvents? events = null)
    {
        var quotes = new List<Quote>();
        foreach (var id in city.Products)
        {
            var product = GameData.ProductById(id);
            if (product != null) quotes.Add(QuoteFor(city, product, events?.PriceEventFor(city, id)));
        }
        return quotes;
    }
}
