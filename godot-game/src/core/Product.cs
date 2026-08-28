// One tradeable good, loaded from data/products.json by GameData.Load. Split
// out of GameData so the rules files (and tests/) can see it without Godot.
// BasePrice is the world-wide anchor: it sets the ceiling every city's
// shortage price climbs to, and the floor its LocalBasePrice starts from
// (src/trading/Production.cs, src/trading/Supply.cs).
public sealed class Product
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public double BasePrice;
    // "luxury" in the data file; the rest are staples. Which class a product
    // is decides who produces it: small cities make one staple, medium one of
    // each, large two luxuries (Production.Deal).
    public bool IsLuxury;
}
