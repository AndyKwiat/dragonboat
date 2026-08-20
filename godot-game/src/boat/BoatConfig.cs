using Godot;

// Everything about how the player boat moves, looks and leans. The [Tweak]
// fields can be moved live from the T panel (see core/Tweaks.cs); the values
// here are the defaults and the source of truth — tune on the panel, then
// paste the number back here.
public static class BoatConfig
{
    public const float StartX = 2556f;
    public const float StartY = 15829f;
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

    // Handling (world units, seconds).
    [Tweak("Boat", 5, 150, 1)] public static float MaxSpeed = 50f;
    [Tweak("Boat", 0, 60, 1)] public static float MaxReverseSpeed = 20f;
    [Tweak("Boat", 5, 120, 1)] public static float Accel = 40f;
    [Tweak("Boat", 0, 120, 1)] public static float Friction = 45f;     // units/s² when coasting
    [Tweak("Boat", 0.2f, 5, 0.05f)] public static float TurnRate = Mathf.Pi * 0.55f;  // rad/s at full authority
    [Tweak("Boat", 1, 60, 1)] public static float TurnFullSpeed = 20f;  // speed at which steering has full authority
    // Largest physics step; Main sub-steps fast-forwarded time so turns don't cut corners.
    public const float MaxStepSeconds = 0.05f;

    // Lean (heel) while turning and a gentle bob, as in the combat prototype:
    // roll = LeanAngle × speed fraction, outward from the turn.
    [Tweak("Boat lean", 0, 0.6f, 0.01f)] public static float LeanAngle = 0.08f;  // radians at full speed
    [Tweak("Boat lean", 0, 1, 0.05f)] public static float LeanSmoothSec = 0.15f;  // 1/e time of the roll ease (0 = instant)
    [Tweak("Boat lean", 0, 1, 0.01f)] public static float BobAmplitude = 0.15f;   // world units
    [Tweak("Boat lean", 0, 10, 0.1f)] public static float BobRadPerSec = 2f;

    // Blob shadow: a dark ellipse on the water under the hull.
    [Tweak("Shadows", 0, 1, 0.05f)] public static float ShadowStrength = 0.4f;
    [Tweak("Shadows", 0.5f, 2, 0.05f)] public static float ShadowScale = 1.55f;   // × the hull footprint (must peek past the hull at the iso angle)

    // Land collision: the hull is three circles along its length pushed out
    // of any non-water cell (world/Terrain.cs), which removes the motion into
    // the shore and keeps the motion along it — the slide. While touching,
    // speed decays at CollisionDrag × how squarely the bow is into the land
    // (per second), so a head-on hit dies in a fraction of a second and a
    // glancing one carries on along the shore.
    [Tweak("Boat land", 0, 20, 0.5f)] public static float CollisionDrag = 4f;
    [Tweak("Boat land", 0.3f, 1.5f, 0.05f)] public static float CollisionRadiusScale = 1f; // × Width/2 per circle
}
