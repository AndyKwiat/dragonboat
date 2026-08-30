using Godot;
using System;

// A non-player boat that sails itself.
//
// It IS a Boat: the same hull, the same arcade physics, the same land
// collision and the same hit box as the player's — so whatever the AI learns
// to do, it does under the rules the player sails by. All it adds is a state
// (src/ai/AiState.cs) and, once a frame, the helm orders that state wants;
// the physics step is the player's, unchanged.
//
// It is NOT the hostile fleet: those hulls are EnemyShip (src/enemies), they
// are moored, they never move, and this class does not touch them. AI boats
// exist only inside a test scenario (src/scenarios/Scenario.cs), so a normal
// launch has none of them.
public partial class AiBoat : Boat
{
    // Blue, so an AI boat is never mistaken for the player's boat (dark
    // brown) or for the hostile fleet (red).
    public static readonly Color BlueHull = new Color("#2f6fb0");

    public AiState State = AiStates.Default;
    // Where a Voyage is bound, and the route it is following there
    // (src/nav/NavPath.cs). Null on a boat that was never given a passage.
    public Vector2? Destination;
    public NavRoute? Route;
    // The waypoint currently being made for; everything before it is astern.
    public int Leg;

    public bool Arrived => Route != null && Leg >= Route.Waypoints.Count;

    // The point this boat is steering for right now, for the route overlay.
    public Vector2? Target =>
        Route != null && Leg < Route.Waypoints.Count
            ? new Vector2(Route.Waypoints[Leg].X, Route.Waypoints[Leg].Y)
            : null;

    // One frame of AI: decide, then sail. `player` is the player's boat —
    // most states will want to know where it is.
    public void Think(float dt, Boat player)
    {
        var helm = State switch
        {
            // Lies to. Sails struck (so the wind cannot take her) and not an
            // order given: she goes nowhere unless the water moves her.
            AiState.Idle => HelmOrder.None,
            AiState.Voyage => Sail(),
            _ => HelmOrder.None,
        };
        // Sub-stepped exactly like the player's boat, so a big time scale
        // can't cut corners on a turn (Main sub-steps the player the same way).
        float remaining = dt;
        while (remaining > 0)
        {
            float step = Mathf.Min(remaining, BoatConfig.MaxStepSeconds);
            Update(step, helm);
            remaining -= step;
        }
    }

    // Follow the route: drop every waypoint already reached — a fast boat can
    // pass more than one in a frame, and the first is our own starting point
    // — then steer for the next. Arriving strikes the oars and falls back to
    // Idle, so a finished passage looks like a boat lying quietly at anchor.
    private HelmOrder Sail()
    {
        if (Route == null) return HelmOrder.None;
        while (Leg < Route.Waypoints.Count
               && Helmsman.Reached(X, Y, Route.Waypoints[Leg].X, Route.Waypoints[Leg].Y))
            Leg++;
        if (Leg >= Route.Waypoints.Count)
        {
            State = AiState.Idle;
            return HelmOrder.None;
        }
        var wp = Route.Waypoints[Leg];
        return Helmsman.SteerTo(X, Y, Heading, wp.X, wp.Y);
    }

    // How far is left to sail, along the route rather than as the crow flies.
    public float DistanceToGo()
    {
        if (Route == null || Leg >= Route.Waypoints.Count) return 0;
        float total = new Vector2(Route.Waypoints[Leg].X - X, Route.Waypoints[Leg].Y - Y).Length();
        for (int i = Leg + 1; i < Route.Waypoints.Count; i++)
        {
            var a = Route.Waypoints[i - 1];
            var b = Route.Waypoints[i];
            total += new Vector2(b.X - a.X, b.Y - a.Y).Length();
        }
        return total;
    }
}
