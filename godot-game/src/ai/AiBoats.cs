using Godot;
using System;
using System.Collections.Generic;

// The AI fleet: every self-sailing non-player boat in the run, ticked once a
// frame. Only a test scenario ever puts boats in it (src/scenarios), so in a
// normal game this holds nothing and Update walks an empty list — the game
// sails exactly as it did before the AI existed.
//
// It also builds the contact list each frame — every vessel every boat can
// see. That happens here, once for the fleet, because the coordination rules
// worth wanting later ("don't pile onto a ship a teammate is already
// fighting") need to see all of them at once.
public partial class AiBoats : Node3D
{
    private readonly List<AiBoat> boats = new();
    private readonly List<AiContact> contacts = new();
    private readonly List<Berth> taken = new();
    public IReadOnlyList<AiBoat> Boats => boats;
    public int Count => boats.Count;

    private Terrain terrain = null!;
    private NavMesh nav = null!;
    private CombatSystem combat = null!;
    private TerrainMap map = null!;
    private Random rng = null!;
    private Boat player = null!;
    private Action onKill = () => { };

    public void Init(Terrain terrain, NavMesh nav, CombatSystem combat)
    {
        Name = "AiBoats";
        this.terrain = terrain;
        this.nav = nav;
        this.combat = combat;
    }

    // Stock the world with a hostile fleet: boats scattered on coastal water,
    // each guarding the spot it appears on. This is what the game itself has
    // instead of a scenario's hand-placed boats — the same AiBoat, the same
    // protect orders, so the fleet in the real world behaves exactly as the
    // ones on the battle bench do.
    public void Scatter(TerrainMap map, Boat player, Random rng, int count, int team, Action onKill)
    {
        this.map = map; this.player = player; this.rng = rng; this.onKill = onKill;
        while (boats.Count < count)
        {
            if (FindSpawn(new Rect2()) is not (float x, float y)) break;
            var boat = Spawn(x, y, (float)(rng.NextDouble() * Math.Tau),
                new ProtectOrder(x, y, new StandOffTactic()), team);
            boat.OnSunk = onKill;
        }
    }

    // Somewhere to appear: water, with land within reach so she is not out in
    // the deep ocean, clear of the player's start, and outside `avoid` (what
    // the camera can see) so nobody watches a hull pop into existence. The
    // view constraint is dropped if it cannot be met — on a map small enough
    // to fit on screen there is nowhere out of sight.
    private (float, float)? FindSpawn(Rect2 avoid)
    {
        for (int pass = 0; pass < 2; pass++)
            for (int tries = 0; tries < 400; tries++)
            {
                float x = (float)rng.NextDouble() * map.WorldW;
                float y = (float)rng.NextDouble() * map.WorldH;
                if (!terrain.IsWater(x, y) || !NearLand(x, y)) continue;
                float dx = x - player.X, dy = y - player.Y;
                if (dx * dx + dy * dy < AiConfig.MinSpawnDistance * AiConfig.MinSpawnDistance) continue;
                if (pass == 0 && avoid.HasPoint(new Vector2(x, y))) continue;
                return (x, y);
            }
        return null;
    }

    private bool NearLand(float x, float y)
    {
        int cx = (int)x, cy = (int)y, r = AiConfig.MaxLandDistanceCells;
        for (int j = -r; j <= r; j += AiConfig.LandSampleStep)
            for (int i = -r; i <= r; i += AiConfig.LandSampleStep)
                if (!Terrain.IsWaterIndex(map.TerrainAt(cx + i, cy + j))) return true;
        return false;
    }

    // Put a boat on the water. Sails are struck: under canvas the wind would
    // shove her about before she has any say in where she goes, and the oars
    // answer the helm at any speed — including turning on the spot, which is
    // what both a river bend and a broadside need.
    public AiBoat Spawn(float x, float y, float heading, IAiOrder order, int team, Vector2? destination = null)
    {
        var boat = new AiBoat
        {
            X = x, Y = y, Heading = heading, Order = order, Team = team,
            Hull = AiBoat.BlueHull, SailsUp = false, Destination = destination,
        };
        boat.Init(terrain);
        boat.Arm(combat, nav);
        boat.Memory.StationX = x;
        boat.Memory.StationY = y;
        // Solid to gunfire either way: a ball is only ever tested against the
        // targets the combat system was told about (src/combat/Projectiles.cs),
        // and without this a shell sails straight through the hull.
        combat.AddTarget(boat);
        // But only a boat that can fight gets guns. An idle hull with a
        // battery would start counting shots that nothing fires, and would
        // quietly break the benches that assert on the world's shot tally.
        if (order.Tactic != null)
            boat.Guns = combat.Arm(boat, CombatConfig.PlayerMounts, BoatConfig.Length);
        if (destination is Vector2 to)
        {
            boat.Memory.Route = NavPath.Find(nav, x, y, to.X, to.Y);
            if (boat.Memory.Route == null)
            {
                GD.PushWarning($"AI boat at {x:0},{y:0}: no route to {to.X:0},{to.Y:0} — lying to instead");
                boat.Order = new IdleOrder();
            }
        }
        AddChild(boat);
        boats.Add(boat);
        return boat;
    }

