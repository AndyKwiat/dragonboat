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
    // Where the guns sit on the nominal hull: chasers at the ends, the
    // broadside spread evenly along BroadsideAft..BroadsideFore at
    // BroadsideRight either side.
    public const float ChaserForward = 6.5f, ChaserSpread = 1f, MountHeight = 2.4f;
    public const float BroadsideAft = -3f, BroadsideFore = 3f, BroadsideRight = 2f;

    // A vessel's battery, laid out from the gun counts her class carries
    // (boats.yml): bow chasers, stern chasers and so many guns a side. The
    // medium boat's 1 + 3 + 3 comes out exactly where it always was, and a
    // boat added to the file is armed without anything here changing.
    public static Mount[] MountsFor(BoatClass cls)
    {
        var mounts = new System.Collections.Generic.List<Mount>();
        // Chasers: one on the centreline, more ranged abreast of it.
        void Chasers(int n, float forward, float bearing)
        {
            for (int i = 0; i < n; i++)
            {
                float right = n == 1 ? 0 : Mathf.Lerp(-ChaserSpread, ChaserSpread, i / (float)(n - 1));
                mounts.Add(new Mount(forward, right, MountHeight, bearing));
            }
        }
        Chasers(cls.GunsBow, ChaserForward, 0);
        Chasers(cls.GunsStern, -ChaserForward, 180);
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < cls.GunsPerSide; i++)
            {
                float fwd = cls.GunsPerSide == 1 ? 0
                          : Mathf.Lerp(BroadsideAft, BroadsideFore, i / (float)(cls.GunsPerSide - 1));
                mounts.Add(new Mount(fwd, side * BroadsideRight, MountHeight, side * 90));
            }
        return mounts.ToArray();
    }
    public static readonly Vector3 CannonSize = new(2f, 0.8f, 0.8f);   // length along its own forward (+X)
    public static readonly Color CannonColor = new Color(0x222222FF);
    public static readonly Color CannonReloadingColor = new Color(0x8a6a4aFF);

    // Ballistics: every ball leaves at MuzzleSpeed; the launch angle is
    // solved so it lands on the aim point (see Projectiles.SolveLaunch).
    // Farther shots arc higher and take longer, and range is capped by
    // physics (~MuzzleSpeed²/Gravity on flat water — the reticle greys out
    // beyond it). The angle never drops below MinAngleDeg, so a very close
    // target gets overshot — the ball sails over it.
    [Tweak("Cannon", 10, 200, 1)] public static float MuzzleSpeed = 70f;    // world units/s
    [Tweak("Cannon", 0, 45, 1)] public static float MinAngleDeg = 0f;
    [Tweak("Cannon", 5, 150, 1)] public static float Gravity = 30f;        // world units/s²
    [Tweak("Cannon", 0, 6, 0.1f)] public static float ReloadTime = 2f;      // seconds, per cannon
    [Tweak("Cannon", 10, 180, 5)] public static float FiringArcDeg = 60f;   // total arc each cannon covers
    [Tweak("Cannon", 0.2f, 4, 0.1f)] public static float BallRadius = 0.9f;
    [Tweak("Cannon", 0, 3, 0.1f)] public static float HitMargin = 0.5f;     // hit box padding around a hull
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

    // Blob shadows under cannonballs: a dark disc on the ground straight
    // below the ball, shrinking and fading as it climbs (gone at
    // ShadowShrinkHeight and above).
    [Tweak("Shadows", 0, 1, 0.05f)] public static float BallShadowStrength = 0.35f;
    [Tweak("Shadows", 0.5f, 4, 0.1f)] public static float BallShadowScale = 1.5f;   // × BallRadius at ground level
    [Tweak("Shadows", 0, 0.9f, 0.05f)] public static float BallShadowMin = 0.35f;    // fraction left at full height
    [Tweak("Shadows", 5, 200, 1)] public static float ShadowShrinkHeight = 60f;

    // Aim reticle: a ring under the cursor, a constant size on screen.
    [Tweak("Aim", 4, 40, 1)] public static float ReticlePx = 14f;
    public static readonly Color ReticleReadyColor = new Color(0xffdd55FF);
    public static readonly Color ReticleBlockedColor = new Color(0x888888FF);
}
