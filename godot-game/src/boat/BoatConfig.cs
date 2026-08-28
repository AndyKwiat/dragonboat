using Godot;

// Everything about how the player boat moves, looks and leans. The [Tweak]
// fields can be moved live from the T panel (see core/Tweaks.cs); the values
// here are the defaults and the source of truth — tune on the panel, then
// paste the number back here.
public static class BoatConfig
{
    // The start position is off a large city's shore (Main / CityPlacer.WaterStartNear).
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

    // Sails up or down (SPACE). Under canvas the boat is slower to answer
    // the helm and slower to gather way, but the sea drags it less, so it
    // ends up faster — and the wind acts on it. Under oars nothing but the
    // rowers moves it: a hard shove that dies against much more drag, and a
    // tired crew (CrewStamina). Each state scales the thrust, the drag and
    // the rudder; the defaults give ~67 u/s under sail vs ~43 at the oars,
    // and the oars pull more than twice as hard off the mark.
    [Tweak("Sailing", 0.2f, 3, 0.05f)] public static float SailThrustScale = 0.7f;
    [Tweak("Sailing", 0.2f, 3, 0.05f)] public static float SailDragScale = 0.45f;
    [Tweak("Sailing", 0.2f, 2, 0.05f)] public static float SailTurnScale = 0.6f;
    [Tweak("Sailing", 0.2f, 3, 0.05f)] public static float OarThrustScale = 1.6f;
    [Tweak("Sailing", 0.2f, 3, 0.05f)] public static float OarDragScale = 2f;
    [Tweak("Sailing", 0.2f, 2, 0.05f)] public static float OarTurnScale = 1f;
    // Rudder authority an oared boat keeps with no way on: the crew can back
    // one side and pull the other, so it spins on the spot. Sails still need
    // water over the rudder.
    [Tweak("Sailing", 0, 1, 0.05f)] public static float OarStandingTurn = 0.5f;

    // What the sails pull is the wind's to give: no wind, no way on. The
    // curve is deliberately generous (Wind.Drive) — SailWindKneeDrive of the
    // rig's full pull by SailWindKneeFrac of SailFullWind — so anything
    // above a breath of air already beats the oars, and only a flat calm
    // leaves you reaching for them.
    [Tweak("Sailing", 1, 60, 0.5f)] public static float SailFullWind = 30f;
    [Tweak("Sailing", 0.02f, 1, 0.01f)] public static float SailWindKneeFrac = 0.15f;
    [Tweak("Sailing", 0, 1, 0.05f)] public static float SailWindKneeDrive = 0.6f;

    public static float ThrustScaleFor(bool sailsUp) => sailsUp ? SailThrustScale : OarThrustScale;
    public static float DragScaleFor(bool sailsUp) => sailsUp ? SailDragScale : OarDragScale;
    public static float TurnScaleFor(bool sailsUp) => sailsUp ? SailTurnScale : OarTurnScale;

    // Crew stamina: game hours of unbroken rowing to empty the bar, and of
    // rest (sailing, or oars shipped with no helm order) to fill it again.
    // Nothing happens at 0 yet beyond the bar flashing.
    [Tweak("Sailing", 6, 240, 1)] public static float RowHoursToEmpty = 72f;    // ~3 game days
    [Tweak("Sailing", 6, 240, 1)] public static float RestHoursToFull = 36f;

    // Reference top speed in still water with no crew bonus; cameras and the
    // HUD scale by it. Derived from the drag, so it follows the sliders —
    // and from the sail state, so the no-argument form (the fastest the boat
    // goes: fully-powered canvas) is what the cameras use.
    public static float TerminalSpeed() => TerminalSpeed(true);

    public static float TerminalSpeed(bool sailsUp)
    {
        float force = MaxForce * ThrustScaleFor(sailsUp);
        float k1 = DragLinear * DragScaleFor(sailsUp), k2 = DragQuad * DragScaleFor(sailsUp);
        if (k2 <= 0) return k1 <= 0 ? 1e9f : force / k1;
        return (-k1 + Mathf.Sqrt(k1 * k1 + 4 * k2 * force)) / (2 * k2);
    }

    // Wind (world/boat/Sailing.cs `Wind`): a force on a hull under canvas
    // only — with the sails down the boat feels nothing. Bearing is the
    // compass direction in degrees the wind blows TOWARDS (0 = east/+x,
    // 90 = south/+y, 180 = west); it wanders SwingDeg either side of the
    // base bearing over SwingHours of game time. What the boat catches
    // depends on where it points: everything running dead downwind, nothing
    // on a beam reach, and BeatingScale× of it — still blowing the boat
    // astern — when it tries to beat into the wind.
    [Tweak("Wind", 0, 60, 0.5f)] public static float WindForce = 22f;
    [Tweak("Wind", 0, 360, 5)] public static float WindBearingDeg = 180f;   // west
    [Tweak("Wind", 0, 180, 5)] public static float WindSwingDeg = 60f;
    [Tweak("Wind", 1, 480, 1)] public static float WindSwingHours = 96f;    // one full swing in ~4 game days
    [Tweak("Wind", 0, 1, 0.05f)] public static float WindBeatingScale = 0.35f;

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
