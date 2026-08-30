using System;

// Turning a point to make for into helm orders. Pure geometry, no Godot and
// no boat: given where you are, which way you are pointing and where you
// want to be, it says what to do with the tiller and the oars.
//
// Kept apart from AiBoat so the steering can be reasoned about and tested on
// its own — "does it turn the short way round?" is a question with an answer,
// and it should not need a running game to ask it.
public static class Helmsman
{
    // Close enough to a waypoint to start making for the next one. A boat
    // length: aiming any finer just makes it circle a spot it has reached.
    public const float ArriveRadius = 12f;
    // Heading error under this is not worth touching the tiller for; over
    // this the boat is so far off that it backs the oars and turns on the
    // spot rather than motoring away from where it wants to go.
    public const float DeadbandDeg = 4f;
    public const float TurnOnTheSpotDeg = 100f;

    // Signed shortest angle from `from` to `to`, in radians: + is a turn to
    // starboard (world headings turn +x towards +y).
    public static float Bearing(float x, float y, float heading, float tx, float ty)
    {
        float wanted = MathF.Atan2(ty - y, tx - x);
        float error = wanted - heading;
        while (error > MathF.PI) error -= MathF.Tau;
        while (error < -MathF.PI) error += MathF.Tau;
        return error;
    }

    public static bool Reached(float x, float y, float tx, float ty, float radius = ArriveRadius)
    {
        float dx = tx - x, dy = ty - y;
        return dx * dx + dy * dy <= radius * radius;
    }

    // The orders to make for (tx, ty). Turn the short way round; pull ahead
    // unless the target is so far off the bow that rowing would only take us
    // further from it.
    public static HelmOrder SteerTo(float x, float y, float heading, float tx, float ty)
    {
        float error = Bearing(x, y, heading, tx, ty);
        float deadband = DeadbandDeg * MathF.PI / 180f;
        bool starboard = error > deadband;
        bool port = error < -deadband;
        bool ahead = MathF.Abs(error) < TurnOnTheSpotDeg * MathF.PI / 180f;
        return new HelmOrder(ahead, false, port, starboard);
    }
}
