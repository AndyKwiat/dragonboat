using System;

// Fight at arm's length: close to a comfortable range, stop, turn broadside
// on, and shoot. The first tactic, and the one the numbers in AiConfig were
// chosen for.
//
// Two things make this simpler than it looks:
//
//   She STOPS to shoot. Holding a beam at constant range while under way is
//   an orbit — it needs a continuous turn at speed/range, about 25°/s at a
//   hundred units — and an orbit is where "can never satisfy both range and
//   bearing" comes from. But under oars a boat stops from full speed in well
//   under a second and pivots on the spot at ~50°/s, so a beam is under two
//   seconds away and costs less than one reload. Stop, pivot, hold, fire.
//
//   She never decides whether a shot is possible. The battery does that
//   every frame (BestCannon + WithinRange), so the bearing window below only
//   picks which word the report prints. State chatter cannot cost a shot,
//   which is why the hysteresis can be loose and readable rather than exact.
//
// The blind arc is the one thing it must not get wrong: nothing bears
// between 120° and 180° off the bow, so a boat that turned to the far beam
// would sweep the target THROUGH its blind spot instead of out of it. Always
// turn to the beam the target is already on.
public sealed class StandOffTactic : IAiTactic
{
    public AiGoal Fight(in AiSituation s, AiMemory m)
    {
        if (s.Target is not AiContact target) return AiGoal.Stay(s.X, s.Y);

        // Unit vector towards the target; the geometry below is all in terms
        // of it. Degenerate only if we are exactly on top of her.
        float dx = target.X - s.X, dy = target.Y - s.Y;
        float d = MathF.Max(s.Range, 1e-4f);
        float ux = dx / d, uy = dy / d;

        // Too close to fight: open the range rather than drift into a ram.
        if (s.Range < AiConfig.AiMinRange)
        {
            m.Enter(AiState.Pursuing);
            float back = AiConfig.AiFightRange;
            return new AiGoal(s.X - ux * back, s.Y - uy * back, KeepWay: true, OpenFire: false);
        }

        // Too far to fight, or too close to a consort to be any use where she
        // is: keep working for a berth. The crowding half matters as much as
        // the range — without it a boat stops the instant she is in range,
        // wherever that happens to be, and never reaches the spread berth
        // Traffic picked for her. Two of them then sit on top of each other
        // at the same bearing, in each other's line of fire, and widening
        // the spacing makes it WORSE because the berth she abandons is
        // further away. A little hysteresis so she does not shuffle for ever.
        bool engaged = m.State == AiState.Engaging;
        bool crowded = s.ConsortRange < AiConfig.AiSpacing * (engaged ? 0.8f : 1f);
        float far = AiConfig.AiFightFar;
        if (s.Range > far || crowded || (!engaged && s.Range > AiConfig.AiFightRange * 1.05f))
        {
            m.Enter(AiState.Pursuing);
            // Still shooting while she works for room, if she can reach.
            return new AiGoal(target.X - ux * AiConfig.AiFightRange, target.Y - uy * AiConfig.AiFightRange,
                KeepWay: true, OpenFire: s.Range <= far);
        }

        // In range. Enter the fight well inside the broadside arc, leave it a
        // little outside — and once in, stay for a whole volley whatever the
        // bearing does, so a target crossing the bow cannot strobe the state.
        float enter = Deg(AiConfig.AiBeamEnterDeg), hold = Deg(AiConfig.AiBeamHoldDeg);
        bool onTheBeam = s.BeamError <= (engaged ? hold : enter);
        if (!engaged && !onTheBeam && m.FireDwellLeft <= 0)
        {
            // Turning to the beam, still stopped: the state is the same, only
            // the report differs. Engaging covers both — she is in the fight.
            m.Enter(AiState.Engaging);
        }
        else m.Enter(AiState.Engaging);
        if (onTheBeam) m.FireDwellLeft = AiConfig.AiFireDwellSec;

        // Steer at a point on the beam: the boat's bow turns towards it, so
        // the target ends up square abeam on the side she is already on.
        // Rotating the bearing by -90° puts her to starboard, +90° to port.
        int side = s.BeamSide;
        float hx = uy * side, hy = -ux * side;   // the heading that puts her on that beam
        const float lookahead = 60f;             // far enough never to count as "reached"
        return new AiGoal(s.X + hx * lookahead, s.Y + hy * lookahead, KeepWay: false, OpenFire: true);
    }

    private static float Deg(float d) => d * MathF.PI / 180f;
}
