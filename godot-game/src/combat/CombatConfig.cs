using Godot;

// Cannons, cannonballs and impact effects — ported from ../combat-prototype's
// `P` object. [Tweak] fields move live from the T panel (core/Tweaks.cs);
// the values here are the defaults and the source of truth.
public static class CombatConfig
{
    // Where guns sit on a 12-unit hull (the prototype's boat), in hull
    // units: forward offset, offset to starboard, height above the
    // waterline, bearing in degrees (0 = dead ahead, +90 = starboard).
    // CannonBattery scales these by the vessel's length / 12.
    public record Mount(float Forward, float Right, float Height, float BearingDeg);
    public static readonly Mount[] PlayerMounts =
    {
        new(6.5f, 0, 2.4f, 0),                                   // bow chaser
        new(-3, -2, 2.4f, -90), new(0, -2, 2.4f, -90), new(3, -2, 2.4f, -90),   // port broadside
        new(-3,  2, 2.4f,  90), new(0,  2, 2.4f,  90), new(3,  2, 2.4f,  90),   // starboard broadside
    };
    public static readonly Vector3 CannonSize = new(2f, 0.8f, 0.8f);   // length along its own forward (+X)
    public static readonly Color CannonColor = new Color(0x222222FF);
    public static readonly Color CannonReloadingColor = new Color(0x8a6a4aFF);

    // Ballistics: the launch angle is fixed and the muzzle speed is solved so
    // the ball lands on the aim point (see Projectiles.SolveLaunch).
    [Tweak("Cannon", 10, 85, 1)] public static float ArcAngleDeg = 45f;
    [Tweak("Cannon", 5, 150, 1)] public static float Gravity = 30f;        // world units/s²
    [Tweak("Cannon", 0, 6, 0.1f)] public static float ReloadTime = 2f;      // seconds, per cannon
    [Tweak("Cannon", 10, 180, 5)] public static float FiringArcDeg = 60f;   // total arc each cannon covers
    [Tweak("Cannon", 0.2f, 4, 0.1f)] public static float BallRadius = 0.9f;
    [Tweak("Cannon", 0, 3, 0.1f)] public static float HitMargin = 0.5f;     // hit box padding around a hull
    [Tweak("Cannon", 0, 200, 1)] public static float MaxRange = 0f;        // 0 = unlimited (click anywhere)
    public static readonly Color BallColor = new Color(0x111111FF);

    // Effects: flat rings that grow and fade (splashes, land puffs) and
    // spheres that fade (muzzle flash, hits). Sizes are world units.
    [Tweak("Effects", 0.2f, 10, 0.1f)] public static float SplashSize = 1.5f;
    [Tweak("Effects", 0.1f, 3, 0.1f)] public static float SplashLife = 0.6f;
    [Tweak("Effects", 1, 10, 0.5f)] public static float RingGrow = 4f;       // final size / initial size
    [Tweak("Effects", 0.2f, 10, 0.1f)] public static float LandPuffSize = 2f;
    [Tweak("Effects", 0.1f, 3, 0.1f)] public static float LandPuffLife = 0.8f;
    [Tweak("Effects", 0.2f, 10, 0.1f)] public static float MuzzleFlashSize = 1.5f;
    [Tweak("Effects", 0.05f, 1, 0.05f)] public static float MuzzleFlashLife = 0.15f;
    [Tweak("Effects", 0.5f, 10, 0.1f)] public static float HitFlashSize = 2.5f;
    [Tweak("Effects", 0.1f, 2, 0.05f)] public static float HitFlashLife = 0.5f;
    [Tweak("Effects", 0, 2, 0.05f)] public static float HullFlashSec = 0.4f; // how long a hit hull glows red
    public static readonly Color SplashColor = new Color(0xcfe8ffFF);
    public static readonly Color LandPuffColor = new Color(0xb89c6aFF);
    public static readonly Color MuzzleFlashColor = new Color(0xffaa33FF);
    public static readonly Color HitFlashColor = new Color(0xff5522FF);
    public static readonly Color HullHitColor = new Color(0xff2200FF);

    // Aim reticle: a ring under the cursor, a constant size on screen.
    [Tweak("Aim", 4, 40, 1)] public static float ReticlePx = 14f;
    public static readonly Color ReticleReadyColor = new Color(0xffdd55FF);
    public static readonly Color ReticleBlockedColor = new Color(0x888888FF);
}
