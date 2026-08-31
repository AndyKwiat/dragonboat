using System;

// A vessel's collision shell, as the physics sees it: three circles spaced
// along the keel, which is the same shape Boat.CollideWithLand already uses
// against the shore. Everything here is plain geometry — no Godot, no Boat —
// so the maths can be tested without a game running.
//
// Size is carried per hull rather than read from a config, because there
// will be more than one class of vessel: a launch and a merchantman differ
// in Length, Radius and Mass, and every rule below is written in terms of
// those three numbers rather than the one boat the game has today.
public readonly record struct Hull(
    float X, float Y, float Heading,
    float Length,     // stem to stern
    float Radius,     // of each of the three circles; roughly the half-beam
    float Mass)       // for deciding who gives way; bigger hulls shove smaller ones
{
    // The furthest any part of the shell reaches from the centre. Two hulls
    // further apart than the sum of these cannot possibly touch, which is the
    // whole of the broad phase.
    public float Reach => Length / 2 + Radius;

    public const int CircleCount = 3;

    // The three circle centres, bow to stern, in world coordinates.
    public void Circles(Span<(float X, float Y)> into)
    {
        float cos = MathF.Cos(Heading), sin = MathF.Sin(Heading);
        float step = Length / 3;
        for (int i = 0; i < CircleCount; i++)
        {
            float along = (i - 1) * step;      // -L/3, 0, +L/3
            into[i] = (X + cos * along, Y + sin * along);
        }
    }
}

public static class Hulls
{
    // Do two hulls overlap, and if so, which way and by how much?
    //
    // The deepest overlapping pair of circles wins: pushing that one apart
    // clears the worst of the intersection, and because the resolution runs
    // every frame the rest follows. `nx, ny` is a unit vector pointing from
    // `a` towards `b` — the direction b must be pushed and a must give way.
    public static bool Overlap(in Hull a, in Hull b, out float nx, out float ny, out float depth)
    {
        nx = 0; ny = 0; depth = 0;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float reach = a.Reach + b.Reach;
        if (dx * dx + dy * dy > reach * reach) return false;   // broad phase

        Span<(float X, float Y)> ca = stackalloc (float, float)[Hull.CircleCount];
        Span<(float X, float Y)> cb = stackalloc (float, float)[Hull.CircleCount];
        a.Circles(ca);
        b.Circles(cb);
        float touching = a.Radius + b.Radius;
        float deepest = 0;
        foreach (var pa in ca)
            foreach (var pb in cb)
            {
                float ox = pb.X - pa.X, oy = pb.Y - pa.Y;
                float distSq = ox * ox + oy * oy;
                if (distSq >= touching * touching) continue;
                float dist = MathF.Sqrt(distSq);
                float over = touching - dist;
                if (over <= deepest) continue;
                deepest = over;
                // Two hulls exactly on top of each other have no direction to
                // separate along; shove them apart abeam of the first, which
                // is the shortest way out of a hull-shaped overlap.
                if (dist > 1e-4f) { nx = ox / dist; ny = oy / dist; }
                else { nx = -MathF.Sin(a.Heading); ny = MathF.Cos(a.Heading); }
            }
        depth = deepest;
        return deepest > 0;
    }

    // How much of a separation each hull gives way by. The heavier vessel
    // moves less, so a launch bounces off a merchantman rather than shifting
    // it; two of the same size each give half.
    public static (float A, float B) Share(float massA, float massB)
    {
        float total = massA + massB;
        if (total <= 0) return (0.5f, 0.5f);
        return (massB / total, massA / total);
    }
}
