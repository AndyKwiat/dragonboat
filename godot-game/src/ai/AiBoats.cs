using Godot;
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
    public IReadOnlyList<AiBoat> Boats => boats;
    public int Count => boats.Count;

    private Terrain terrain = null!;
    private NavMesh nav = null!;
    private CombatSystem combat = null!;

    public void Init(Terrain terrain, NavMesh nav, CombatSystem combat)
    {
        Name = "AiBoats";
        this.terrain = terrain;
        this.nav = nav;
        this.combat = combat;
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
    public void Update(float dt, Boat player)
    {
        if (dt <= 0 || boats.Count == 0) return;
        contacts.Clear();
        contacts.Add(new AiContact(0, player.Team, player.X, player.Y, player.Vx, player.Vy, player.Heading));
        foreach (var b in boats) contacts.Add(b.AsContact());
        foreach (var b in boats) b.Think(dt, contacts);
    }

    // Any AI boat within dist of (x, y)? Drives the combat camera, which
    // would otherwise fall back to follow mid-fight on a map with no legacy
    // enemy ships on it — which is every scenario.
    public bool AnyWithin(float x, float y, float dist)
    {
        foreach (var b in boats)
        {
            float dx = b.X - x, dy = b.Y - y;
            if (dx * dx + dy * dy <= dist * dist) return true;
        }
        return false;
    }

    // One line per boat for a scripted run's final report, so a fight can be
    // asserted on without looking at pixels. Order and state are separate
    // tokens so either can be grepped; `guns` frozen at 0/7 is the "forgot to
    // tick the battery" bug showing itself.
    public string Report()
    {
        if (boats.Count == 0) return "";
        var parts = new List<string>();
        foreach (var b in boats)
        {
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
        return $"ai boats {string.Join("; ", parts)}";
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
