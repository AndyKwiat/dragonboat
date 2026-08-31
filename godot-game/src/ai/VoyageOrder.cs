// Sail somewhere, then lie to. The passage the AI could already make, lifted
// from a state into an order: the route is planned once when the boat spawns
// (AiBoats.Spawn) and followed until it runs out of waypoints.
//
// It does not fight — no tactic, so the boat is never armed and never looks
// for a target.
public sealed class VoyageOrder : IAiOrder
{
    public AiOrderKind Kind => AiOrderKind.Voyage;
    public IAiTactic? Tactic => null;

    public bool Acquire(in AiSituation s, in AiContact c, AiMemory m) => false;
    public bool Keep(in AiSituation s, in AiContact c, AiMemory m) => false;

    // Follow the route: drop every waypoint already reached — a fast boat
    // can pass more than one in a frame, and the first is her own starting
    // point — then steer for the next. Arriving strikes the oars.
    public AiGoal Idle(in AiSituation s, AiMemory m)
    {
        if (m.Route == null)
        {
            m.Enter(AiState.Idle);
            return AiGoal.Stay(s.X, s.Y);
        }
        while (m.Leg < m.Route.Waypoints.Count
               && Helmsman.Reached(s.X, s.Y, m.Route.Waypoints[m.Leg].X, m.Route.Waypoints[m.Leg].Y))
            m.Leg++;
        if (m.Leg >= m.Route.Waypoints.Count)
        {
            m.Enter(AiState.Idle);
            return AiGoal.Stay(s.X, s.Y);
        }
        m.Enter(AiState.Sailing);
        var wp = m.Route.Waypoints[m.Leg];
        return new AiGoal(wp.X, wp.Y, KeepWay: true, OpenFire: false);
    }
}
