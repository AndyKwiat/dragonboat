// The fight's numbers, live-tunable on the T panel (group "AI") and settable
// from a scripted run with --set=AiFightRange=90. Godot-free, so the rules
// that read them compile into tests/ — core/Tweaks.cs imports no Godot.
//
// They are [Tweak]s rather than constants because these are values you find
// by watching, not by reasoning: open ./battleTest.sh, press T, drag, and
// dump the ones worth keeping. The code defaults are the source of truth.
//
// The gunnery facts they are derived from (src/combat/CombatConfig.cs):
// max range is MuzzleSpeed²/Gravity = 163 units; the firing arc is 60° wide,
// so a mount bears within ±30°; mounts sit at 0° and ±90°, so a broadside is
// three guns and the bow chaser is one; reload is 2 s per cannon.
public static class AiConfig
{
    // --- who to fight -------------------------------------------------
    // Both radii are measured from the thing being guarded, NOT from the
    // boat. Measuring from the boat would be positive feedback: closing the
    // range is what makes the acquire test pass, and breaking off is what
    // makes it fail, so a boat would chase, drop, chase, drop.
    [Tweak("AI", 0, 600, 10)] public static float AiEngageRadius = 220f;    // > 163: start moving before they can shoot
    [Tweak("AI", 0, 900, 10)] public static float AiDisengageRadius = 380f; // 1.7x: they must genuinely leave
    [Tweak("AI", 0, 30, 0.5f)] public static float AiEngageMinSec = 4f;     // never drop a target in the first few seconds
    [Tweak("AI", 0, 900, 10)] public static float AiLeashRadius = 300f;     // never chase further than this from station

    // --- how to fight -------------------------------------------------
    [Tweak("AI", 20, 163, 5)] public static float AiFightRange = 110f;   // the range it wants; flight time 1.5 s
    [Tweak("AI", 20, 163, 5)] public static float AiFightFar = 140f;     // beyond this, close again (0.86 x max range)
    [Tweak("AI", 0, 100, 5)] public static float AiMinRange = 30f;       // inside this, open the range rather than ram
    // The broadside bears between 60° and 120° off the bow. Enter well
    // inside that; leave a little outside it. The gap is the dwell that
    // stops a drifting bearing flipping the state every frame — though the
    // window is only cosmetic, since the battery decides the actual shot.
    [Tweak("AI", 0, 30, 1)] public static float AiBeamEnterDeg = 18f;     // enter at 90 ± 18
    [Tweak("AI", 0, 30, 1)] public static float AiBeamHoldDeg = 27f;      // leave at 90 ± 27
    [Tweak("AI", 0, 10, 0.5f)] public static float AiFireDwellSec = 2.5f; // > ReloadTime, so a volley finishes
    [Tweak("AI", 0, 40, 1)] public static float AiStopSpeed = 6f;         // back the oars above this while engaging

    // --- gunnery ------------------------------------------------------
    // How far ahead of a moving target to aim, as a fraction of the full
    // ballistic lead. 0 aims where the target is right now, a guaranteed
    // miss on anything under way; 1 is a perfect shot at something holding
    // its course. The default is deliberately short of perfect so a
    // manoeuvring player can spoil it.
    [Tweak("AI", 0, 1.5f, 0.05f)] public static float AiLeadFraction = 0.85f;

    // --- taking damage ------------------------------------------------
    // Rounds an AI boat can absorb before she goes down. The player has no
    // such number and cannot be sunk at all (src/boat/Boat.cs).
    [Tweak("AI", 1, 20, 1)] public static int AiHitsToSink = 3;

    // --- keeping out of each other's way ------------------------------
    // How much water a boat wants to itself, and how far apart a squadron
    // ends up sitting round a shared target. Two standard hulls reach 8
    // units from their centres, so anything under 16 is a shove — but the
    // binding constraint is the line of fire, not the hulls: consorts a
    // third of a boat-length apart on the same bearing shoot through each
    // other. Measured on the battle bench, 36 keeps three boats 44 units
    // apart at the closest and nothing within 9 units of anyone's line.
    [Tweak("AI", 0, 120, 2)] public static float AiSpacing = 36f;

    // --- housekeeping -------------------------------------------------
    [Tweak("AI", 0.1f, 10, 0.1f)] public static float AiRepathSec = 1.5f;   // how often a blind chase re-plans
    [Tweak("AI", 0.05f, 2, 0.05f)] public static float AiLosRefreshSec = 0.25f;  // how often line of sight is retested

    // The hostile fleet the world is stocked with, and what one is worth.
    public const int FleetCount = 20;
    public const int KillHonour = 50;
    // Nobody is scattered closer to the player's start than this...
    public const float MinSpawnDistance = 400f;
    // ...nor out in the deep ocean: a spawn must have land within this many
    // cells, sampled every LandSampleStep, which covers rivers and coastal
    // water and excludes the open sea.
    public const int MaxLandDistanceCells = 250;
    public const int LandSampleStep = 8;
    // A boat this far from the player, and with nothing to do, is not worth
    // thinking about. She is already at her station; she can wait.
    public const float ThinkRange = 700f;

    // How far apart boats spawned as a group are placed. Not a [Tweak]:
    // it is read once, when they are put on the water, and a slider that
    // did nothing after launch would only mislead.
    public const float SquadronSpacing = 18f;
}
