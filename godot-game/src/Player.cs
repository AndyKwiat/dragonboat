using System;
using System.Collections.Generic;

// The player's purse and cargo. Money may go negative; a product count never
// goes below 0 and the hold never holds more than the crew can carry.
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

    // Capped by the hold, not the purse.
    public int Buy(string id, int qty, int unitPrice)
    {
        int n = Math.Min(Math.Max(0, qty), CargoSpace());
        if (n == 0) return 0;
        Money -= n * unitPrice;
        Inventory[id] = Held(id) + n;
        return n;
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
