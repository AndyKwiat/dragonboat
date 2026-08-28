using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// products.json, loaded once at startup, and the run's cities — placed on
// the coast by CityPlacer (Main hands them over with SetCities) and given
// their trade here: what each produces and stocks (Production.Deal) and what
// distance from the nearest producers does to its prices
// (Production.SetLocalPrices). Live prices are derived in Supply.cs/Market.cs,
// never stored. Product itself lives in core/Product.cs, Godot-free.

public static class GameData
{
    public static List<Product> Products { get; private set; } = new();
    public static List<City> Cities { get; private set; } = new();
    private static readonly Dictionary<string, Product> ById = new();

    public static Product? ProductById(string id) => ById.TryGetValue(id, out var p) ? p : null;

    public static void Load()
    {
        using (var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Config.ProductsPath)))
        {
            Products = new List<Product>();
            foreach (var p in doc.RootElement.GetProperty("products").EnumerateArray())
            {
                Products.Add(new Product
                {
                    Id = p.GetProperty("id").GetString() ?? "",
                    Name = p.GetProperty("name").GetString() ?? "",
                    Description = p.GetProperty("description").GetString() ?? "",
                    BasePrice = p.GetProperty("basePrice").GetDouble(),
                    IsLuxury = (p.GetProperty("class").GetString() ?? "") == "luxury",
                });
            }
        }
        ById.Clear();
        foreach (var p in Products) ById[p.Id] = p;

    }

    // The run's freshly placed cities: production is dealt, then distance is
    // priced. Returns everything wrong with the result (Production.Problems)
    // — Main dies loudly on a non-empty list rather than sailing into a world
    // with no economy. Pricing runs before the check so the check can see the
    // settled prices too; it copes with a world too small to price properly.
    public static List<string> SetCities(List<City> cities, Random rng, float mapWidth)
    {
        Cities = cities;
        Production.Deal(cities, Products, rng, Config.Market);
        Production.SetLocalPrices(cities, Products, mapWidth);
        return Production.Problems(cities, Products);
    }
}
