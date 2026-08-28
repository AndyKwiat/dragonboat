using System;
using System.Collections.Generic;

// The player's purse and cargo. Money never goes below 0 (buys are capped by
// the purse, wages stop at zero); a product count never goes below 0 and the
// hold never holds more than the crew can carry.
//
// The hold also knows what it cost (see `basis`). Trading in a market goes
// through MarketDeal, which calls ApplyStaged here — this class holds the
// cargo and the money but none of the market arithmetic.
public sealed class Player : ITrader
{
    // A property, not a field, so ITrader can name it — the market's deal
    // layer reads the purse through that interface.
    public int Money { get; set; }
    // Goodwill earned by giving goods away (GiveAway). Starts at 0 and,
    // unlike money, may go negative.
    public int Honour;
    // Standing with the monasteries: alms and monk errands earn it, a monk
    // left too long loses it (src/religion). Starts at 0, may go negative.
    public int Piety;
    public int CrewCount;
    public readonly Dictionary<string, int> Inventory = new();
    // What the units currently aboard cost, per product — the running total
    // paid for them, so the hold knows its own book value. Units leave at the
    // average price they went on at (Consume), so this stays proportional to
    // the count however the cargo was accumulated, and a sale can be shown as
    // a profit or a loss rather than just a pile of coins (AverageCost).
    private readonly Dictionary<string, double> basis = new();
    // VIP passengers aboard, each bound for a city and stamped with the day
    // they came aboard. They take no cargo space, but the reward for landing
    // one decays by the day and they jump ship if kept too long (Vips).
    public readonly List<VipPassenger> VipPassengers = new();

    public Player()
    {
        Money = Config.StartingMoney;
        CrewCount = Crew.Clamp(Config.Crew.Starting, Config.Crew);
        // The hold you start with is booked at base price — you did not buy
        // it anywhere, so there is no better figure to call it in at.
        foreach (var kv in Config.StartingInventory)
            if (kv.Value > 0)
            {
                Inventory[kv.Key] = kv.Value;
                basis[kv.Key] = kv.Value * (GameData.ProductById(kv.Key)?.BasePrice ?? 0);
            }
    }

    public int Held(string id) => Inventory.TryGetValue(id, out var n) ? n : 0;

    // What was paid for everything of this product aboard, and what that
    // works out at per unit — the number to hold a sell price against.
    public double BasisOf(string id) => basis.TryGetValue(id, out var v) ? v : 0;

    public double AverageCost(string id)
    {
        int n = Held(id);
        return n <= 0 ? 0 : BasisOf(id) / n;
    }

    // What the whole hold cost, for the purse line.
    public int CargoValue()
    {
        double total = 0;
        foreach (var v in basis.Values) total += v;
        return (int)Math.Round(total);
    }

    private void SetBasis(string id, double value)
    {
        if (Held(id) <= 0 || value <= 0) basis.Remove(id); else basis[id] = value;
    }

    public int TotalUnits()
    {
        int total = 0;
        foreach (var n in Inventory.Values) total += n;
        return total;
    }

    public bool CanChangeCrew(int delta) => Crew.ChangeAllowed(CrewCount, delta, TotalUnits(), Config.Crew);

    public int ChangeCrew(int delta)
    {
        if (CanChangeCrew(delta)) CrewCount = Crew.Clamp(CrewCount + delta, Config.Crew);
        return CrewCount;
    }

    public int CargoCapacity() => Crew.CargoCapacity(CrewCount, Config.Crew);
    public int CargoSpace() => Math.Max(0, CargoCapacity() - TotalUnits());

    // Capped by the hold AND the purse — money can't go negative.
    public int Buy(string id, int qty, int unitPrice)
    {
        int n = Math.Min(Math.Max(0, qty), CargoSpace());
        if (unitPrice > 0) n = Math.Min(n, Money / unitPrice);
        if (n <= 0) return 0;
        Money -= n * unitPrice;
        Inventory[id] = Held(id) + n;
        SetBasis(id, BasisOf(id) + n * (double)unitPrice);
        return n;
    }

    // Move a staged market position from oldDelta to newDelta units relative
    // to some baseline (positive = bought, negative = sold), where costOld and
    // costNew are what the city's price ladder says each position costs —
    // signed, so a sale is negative (Supply.CostOf). Only the difference
    // changes hands, and both ends are priced off the SAME ladder anchored at
    // the stock level the market was at when the row opened, so sliding a deal
    // back to zero refunds it to the coin: removing a buy is not a sell, and
    // the buy/sell markup never bites while you are still making up your mind.
    // The caller keeps newDelta within the hold, the purse and the market.
    // ITrader: apply one step of a staged market deal. MarketDeal works out
    // what the step is worth (it owns the anchoring that makes sliding out
    // and back exact); all that happens here is that the units, the money and
    // the hold's book value move to what it says.
    public void ApplyStaged(string id, int units, int cost, double basis)
    {
        if (units > 0) Inventory[id] = Held(id) + units;
        else if (units < 0) Consume(id, -units);
        Money -= cost;
        SetBasis(id, basis);
    }

    // Units always leave at the average price they went on at, so whatever
    // takes them — a sale, a gift, the crew's dinner — the book value of
    // what's left stays true.
    public int Consume(string id, int qty)
    {
        int n = Math.Min(Math.Max(0, qty), Held(id));
        if (n == 0) return 0;
        int had = Held(id);
        int left = had - n;
        if (left == 0) Inventory.Remove(id); else Inventory[id] = left;
        SetBasis(id, left == 0 ? 0 : BasisOf(id) * left / had);
        return n;
    }

    // What a call at the destination paid: how many stepped ashore, the
    // money, the flat honour per head and the speed bonus on top.
    public readonly record struct VipDelivery(int Count, int Money, int Honour, int SpeedHonour)
    {
        public int TotalHonour => Honour + SpeedHonour;
    }

    // Everyone aboard bound for this city steps ashore: money and honour per
    // head, plus a bonus that shrinks with every day they spent aboard
    // (Vips.SpeedHonourFor), so a fast passage is worth far more than a slow
    // one. `today` is the clock's day index (Days.Of).
    public VipDelivery DeliverVips(City city, int today)
    {
        int speed = 0, n = 0;
        for (int i = VipPassengers.Count - 1; i >= 0; i--)
        {
            if (VipPassengers[i].Destination != city) continue;
            speed += Vips.SpeedHonourFor(VipPassengers[i].DaysAboard(today), Config.Vips);
            VipPassengers.RemoveAt(i);
            n++;
        }
        var paid = new VipDelivery(n, n * Config.VipDeliveryMoney, n * Config.VipDeliveryHonour, speed);
        Money += paid.Money;
        Honour += paid.TotalHonour;
        return paid;
    }

    // Donate everything held of a product: no money back, one honour point
    // per ten units given (rounded down, but never less than 1).
    public static int GiveAwayHonour(int units) => units <= 0 ? 0 : Math.Max(1, units / 10);

    public int GiveAway(string id)
    {
        int n = Consume(id, Held(id));
        Honour += GiveAwayHonour(n);
        return n;
    }

    public int Sell(string id, int qty, int unitPrice)
    {
        int n = Consume(id, qty);
        Money += n * unitPrice;
        return n;
    }
}
