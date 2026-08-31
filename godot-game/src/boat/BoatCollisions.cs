using Godot;
using System;
using System.Collections.Generic;

// Hulls do not pass through each other. Run once a frame, after every boat
// has moved, over every boat afloat.
//
// The shape is the same three circles the shore is resolved against
// (src/boat/Hulls.cs): overlapping hulls are pushed apart along the deepest
// contact, the part of their closing speed that is driving them together is
// taken out, and the part along the contact survives — so a boat brought
// alongside another slides down her side instead of stopping dead, exactly
// as she does against a beach.
//
// Who gives way is decided by mass, so this already works for vessels of
// different size: a launch shoved by a merchantman moves most of the way,
// two of a kind share it. Nothing here reads BoatConfig.Length — every
// dimension comes off the boat's own hull.
//
// `Rammed` is the hook for making a collision hurt. It fires once per pair
// per frame, and only when they were closing hard enough to be worth calling
// a ram (BoatConfig.RamSpeed); nothing is subscribed to it yet.
public sealed class BoatCollisions
{
    // (rammer, rammed, closing speed in world units/sec, where they touched)
    public Action<Boat, Boat, float, Vector2> Rammed = (a, b, speed, at) => { };

    public int Contacts { get; private set; }
    public int Rams { get; private set; }

    public void Resolve(IReadOnlyList<Boat> boats)
    {
        Contacts = 0;
        for (int i = 0; i < boats.Count; i++)
            for (int j = i + 1; j < boats.Count; j++)
                Separate(boats[i], boats[j]);
    }

    private void Separate(Boat a, Boat b)
    {
        var ha = a.Shell;
        var hb = b.Shell;
        // A little slack, so two boats can lie alongside each other without
        // shoving — without it they jitter apart at the exact contact depth.
        var slack = BoatConfig.HullSlack;
        ha = ha with { Radius = ha.Radius - slack / 2 };
        hb = hb with { Radius = hb.Radius - slack / 2 };
        if (!Hulls.Overlap(ha, hb, out float nx, out float ny, out float depth)) return;
        Contacts++;

        // Part them, the heavier hull giving way least.
        var (shareA, shareB) = Hulls.Share(a.Mass, b.Mass);
        a.X -= nx * depth * shareA; a.Y -= ny * depth * shareA;
        b.X += nx * depth * shareB; b.Y += ny * depth * shareB;

        // Take out the closing speed along the contact; whatever they were
        // doing along it survives, which is what makes them slide rather than
        // stop. Anything already parting is left alone.
        float closing = (a.Vx - b.Vx) * nx + (a.Vy - b.Vy) * ny;
        if (closing > 0)
        {
            float impulse = closing * (1 + BoatConfig.HullBounce);
            a.Vx -= nx * impulse * shareA; a.Vy -= ny * impulse * shareA;
            b.Vx += nx * impulse * shareB; b.Vy += ny * impulse * shareB;
            if (closing >= BoatConfig.RamSpeed)
            {
                Rams++;
                a.Jolt(); b.Jolt();
                Rammed(a, b, closing, new Vector2((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            }
        }

        // Their meshes were placed when they moved, so put them where they
        // actually ended up.
        a.SyncMesh();
        b.SyncMesh();
    }
}
