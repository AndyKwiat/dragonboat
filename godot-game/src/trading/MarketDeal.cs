using System;

// One product's staged position in one city's market — the thing the popup's
// trade slider moves, and the only place the subtle part of trading lives.
//
// A deal is ANCHORED: it remembers the market's stock level, the units
// aboard and what they cost at the moment it opened, and prices every
// position off that anchor rather than accumulating leg by leg. That is what
// makes the slider safe to drag:
//
//   * moving to N units and back to 0 returns the money to the coin and the
//     hold's book value to exactly where it started — undoing a buy is not a
//     sell, so the buy/sell markup never bites while you are still deciding;
//   * every unit is still priced one step further along the city's supply
//     ladder (Supply.BuyCost / SellRevenue), so a big order moves the price
//     against itself as it fills and a market cannot be bought out at its
//     floor price;
//   * a sale knows what its units cost to get aboard, so it can be shown as
//     a profit or a loss rather than just a pile of coins.
//
// The anchor is only good while the row is open. Anything that changes the
// hold behind its back (the crew eating the rice, a gift, the midnight
// decay moving the market) means the rows must be rebuilt — see
// CityPopup.Rebuild and Main.RefreshMarkets.
public sealed class MarketDeal
{
    public readonly City City;
    public readonly Product Product;
    private readonly ITrader trader;
    private readonly MarketTerms terms;
    private readonly int supply0;     // the market's stock when the deal opened
    private readonly int held0;       // units aboard then
    private readonly double basis0;   // what those units had cost

    // The staged position: positive is units being bought, negative sold.
    public int Units { get; private set; }

    public MarketDeal(City city, Product product, ITrader trader, MarketTerms terms)
    {
        City = city;
        Product = product;
        this.trader = trader;
        this.terms = terms;
        supply0 = city.SupplyOf(product.Id);
        held0 = trader.Held(product.Id);
        basis0 = trader.BasisOf(product.Id);
    }

    // Signed money for a position: a buy costs, a sale pays.
    public int CostOf(int units) => Supply.CostOf(City, Product, supply0, units, terms);
    public int Cost => CostOf(Units);

    // The purse and the hold as they stood before this deal was staged —
    // every limit is worked out as if it were first undone, so sliding a row
    // back and forth never eats into what the other rows can do.
    private int PurseBefore => trader.Money + Cost;
    private int SpaceBefore => trader.CargoSpace() + Units;

    // The most that fits, that the purse covers walking the price ladder,
    // AND that the market can part with without going past -MarketSize.
    public int MaxBuy() => Supply.Affordable(City, Product, supply0,
        Math.Min(SpaceBefore, Supply.MaxBuyableAt(supply0, City.MarketSize)), PurseBefore, terms);

    // What's aboard, capped by what the city can absorb before it is glutted.
    public int MaxSell() => Math.Min(held0, Supply.MaxSellableAt(supply0, City.MarketSize));

    // What the units in a staged sale had cost to get aboard, at the hold's
    // average price for the product when the deal opened.
    public int CostOfSold => Units >= 0 || held0 <= 0 ? 0 : (int)Math.Round(basis0 * -Units / held0);

    // Revenue minus that. Zero unless something is being sold.
    public int Profit => Units >= 0 ? 0 : -Cost - CostOfSold;

    // What the hold's book value becomes at a position: a buy adds what it
    // cost, a sale keeps the untouched units' share of what the whole lot
    // cost. Rebuilt from the anchor, never accumulated — that is the reason
    // a round trip is exact.
    private double BasisAt(int units) => units >= 0
        ? basis0 + CostOf(units)
        : held0 > 0 ? basis0 * (held0 + units) / held0 : 0;

    // Move the staged position, clamped to what is actually possible. The
    // trader and the city are both updated; returns false if nothing moved.
    public bool MoveTo(int units)
    {
        units = Math.Max(-MaxSell(), Math.Min(MaxBuy(), units));
        if (units == Units) return false;
        trader.ApplyStaged(Product.Id, units - Units, CostOf(units) - Cost, BasisAt(units));
        Units = units;
        City.Supply[Product.Id] = supply0 - units;
        return true;
    }

    // Slider glue: -1..+1 spans sell-everything to buy-the-most, with 0 in
    // the middle meaning no deal.
    public int UnitsAt(double fraction) => fraction >= 0
        ? (int)Math.Round(fraction * MaxBuy())
        : -(int)Math.Round(-fraction * MaxSell());

    // Where the knob belongs for the position actually dealt — the maxima
    // shift as other rows are slid around, so the knob is snapped to this
    // when a drag ends rather than left where the mouse was.
    public double Fraction
    {
        get
        {
            if (Units == 0) return 0;
            int max = Units > 0 ? MaxBuy() : MaxSell();
            return max > 0 ? (double)Math.Abs(Units) / max * Math.Sign(Units) : 0;
        }
    }
}

// What a MarketDeal needs of whoever is trading. Player implements it; the
// tests use a stand-in, which is the point — none of the arithmetic above
// needs a Godot type or a real game to exercise.
public interface ITrader
{
    int Money { get; }
    int CargoSpace();
    int Held(string id);
    double BasisOf(string id);
    // Apply one staged move: `units` change hands (positive aboard), `cost`
    // leaves the purse (negative pays in), and the hold's book value for the
    // product is set outright to `basis`.
    void ApplyStaged(string id, int units, int cost, double basis);
}
