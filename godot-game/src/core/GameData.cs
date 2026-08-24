using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// cities.json / products.json, loaded once at startup.
// A city's name and location are fixed by the JSON; the sizes are shuffled
// between the cities every run (DealSizes — same counts, different spots)
// and what each trades is dealt fresh from its size (DealProducts). Prices
// are derived in Market.cs, never stored.
public sealed class Product
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public double BasePrice;
}

public sealed class City
{
    public string Name = "";
    public float X, Y;                        // world units, y-down from the top-left
    public string Size = "small";             // small | medium | large
    public List<string> Products = new();
    // The city's always-cheaper goods, dealt with the products each run.
    public List<string> Specialities = new();
    // Runtime state, not from JSON: set forever once the player opens the
    // city's popup; gates the price intel shown under its map label.
    public bool Discovered;
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

        using (var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Config.CitiesPath)))
        {
            Cities = new List<City>();
            foreach (var c in doc.RootElement.GetProperty("cities").EnumerateArray())
            {
                var city = new City
                {
                    Name = c.GetProperty("name").GetString() ?? "",
                    X = c.GetProperty("location").GetProperty("x").GetSingle(),
                    Y = c.GetProperty("location").GetProperty("y").GetSingle(),
                    Size = c.GetProperty("size").GetString() ?? "small",
                };
                Cities.Add(city);
            }
        }
        var rng = new Random();
        DealSizes(rng);
        DealProducts(rng);
    }

    // Shuffle the JSON sizes between the cities, so each run the large,
    // medium and small cities land in different spots but the mix of the
    // world stays the same.
    public static void DealSizes(Random rng)
    {
        var sizes = Cities.ConvertAll(c => c.Size);
        for (int i = sizes.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (sizes[i], sizes[j]) = (sizes[j], sizes[i]);
        }
        for (int i = 0; i < Cities.Count; i++) Cities[i].Size = sizes[i];
    }

    // Each run deals the markets fresh (any products in cities.json are
    // ignored): medium and large cities trade everything, a small city rice
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
