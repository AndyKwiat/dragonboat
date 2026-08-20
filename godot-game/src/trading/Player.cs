using System;
using System.Collections.Generic;

// The player's purse and cargo. Money never goes below 0 (buys are capped by
// the purse, wages stop at zero); a product count never goes below 0 and the
// hold never holds more than the crew can carry.
public sealed class Player
{
    public int Money;
    public int CrewCount;
    public readonly Dictionary<string, int> Inventory = new();

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

    public int Sell(string id, int qty, int unitPrice)
    {
        int n = Consume(id, qty);
        Money += n * unitPrice;
        return n;
    }
}
