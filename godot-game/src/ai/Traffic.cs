using System;
using System.Collections.Generic;

// A spot on the water somebody is in, or has said they are going to.
public readonly record struct Berth(float X, float Y);

// Keeping boats out of each other's way.
//
// Three boats given the same orders want the same piece of water: they all
// close on the same enemy, so they all steer for the same point off her
// beam and end up in a heap. Hulls now collide, so they no longer overlap —
// but shoving each other aside every frame is not the same as station
// keeping, and it looks like what it is.
//
// So before a boat commits to a goal it checks whether that water is spoken
// for, and if it is, finds the nearest bit that is not. Boats decide in a
// fixed order and each one claims its goal, so the second boat avoids the
// first's chosen spot rather than its current one — which is what stops
// them converging on a place none of them has reached yet.
//
// Given a pivot — the enemy they are all circling — a blocked boat slides
// AROUND it rather than backing off, keeping its range while giving its
// consort room. That turns a queue into a ring, which is what three boats
// working a single target should look like. Without a pivot it simply stops
// short of the obstruction.
public static class Traffic
{
    // How far round the pivot to try, and in what steps.
    private const int Steps = 12;
    private const float StepDeg = 15f;

    public static (float X, float Y) MakeRoom(float goalX, float goalY, float clearance,
        IReadOnlyList<Berth> taken, (float X, float Y)? pivot)
    {
        if (taken.Count == 0 || IsClear(goalX, goalY, clearance, taken)) return (goalX, goalY);

        // Slide around the shared target, keeping the range, nearest first.
        if (pivot is (float px, float py))
        {
            float dx = goalX - px, dy = goalY - py;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            if (r > 1e-4f)
            {
                float from = MathF.Atan2(dy, dx);
                float step = StepDeg * MathF.PI / 180f;
                for (int i = 1; i <= Steps; i++)
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        float a = from + sign * i * step;
                        float x = px + MathF.Cos(a) * r, y = py + MathF.Sin(a) * r;
                        if (IsClear(x, y, clearance, taken)) return (x, y);
                    }
            }
        }

        // Nothing free around it (or nothing to go around): stop short of
        // whatever is in the way, backing off along the line from it.
        var blocker = Nearest(goalX, goalY, taken);
        float bx = goalX - blocker.X, by = goalY - blocker.Y;
        float d = MathF.Sqrt(bx * bx + by * by);
        // Wanting the exact spot somebody already has gives no direction to
        // back off in. Pick one — any fixed axis will do, and a fixed one
        // keeps the answer the same from frame to frame, which matters more
        // than which way it points.
        if (d < 1e-4f) { bx = 1; by = 0; d = 1; }
        return (blocker.X + bx / d * clearance, blocker.Y + by / d * clearance);
    }

    public static bool IsClear(float x, float y, float clearance, IReadOnlyList<Berth> taken)
    {
        foreach (var b in taken)
        {
            float dx = x - b.X, dy = y - b.Y;
            if (dx * dx + dy * dy < clearance * clearance) return false;
        }
        return true;
    }

    private static Berth Nearest(float x, float y, IReadOnlyList<Berth> taken)
    {
        Berth best = taken[0];
        float bestSq = float.PositiveInfinity;
        foreach (var b in taken)
        {
            float dx = x - b.X, dy = y - b.Y;
            float sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; best = b; }
        }
        return best;
    }
}
