using System.Collections.Generic;

// Who to fight. One pass over every contact, run for each boat before its
// order decides anything.
//
// It lives here rather than inside each order for one reason: the rule you
// will eventually want — don't pile onto a ship a teammate is already
// fighting — needs to see the whole fleet, not one boat. That filter goes in
// this function and nowhere else.
//
// Sticky by design. A target already being fought is kept as long as the
// order says to keep it, and only when there is none does it look for a new
// one. That asymmetry between Acquire and Keep IS the hysteresis; see
// ProtectOrder for why its radii are measured from the station.
public static class Targeting
{
    public static AiContact? Pick(int selfTeam, float selfX, float selfY,
        IReadOnlyList<AiContact> contacts, IAiOrder order, AiMemory memory, in AiSituation situation)
    {
        if (order.Tactic == null) { memory.ForgetTarget(); return null; }

        // Still fighting the one we were?
        if (memory.TargetId is int id)
        {
            foreach (var c in contacts)
            {
                if (c.Id != id) continue;
                if (Hostile(selfTeam, c.Team) && order.Keep(situation, c, memory)) return c;
                break;
            }
            memory.ForgetTarget();
        }

        // Otherwise the nearest hostile worth starting on.
        AiContact? best = null;
        float bestSq = float.PositiveInfinity;
        foreach (var c in contacts)
        {
            if (!Hostile(selfTeam, c.Team)) continue;
            if (!order.Acquire(situation, c, memory)) continue;
            float dx = c.X - selfX, dy = c.Y - selfY;
            float sq = dx * dx + dy * dy;
            if (sq >= bestSq) continue;
            bestSq = sq; best = c;
        }
        if (best is AiContact picked)
        {
            memory.TargetId = picked.Id;
            memory.SecondsEngaged = 0;
        }
        return best;
    }

    // Two vessels are enemies when they are not on the same side. That is the
    // whole of the game's diplomacy, and it is enough for everything asked of
    // it so far.
    public static bool Hostile(int a, int b) => a != b;
}
