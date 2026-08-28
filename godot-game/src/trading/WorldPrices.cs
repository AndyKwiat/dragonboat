using System.Collections.Generic;

// What each product fetches across the whole world — the figure every
// percentage in the UI is measured against, so "▲ 163%" means dear compared
// with everywhere else today rather than compared with a constant in a data
// file.
//
// The average is WEIGHTED BY MARKET SIZE (Supply.WorldAverage): it is the
// price trade actually happens at, not the average of the signs in the
// windows. A large city moves a hundred times what a small one does and
// counts for a hundred times as much, so with the default city mix the big
// ports carry most of the weight and read near 100% while small remote ones
// swing hard.
//
// Walking every city's every product is far too much to do per frame, so it
// is a daily fixing: built at launch and refreshed at every midnight, which
// is roughly what a trader would actually have heard. It is an object rather
// than a static cache on purpose — it is constructed already populated, so
// there is no window in which it can quietly answer with base prices, and
// whoever holds it can see where it came from (Main hands it to the city
// popup and the map labels like any other system).
public sealed class WorldPrices
{
    private readonly Dictionary<string, double> average = new();

    public WorldPrices(List<City> cities, List<Product> products, MarketTerms terms) =>
        Refresh(cities, products, terms);

    public void Refresh(List<City> cities, List<Product> products, MarketTerms terms)
    {
        foreach (var p in products) average[p.Id] = Supply.WorldAverage(cities, p, terms);
    }

    // Never 0: a product nowhere stocks still has its base price, so callers
    // can divide by this without guarding.
    public double Of(Product p) =>
        average.TryGetValue(p.Id, out var v) && v > 0 ? v : p.BasePrice;
}
