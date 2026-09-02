using System;
using System.Collections.Generic;

// The harbour: where a boat is bought. Medium and large cities have one; a
// small one has a market and nothing else. You own exactly one hull, so
// buying is always a trade — the old boat goes back over the counter at what
// it cost (buy and sell price are the same) and only the difference changes
// hands. See docs/harbour.md.
//
// This file is the arithmetic and nothing else: no Godot types, no UI, no
// swapping of nodes. ui/CityPopup.cs draws the offers and Main does the swap.
public static class Harbour
{
    // Only the bigger ports build boats. Same shape as Loans.HasBank.
    public static bool HasHarbour(City city) => city.Size is "medium" or "large";

    // One boat on offer. `Net` is what leaves the purse — negative when
    // trading down, which pays. `Blocked` is null when the deal can be
    // struck, and otherwise says why not, in words fit for a tooltip.
    public readonly record struct Offer(BoatClass Class, int Price, int TradeIn, int Net, bool Owned, string? Blocked)
    {
        public bool CanBuy => Blocked == null;
    }

    public static Offer OfferOf(BoatClass want, BoatClass current, int money, int cargoUnits)
    {
        int net = want.Cost - current.Cost;
        bool owned = ReferenceEquals(want, current)
                  || string.Equals(want.Name, current.Name, StringComparison.OrdinalIgnoreCase);
        string? blocked =
            owned ? "You are sailing her"
            // A smaller hold is the one deal that can lose cargo, so it is
            // refused outright rather than silently thrown overboard.
            : cargoUnits > want.Cargo ? $"Her hold takes {want.Cargo} — sell down to {want.Cargo} units first"
            : net > money ? $"You are {net - money} short"
            : null;
        return new Offer(want, want.Cost, current.Cost, net, owned, blocked);
    }

    public static IReadOnlyList<Offer> OffersAt(BoatClass current, int money, int cargoUnits)
    {
        var offers = new List<Offer>();
        foreach (var b in BoatClasses.All) offers.Add(OfferOf(b, current, money, cargoUnits));
        return offers;
    }
}