    // dt is game time, and 0 while the world is paused (the city menu).
    // `view` is the ground the camera can see; wrecks come back outside it.
    public void Update(float dt, Boat player, Rect2 view = default)
    {
        // Wrecks return even while the fight logic is asleep, so the sea is
        // never empty for long — and out of sight, so nobody sees it happen.
        if (map != null)
            foreach (var b in boats)
                if (b.Sunk && FindSpawn(view) is (float rx, float ry))
                    b.Refloat(rx, ry, (float)(rng.NextDouble() * Math.Tau),
                        new ProtectOrder(rx, ry, new StandOffTactic()));

        if (dt <= 0 || boats.Count == 0) return;
        contacts.Clear();
        contacts.Add(new AiContact(0, player.Team, player.X, player.Y, player.Vx, player.Vy, player.Heading));
        foreach (var b in boats) if (!b.Sunk) contacts.Add(b.AsContact());

        // Boats decide in a fixed order, and each is told where its consorts
        // are AND where the ones that have already decided are going. That
        // second half is what stops two boats converging on a spot neither
        // has reached yet (src/ai/Traffic.cs).
        float gateSq = AiConfig.ThinkRange * AiConfig.ThinkRange;
        for (int i = 0; i < boats.Count; i++)
        {
            if (boats[i].Sunk) continue;
            // A boat lying at her station, far from the player, is already
            // where she wants to be; she can wait. One distance test buys
            // back the cost of a whole fleet thinking on an 8192-cell map.
            //
            // ONLY a boat at her station. Idle used to count as settled, and
            // that was a bug: every boat starts Idle, so a voyage spawned
            // further than ThinkRange from the player never took her first
            // think and never sailed at all. A boat has to have decided she
            // has nothing to do before we agree with her.
            float pdx = boats[i].X - player.X, pdy = boats[i].Y - player.Y;
            if (boats[i].State == AiState.Holding && pdx * pdx + pdy * pdy > gateSq) continue;
            taken.Clear();
            for (int j = 0; j < boats.Count; j++)
            {
                if (j == i || boats[j].Sunk) continue;
                taken.Add(new Berth(boats[j].X, boats[j].Y));
                if (j < i) taken.Add(boats[j].Claim);
            }
            boats[i].Think(dt, contacts, taken, NearestConsort(i));
        }
    }

    // How far off the nearest boat on the same side is — what a tactic uses
    // to decide whether it has room to stop and fight.
    private float NearestConsort(int self)
    {
        float best = float.MaxValue;
        for (int j = 0; j < boats.Count; j++)
        {
            if (j == self || boats[j].Sunk || boats[j].Team != boats[self].Team) continue;
            float dx = boats[j].X - boats[self].X, dy = boats[j].Y - boats[self].Y;
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    // Any AI boat within dist of (x, y)? Drives the combat camera, which
    // would otherwise fall back to follow mid-fight on a map with no legacy
    // enemy ships on it — which is every scenario.
    public bool AnyWithin(float x, float y, float dist)
    {
        foreach (var b in boats)
        {
            if (b.Sunk) continue;
            float dx = b.X - x, dy = b.Y - y;
            if (dx * dx + dy * dy <= dist * dist) return true;
        }
        return false;
    }

    // One line per boat for a scripted run's final report, so a fight can be
    // asserted on without looking at pixels. Order and state are separate
    // tokens so either can be grepped; `guns` frozen at 0/7 is the "forgot to
    // tick the battery" bug showing itself.
    public string Report(int most = 4)
    {
        if (boats.Count == 0) return "";
        int sunk = 0;
        foreach (var b in boats) if (b.Sunk) sunk++;
        var parts = new List<string>();
        foreach (var b in boats)
        {
            if (b.Sunk || parts.Count >= most) continue;
            var line = $"{b.Order.Kind.ToString().ToLowerInvariant()}/{AiStates.Name(b.State)}" +
                       $"@({b.X:F1}, {b.Y:F1}) {Compass(b.Heading):F0}° {b.Speed:F1}";
            line += b.Aimed is AiContact t
                ? $" tgt {t.Id} r {b.Range:F0} brg {b.BearingDeg:+0;-0} {(b.HadLineOfSight ? "los" : "blind")}"
                : " tgt none";
            if (b.Guns != null) line += $" guns {LoadedCount(b)}/{b.Guns.Cannons.Count} shots {b.Shots}";
            line += $" taken {b.HitsTaken}";
            if (b.Route != null && b.Order.Kind == AiOrderKind.Voyage)
                line += b.Arrived ? $" arrived ({b.Route.Waypoints.Count} legs)"
                                  : $" leg {b.Leg}/{b.Route.Waypoints.Count - 1}, {b.DistanceToGo():F0} to go";
            parts.Add(line);
        }
        var tail = boats.Count > parts.Count + sunk ? $" (+{boats.Count - parts.Count - sunk} more)" : "";
        var lost = sunk > 0 ? $", {sunk} sunk" : "";
        return $"ai boats [{boats.Count - sunk} afloat{lost}] {string.Join("; ", parts)}{tail}";
    }

    // Heading accumulates as a boat turns, so a hull that has spun twice
    // reads -800 degrees. Wrap it for the report.
    private static float Compass(float radians)
    {
        float deg = Mathf.RadToDeg(radians) % 360f;
        return deg > 180f ? deg - 360f : deg < -180f ? deg + 360f : deg;
    }

    private static int LoadedCount(AiBoat b)
    {
        int n = 0;
        foreach (var c in b.Guns!.Cannons) if (c.Loaded) n++;
        return n;
    }
}
