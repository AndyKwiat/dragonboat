using System;

// Where to aim at something that is moving.
//
// Nothing else in the game leads a target: the moored fleet fires at where
// the player IS, which is why it feels accurate against a boat lying still
// and misses everything under way. At a hundred units a ball is in the air
// for a second and a half and a rowed boat covers sixty-five units in that
// time — five hull lengths. Unled, an AI boat would never hit anything, and
// worse, a run could not tell "never fired" from "fired and missed".
//
// The flight time comes straight out of the same solution Projectiles uses
// (SolveLaunch), rearranged so it needs no trigonometry: with both hulls on
// the water the launch angle satisfies
//     T = tan(theta) = (v^2 - sqrt(v^4 - g^2 d^2)) / (g d)
// and the flight time is then d * sqrt(1 + T^2) / v, since the horizontal
// speed is v * cos(theta). Out of range is exactly v^4 < g^2 d^2 — the same
// discriminant that greys out the player's reticle.
public static class Gunnery
{
    // Seconds a ball is in the air over flat water, or NaN if the shot
    // cannot be made at all.
    public static float FlightTime(float distance, float muzzleSpeed, float gravity)
    {
        float v2 = muzzleSpeed * muzzleSpeed;
        float disc = v2 * v2 - gravity * gravity * distance * distance;
        if (disc < 0 || distance <= 0) return distance <= 0 ? 0 : float.NaN;
        float t = (v2 - MathF.Sqrt(disc)) / (gravity * distance);
        return distance * MathF.Sqrt(1 + t * t) / muzzleSpeed;
    }

    // Where to point the gun. Iterated, not solved in one step: the lead
    // opens the range, which lengthens the flight, which lengthens the lead.
    // Three passes settle it at any sane lead fraction. At FULL lead against
    // a target nearly fast enough to outrun the ball there is no fixed point
    // at all — every unit of lead buys the target more time — and three
    // passes then under-lead rather than running away to the horizon. That
    // is the right way to be wrong: the shot was never going to land, and a
    // boat crossing at speed should be hard to hit.
    // `lead` scales the whole correction — 0 reproduces the moored fleet's
    // naive aim exactly, which is the control that proves this does anything.
    public static bool Solve(float gunX, float gunY, float targetX, float targetY,
        float targetVx, float targetVy, float muzzleSpeed, float gravity, float lead,
        out float aimX, out float aimY)
    {
        aimX = targetX; aimY = targetY;
        for (int i = 0; i < 3; i++)
        {
            float dx = aimX - gunX, dy = aimY - gunY;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float t = FlightTime(d, muzzleSpeed, gravity);
            if (float.IsNaN(t)) return false;
            aimX = targetX + targetVx * t * lead;
            aimY = targetY + targetVy * t * lead;
        }
        // The final aim point has to be reachable too, or the shot is wasted.
        float fx = aimX - gunX, fy = aimY - gunY;
        return !float.IsNaN(FlightTime(MathF.Sqrt(fx * fx + fy * fy), muzzleSpeed, gravity));
    }
}
