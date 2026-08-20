using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// cities.json / products.json, loaded once at startup (see ../game/AGENTS.md
// for how they are generated). A city's name, size and location are fixed by
// the JSON; what it trades is dealt fresh every run (DealProducts). Prices
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
        DealProducts(new Random());
    }

    // Each run deals the specialities fresh (any products in cities.json are
    // ignored): large cities trade everything, a medium city every staple
    // plus one random luxury, a small city rice plus one other random staple.
    public static void DealProducts(Random rng)
    {
        var staples = new List<string>();
        var luxuries = new List<string>();
        foreach (var p in Products)
            (Config.LuxuryProductIds.Contains(p.Id) ? luxuries : staples).Add(p.Id);
        var otherStaples = staples.FindAll(id => id != "RICE");
        foreach (var city in Cities)
        {
            city.Products.Clear();
            switch (city.Size)
            {
                case "large":
                    foreach (var p in Products) city.Products.Add(p.Id);
                    break;
                case "medium":
                    city.Products.AddRange(staples);
                    city.Products.Add(luxuries[rng.Next(luxuries.Count)]);
                    break;
                default:
                    city.Products.Add("RICE");
                    city.Products.Add(otherStaples[rng.Next(otherStaples.Count)]);
                    break;
            }
        }
    }
}
