using System;

// Guard a spot. The boat lies at her station; when something hostile comes
// near it she fights, the way her tactic says to; when it leaves, she sails
// home and settles again.
//
// THE RADII ARE MEASURED FROM THE STATION, NOT FROM THE BOAT. That is the
// whole trick and it is worth being explicit about: a boat that measured
// from herself would be chasing the very thing that decides whether to
// chase. Closing the range makes the acquire test pass harder, breaking off
// makes it fail — so she would lock on, charge, drop, drift back, lock on
// again. The station cannot move, so the decision is stable no matter what
// the boat does. `AiEngageMinSec` then covers the other direction: a target
// loitering exactly on the disengage ring cannot flicker either.
public sealed class ProtectOrder : IAiOrder
{
    public AiOrderKind Kind => AiOrderKind.Protect;
    public IAiTactic? Tactic { get; }

    public float StationX { get; }
    public float StationY { get; }

    public ProtectOrder(float stationX, float stationY, IAiTactic tactic)
    {
        StationX = stationX; StationY = stationY;
        Tactic = tactic ?? throw new ArgumentNullException(nameof(tactic));
    }

    private float FromStation(float x, float y)
    {
        float dx = x - StationX, dy = y - StationY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public bool Acquire(in AiSituation s, in AiContact c, AiMemory m) =>
        FromStation(c.X, c.Y) <= AiConfig.AiEngageRadius;

    public bool Keep(in AiSituation s, in AiContact c, AiMemory m) =>
        // Committed for the first few seconds whatever happens, then held
        // until the target is well outside the ring it was acquired in.
        m.SecondsEngaged < AiConfig.AiEngageMinSec
        || FromStation(c.X, c.Y) <= AiConfig.AiDisengageRadius;

    // Nothing to fight: go back to the station, and once there, stay.
    public AiGoal Idle(in AiSituation s, AiMemory m)
    {
        if (Helmsman.Reached(s.X, s.Y, StationX, StationY))
        {
            m.Enter(AiState.Holding);
            return AiGoal.Stay(StationX, StationY);
        }
        m.Enter(AiState.Sailing);
        return new AiGoal(StationX, StationY, KeepWay: true, OpenFire: false);
    }

    // How far the boat may be dragged from her station by a chase. Without
    // it a fast target could tow a guard clean off the thing it guards.
    public (float X, float Y) Leash(float x, float y)
    {
        float dx = x - StationX, dy = y - StationY;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= AiConfig.AiLeashRadius || d < 1e-4f) return (x, y);
        float k = AiConfig.AiLeashRadius / d;
        return (StationX + dx * k, StationY + dy * k);
    }
}
