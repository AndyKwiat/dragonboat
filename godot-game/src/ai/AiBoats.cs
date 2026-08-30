using Godot;
using System.Collections.Generic;

// The AI fleet: every self-sailing non-player boat in the run, ticked once a
// frame. Only a test scenario ever puts boats in it (src/scenarios), so in a
// normal game this holds nothing and Update walks an empty list — the game
// sails exactly as it did before the AI existed.
public partial class AiBoats : Node3D
{
    private readonly List<AiBoat> boats = new();
    public IReadOnlyList<AiBoat> Boats => boats;
    public int Count => boats.Count;

    private Terrain terrain = null!;
    private NavMesh nav = null!;

    public void Init(Terrain terrain, NavMesh nav)
    {
        Name = "AiBoats";
        this.terrain = terrain;
        this.nav = nav;
    }

    // Put a boat on the water. Sails are struck: under canvas the wind would
    // shove her about before she has any say in where she goes, and the oars
    // answer the helm at any speed — including turning on the spot, which is
    // what a river bend needs.
    public AiBoat Spawn(float x, float y, float heading, AiState state, Vector2? destination = null)
    {
        var boat = new AiBoat
        {
            X = x, Y = y, Heading = heading, State = state,
            Hull = AiBoat.BlueHull, SailsUp = false, Destination = destination,
        };
        boat.Init(terrain);
        if (destination is Vector2 to)
        {
            boat.Route = NavPath.Find(nav, x, y, to.X, to.Y);
            if (boat.Route == null)
            {
                GD.PushWarning($"AI boat at {x:0},{y:0}: no route to {to.X:0},{to.Y:0} — lying to instead");
                boat.State = AiState.Idle;
            }
        }
        else if (state == AiState.Voyage)
        {
            GD.PushWarning($"AI boat at {x:0},{y:0}: voyage with nowhere to go — lying to instead");
            boat.State = AiState.Idle;
        }
        AddChild(boat);
        boats.Add(boat);
        return boat;
    }

    // dt is game time, and 0 while the world is paused (the city menu).
    public void Update(float dt, Boat player)
    {
        if (dt <= 0 || boats.Count == 0) return;
        foreach (var b in boats) b.Think(dt, player);
    }

    // One line per boat for a scripted run's final report, so a pathing run
    // can be asserted on without looking at pixels.
    public string Report()
    {
        if (boats.Count == 0) return "";
        var parts = new List<string>();
        foreach (var b in boats)
        {
            var where = $"{b.State.ToString().ToLowerInvariant()}@({b.X:F1}, {b.Y:F1}) {Mathf.RadToDeg(b.Heading):F0}° {b.Speed:F1}";
            if (b.Route != null)
                where += b.Arrived
                    ? $" arrived ({b.Route.Waypoints.Count} legs)"
                    : $" leg {b.Leg}/{b.Route.Waypoints.Count - 1}, {b.DistanceToGo():F0} to go";
            parts.Add(where);
        }
        return $"ai boats {string.Join("; ", parts)}";
    }
}
