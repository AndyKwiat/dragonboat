using System.Collections.Generic;

// A city: placed on the coast by CityPlacer at launch (name, position,
// size, footprint), then given its trade — what it produces, what it stocks,
// and the two numbers behind every price it quotes — by Production.Deal /
// Production.SetLocalPrices (via GameData.SetCities).
public sealed class City
{
    public string Name = "";
    public float X, Y;                        // world units (cells), footprint centre, y-down
    public string Size = "small";             // small | medium | large
    public int Cells;                         // footprint side length in cells
    // What the market stocks, in display order. Small cities stock rice plus
    // the staple they produce; medium and large ones stock everything.
    public List<string> Products = new();
    // What the city itself makes — the goods whose producer marker shows in
    // the market, and the anchors every city's LocalBase is measured against.
    public List<string> Produces = new();
    // How many units the market can absorb or give up, by size
    // (Supply.MarketSizeFor); set with the products.
    public int MarketSize;
    // Live stock level per stocked product, -MarketSize..+MarketSize:
    // negative is a shortage (dear), positive a surplus (cheap). Starts at 0,
    // moves only when someone trades or an event shocks it, and decays back
    // toward 0 every midnight (Supply.Decay).
    public Dictionary<string, int> Supply = new();
    // What this city charges for a product at neutral supply: base price
    // marked up by how far away the nearest producers are
    // (Production.SetLocalPrices). Fixed for the run.
    public Dictionary<string, double> LocalBase = new();
    // Runtime state: set forever once the player opens the city's popup;
    // gates the price intel shown under its map label.
    public bool Discovered;

    public int SupplyOf(string id) => Supply.TryGetValue(id, out var n) ? n : 0;
    public double LocalBaseOf(string id) => LocalBase.TryGetValue(id, out var p) ? p : 0;
    public bool Stocks(string id) => Products.Contains(id);
    public bool ProducesId(string id) => Produces.Contains(id);
}
