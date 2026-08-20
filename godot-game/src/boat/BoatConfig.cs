using Godot;

// Everything about how the player boat moves, looks and leans. The [Tweak]
// fields can be moved live from the T panel (see core/Tweaks.cs); the values
// here are the defaults and the source of truth — tune on the panel, then
// paste the number back here.
public static class BoatConfig
{
    public const float StartX = 3953f;
    public const float StartY = 15315f;
    public const float StartHeading = -Mathf.Pi / 2; // pointing "up" (world -y)

    // Hull size in world units; the model (BoatModel.cs) is the combat
    // prototype's 12×4 boat scaled to Length, so Width mostly matters for the
    // collision footprint and hit box.
    public const float Length = 12f;
    public const float Width = 4f;
    public const float Height = 2f;     // hull height above the waterline
    public static readonly Color HullColor = new Color(0x8b5a2bFF);
    public static readonly Color MastColor = new Color(0x5a3a1aFF);
    public static readonly Color SailColor = new Color(0xf4f0e6FF);

    // Handling. The boat is a point mass (mass 1, so forces are accelerations
    // in world units/s²) with a velocity vector; W applies MaxForce along the
    // heading, S applies BrakeForce backwards (rowing against the way), and
    // the water drags with a linear + quadratic term split into the hull's
    // forward and lateral axes. Top speed is whatever the drag allows:
    // TerminalSpeed() solves DragLinear·v + DragQuad·v² = MaxForce.
    [Tweak("Boat", 5, 300, 1)] public static float MaxForce = 60f;
    [Tweak("Boat", 0, 200, 1)] public static float BrakeForce = 40f;
    [Tweak("Boat", 0, 5, 0.02f)] public static float DragLinear = 0.6f;     // /s, along the keel
    [Tweak("Boat", 0, 0.2f, 0.001f)] public static float DragQuad = 0.012f; // /unit, along the keel
    [Tweak("Boat", 1, 20, 0.5f)] public static float LateralDragScale = 6f;  // × keel drag for sideways motion
    [Tweak("Boat", 0, 3, 0.05f)] public static float RudderDrag = 0.4f;      // extra linear drag at full rudder
    [Tweak("Boat", 0.2f, 5, 0.05f)] public static float TurnRate = Mathf.Pi * 0.55f;  // rad/s at full authority
    [Tweak("Boat", 1, 60, 1)] public static float TurnFullSpeed = 20f;  // water speed at which the rudder has full authority

    // Reference top speed in still water with no crew bonus; cameras and the
    // HUD scale by it. Derived from the drag, so it follows the sliders.
    public static float TerminalSpeed()
    {
        if (DragQuad <= 0) return DragLinear <= 0 ? 1e9f : MaxForce / DragLinear;
        return (-DragLinear + Mathf.Sqrt(DragLinear * DragLinear + 4 * DragQuad * MaxForce)) / (2 * DragQuad);
    }

    // Wind: a constant force on the hull. Direction is a compass bearing in
    // degrees the wind blows TOWARDS (0 = east/+x, 90 = south/+y, 180 = west).
    [Tweak("Wind", 0, 60, 0.5f)] public static float WindForce = 0f;
    [Tweak("Wind", 0, 360, 5)] public static float WindBearingDeg = 180f;   // west
    public static Vector2 WindVector()
    {
        float a = Mathf.DegToRad(WindBearingDeg);
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * WindForce;
    }

    // Largest physics step; Main sub-steps fast-forwarded time so turns don't cut corners.
    public const float MaxStepSeconds = 0.05f;

    // Lean (heel): the hull rolls away from whichever way the water is
    // pushing it sideways — roll = LeanAngle × lateral speed / LeanFullSpeed.
    // Carving a turn, being set by a current and a beam wind all heel it.
    [Tweak("Boat lean", 0, 0.6f, 0.01f)] public static float LeanAngle = 0.12f;  // radians at LeanFullSpeed sideways
    [Tweak("Boat lean", 1, 40, 0.5f)] public static float LeanFullSpeed = 10f;
    [Tweak("Boat lean", 0, 1, 0.05f)] public static float LeanSmoothSec = 0.15f;  // 1/e time of the roll ease (0 = instant)
    [Tweak("Boat lean", 0, 1, 0.01f)] public static float BobAmplitude = 0.15f;   // world units
    [Tweak("Boat lean", 0, 10, 0.1f)] public static float BobRadPerSec = 2f;

    // Blob shadow: a dark ellipse on the water under the hull.
    [Tweak("Shadows", 0, 1, 0.05f)] public static float ShadowStrength = 0.4f;
    [Tweak("Shadows", 0.5f, 2, 0.05f)] public static float ShadowScale = 1.55f;   // × the hull footprint (must peek past the hull at the iso angle)

    // Land collision: the hull is three circles along its length pushed out
    // of any non-water cell (world/Terrain.cs). The velocity into the shore
    // is removed (a fraction CollisionBounce of it comes back out) and the
    // velocity along it is kept — the slide.
    [Tweak("Boat land", 0, 1, 0.05f)] public static float CollisionBounce = 0.1f;
    [Tweak("Boat land", 0.3f, 1.5f, 0.05f)] public static float CollisionRadiusScale = 1f; // × Width/2 per circle
}
