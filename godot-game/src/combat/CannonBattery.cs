using Godot;
using System.Collections.Generic;

// The guns on one vessel. Added as a child of the vessel node so the cannons
// ride along (and lean) with it. Picks the loaded cannon that can bear on a
// target most squarely — the prototype's `bestCannon`.
public partial class CannonBattery : Node3D
{
    public readonly List<Cannon> Cannons = new();
    // The vessel this battery is mounted on (the node balls must not hit).
    public Node3D Vessel => (Node3D)GetParent();
    // What this battery has fired and hit. Projectiles.Shots/Hits are the
    // whole world's tallies, and the moment an AI boat opens fire those stop
    // meaning "the player" — so every vessel keeps its own count here.
    public int Shots, Hits;

    public CannonBattery(IEnumerable<CombatConfig.Mount> mounts, float vesselLength)
    {
        Name = "CannonBattery";
        float scale = vesselLength / 12f;   // mounts are laid out on a 12-unit hull
        foreach (var m in mounts)
        {
            var c = new Cannon(m, scale);
            Cannons.Add(c);
            AddChild(c);
        }
    }

    public int LoadedCount
    {
        get { int n = 0; foreach (var c in Cannons) if (c.Loaded) n++; return n; }
    }

    public Cannon? BestCannon(Vector3 target)
    {
        float half = Mathf.DegToRad(CombatConfig.FiringArcDeg) / 2;
        Cannon? best = null;
        float bestOff = float.PositiveInfinity;
        foreach (var c in Cannons)
        {
            if (!c.Loaded) continue;
            float off = c.OffAngle(target);
            if (off <= half && off < bestOff) { bestOff = off; best = c; }
        }
        return best;
    }

    public void Tick(float dt)
    {
        foreach (var c in Cannons) c.Tick(dt);
    }
}
