using System;
using System.Collections.Generic;

// VIP passengers: what happens between accepting an offer (StoryEvents) and
// stepping ashore at the destination (Player.DeliverVips) — how the reward
// decays with every day they spend aboard, and when they give up and jump.
// Whole days, counted from the day they came aboard (see core/News.cs).
// No Godot types: unit-tested in tests/.

public sealed class VipTerms
{
    public int SpeedHonour;      // paid for a delivery inside the first day
    public int SpeedHonourPerDay;// lost per further day aboard, down to none
    public int AbandonDays;      // days aboard before the passenger jumps ship
    public int AbandonHonour;    // and what that costs
    public int WarnDays;         // start warning this many days before that
}

// One passenger aboard, bound for Destination. They take no cargo space and
// several may ride at once.
public sealed class VipPassenger
{
    public City Destination = null!;
    public int PickedUpDay;

    public int DaysAboard(int today) => Math.Max(0, today - PickedUpDay);
}

public static class Vips
{
    // The speed bonus for a passenger delivered after this many days aboard:
    // the full SpeedHonour for the first day, SpeedHonourPerDay less for each
    // day after that, never below zero.
    public static int SpeedHonourFor(int daysAboard, VipTerms t) =>
        Math.Max(0, t.SpeedHonour - t.SpeedHonourPerDay * Math.Max(0, daysAboard - 1));

    // Call once a day (Main runs it on the midnight pass): passengers who
    // have been aboard AbandonDays leave over the side, costing
    // AbandonHonour each, and the ones getting close say so. Honour lost
    // goes through `loseHonour`; everything the player should be told about
    // comes back as news.
    public static List<NewsEvent> Update(List<VipPassenger> aboard, int today, VipTerms t, Action<int> loseHonour)
    {
        var news = new List<NewsEvent>();
        for (int i = aboard.Count - 1; i >= 0; i--)
        {
            var vip = aboard[i];
            int days = vip.DaysAboard(today);
            if (days >= t.AbandonDays)
            {
                aboard.RemoveAt(i);
                loseHonour(t.AbandonHonour);
                news.Add(new NewsEvent(
                    $"Passenger for {vip.Destination.Name} gave up and jumped ship — honour -{t.AbandonHonour}", true));
            }
            else if (days >= t.AbandonDays - t.WarnDays)
                news.Add(new NewsEvent(
                    $"Passenger for {vip.Destination.Name} is impatient — jumps ship in " +
                    $"{t.AbandonDays - days} day{(t.AbandonDays - days == 1 ? "" : "s")}!", false));
        }
        news.Reverse();   // oldest passenger first, the order they came aboard
        return news;
    }
}
