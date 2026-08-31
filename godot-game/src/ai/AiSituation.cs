using System;

// Everything the AI knows this frame, as one immutable snapshot. Built fresh
// each frame by AiBoat and handed to the order, the tactic and the steering,
// so none of them recomputes the range, the bearing or the line of sight —
// and so none of them needs a Godot type, which is what lets all of it be
// tested (the ITrader/MarketDeal trick the README describes).
//
// Facts only. Anything the boat has to REMEMBER between frames — which state
// it is in, how long it has been there, which target it settled on — lives
// in AiMemory, because mixing memory into a snapshot makes it unclear which
// of the two a reader is looking at.
public readonly record struct AiSituation(
    float Dt,
    int Team,
    float X, float Y, float Heading, float Speed,
    AiContact? Target,
    float Range,          // to the target; 0 with no target
    float Bearing,        // to the target, signed radians, + = starboard (Helmsman's convention)
    bool LineOfSight,     // clear water between us and it
    int LoadedGuns)
{
    public bool HasTarget => Target.HasValue;

    // How far off the beam the target lies, in radians: 0 when it is square
    // abeam (a broadside bears), pi/2 when it is dead ahead or dead astern.
    public float BeamError => MathF.Abs(MathF.Abs(Bearing) - MathF.PI / 2);

    // Which beam is nearer — the side the target is already on. Turning to
    // the far beam would be the long way round, and from dead astern would
    // sweep the target through the blind arc rather than out of it.
    public int BeamSide => Bearing >= 0 ? 1 : -1;
}
