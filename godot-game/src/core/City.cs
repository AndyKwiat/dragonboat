using System.Collections.Generic;

// A city: placed on the coast by CityPlacer at launch (name, position,
// size, footprint), then dealt its market by GameData.DealProducts.
public sealed class City
{
    public string Name = "";
    public float X, Y;                        // world units (cells), footprint centre, y-down
    public string Size = "small";             // small | medium | large
    public int Cells;                         // footprint side length in cells
    public List<string> Products = new();
    // The city's always-cheaper goods, dealt with the products each run.
    public List<string> Specialities = new();
    // Runtime state: set forever once the player opens the city's popup;
    // gates the price intel shown under its map label.
    public bool Discovered;
}
