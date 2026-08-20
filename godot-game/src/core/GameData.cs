using Godot;
using System.Collections.Generic;
using System.Text.Json;

// cities.json / products.json, loaded once at startup (see ../game/AGENTS.md
// for how they are generated). Prices are derived in Market.cs, never stored.
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
                if (c.TryGetProperty("products", out var prods))
                    foreach (var id in prods.EnumerateArray()) city.Products.Add(id.GetString() ?? "");
                Cities.Add(city);
            }
        }
    }
}
