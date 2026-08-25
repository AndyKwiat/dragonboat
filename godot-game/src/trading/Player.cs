using System;
using System.Collections.Generic;

// The player's purse and cargo. Money never goes below 0 (buys are capped by
// the purse, wages stop at zero); a product count never goes below 0 and the
// hold never holds more than the crew can carry.
public sealed class Player
{
    public int Money;
    // Goodwill earned by giving goods away (GiveAway). Starts at 0 and,
    // unlike money, may go negative.
    public int Honour;
    public int CrewCount;
    public readonly Dictionary<string, int> Inventory = new();
    // VIP passengers aboard, each bound for a city and stamped with the day
    // they came aboard. They take no cargo space, but the reward for landing
    // one decays by the day and they jump ship if kept too long (Vips).
    public readonly List<VipPassenger> VipPassengers = new();

    public Player()
    {
        Money = Config.StartingMoney;
        CrewCount = Crew.Clamp(Config.Crew.Starting, Config.Crew);
        foreach (var kv in Config.StartingInventory) if (kv.Value > 0) Inventory[kv.Key] = kv.Value;
    }

    public int Held(string id) => Inventory.TryGetValue(id, out var n) ? n : 0;

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
        return n;
    }

    // Move a staged market position from oldDelta to newDelta units relative
    // to some baseline (positive = bought at buyPrice, negative = sold at
    // sellPrice). The old leg is undone at the exact price it was struck, so
    // sliding a deal back to zero refunds it to the coin — removing a buy is
    // not a sell. The caller keeps newDelta within the hold and the purse.
    public void Restage(string id, int oldDelta, int newDelta, int buyPrice, int sellPrice)
    {
        if (oldDelta > 0) { Consume(id, oldDelta); Money += oldDelta * buyPrice; }
        else if (oldDelta < 0) { Inventory[id] = Held(id) - oldDelta; Money += oldDelta * sellPrice; }
        if (newDelta > 0) { Inventory[id] = Held(id) + newDelta; Money -= newDelta * buyPrice; }
        else if (newDelta < 0) { Consume(id, -newDelta); Money -= newDelta * sellPrice; }
    }

    public int Consume(string id, int qty)
    {
        int n = Math.Min(Math.Max(0, qty), Held(id));
        if (n == 0) return 0;
        int left = Held(id) - n;
        if (left == 0) Inventory.Remove(id); else Inventory[id] = left;
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
