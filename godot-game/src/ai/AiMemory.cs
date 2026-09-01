// What a boat carries between frames: the state it is in, how long it has
// been there, the target it settled on, and the route it is following.
//
// Kept apart from AiSituation on purpose. The situation is what is true right
// now and is thrown away every frame; this is what the boat remembers, and it
// is the only thing an order or a tactic is allowed to write to.
public sealed class AiMemory
{
    public AiState State = AiState.Idle;
    public float SecondsInState;
    // Where the boat is stationed — the point a Protect order guards and
    // returns to. Set at spawn.
    public float StationX, StationY;

    public int? TargetId;
    public float SecondsEngaged;    // since this target was acquired; the drop is gated on it
    public float FireDwellLeft;     // committed to the current volley for this long

    public NavRoute? Route;
    public int Leg;
    public float SecondsSinceRepath;
    public float SecondsSinceLos;

    // Move to a state, restarting its clock. Re-entering the state it is
    // already in is not a transition and does not reset anything.
    public void Enter(AiState state)
    {
        if (State == state) return;
        State = state;
        SecondsInState = 0;
    }

    public void Advance(float dt)
    {
        SecondsInState += dt;
        SecondsSinceRepath += dt;
        SecondsSinceLos += dt;
        if (TargetId != null) SecondsEngaged += dt;
        FireDwellLeft = System.Math.Max(0, FireDwellLeft - dt);
    }

    public void ForgetTarget()
    {
        TargetId = null;
        SecondsEngaged = 0;
        FireDwellLeft = 0;
    }

    // Wipe her back to a fresh hull at a new station.
    public void Reset(float stationX, float stationY)
    {
        State = AiState.Idle;
        SecondsInState = 0;
        StationX = stationX; StationY = stationY;
        ForgetTarget();
        ClearRoute();
    }

    public void ClearRoute()
    {
        Route = null;
        Leg = 0;
        SecondsSinceRepath = float.MaxValue;   // so the next state that wants a route plans at once
    }
}
