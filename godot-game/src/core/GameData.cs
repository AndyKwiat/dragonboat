using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// products.json, loaded once at startup, and the run's cities — placed on
// the coast by CityPlacer (Main hands them over with SetCities) and dealt
// their markets here from their size (DealProducts). Prices are derived in
// Market.cs, never stored.
public sealed class Product
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public double BasePrice;
}

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
                });
            }
        }
        ById.Clear();
        foreach (var p in Products) ById[p.Id] = p;

    }

    // The run's freshly placed cities; deals each its market.
    public static void SetCities(List<City> cities, Random rng)
    {
        Cities = cities;
        DealProducts(rng);
    }

    // Each run deals the markets fresh: medium and large cities trade everything, a small city rice
    // plus one other random staple. Specialities — always-cheaper goods
    // (Market.MultiplierFor) — are dealt too: a small city's other staple,
    // one random product for a medium city, two for a large.
    public static void DealProducts(Random rng)
    {
        var staples = new List<string>();
        foreach (var p in Products)
            if (!Config.LuxuryProductIds.Contains(p.Id)) staples.Add(p.Id);
        var otherStaples = staples.FindAll(id => id != "RICE");
        foreach (var city in Cities)
        {
            city.Products.Clear();
            city.Specialities.Clear();
            switch (city.Size)
            {
                case "large":
                case "medium":
                    foreach (var p in Products) city.Products.Add(p.Id);
                    int count = city.Size == "large" ? 2 : 1;
                    while (city.Specialities.Count < count)
                    {
                        var pick = Products[rng.Next(Products.Count)].Id;
                        if (!city.Specialities.Contains(pick)) city.Specialities.Add(pick);
                    }
                    break;
                default:
                    var staple = otherStaples[rng.Next(otherStaples.Count)];
                    city.Products.Add("RICE");
                    city.Products.Add(staple);
                    city.Specialities.Add(staple);
                    break;
            }
        }
    }
}
