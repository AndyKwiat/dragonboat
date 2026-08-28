using System;

// The rules behind sails-up / sails-down sailing: what the wind does to a
// hull under canvas, and how fast the crew tires at the oars. Godot-free on
// purpose (compiled into `dotnet test tests`); the knobs live in
// BoatConfig and the wiring in Boat/Main. See docs/boat.md.

// The world's wind: a force of `Force` blowing TOWARDS `BearingDeg` (a
// compass bearing in world degrees, 0 = east/+x, 90 = south/+y), swinging
// slowly either side of a base bearing as the days pass. Deterministic —
// the swing is a sine of elapsed game hours, so a --seed run repeats.
public sealed class Wind
{
    public float BaseBearingDeg;
    public float SwingDeg;      // how far either side of the base the bearing wanders
    public float SwingHours;    // game hours for one full there-and-back swing
    public float Force;         // world units/s² on a hull running dead downwind

    private float hours;

    public Wind(float bearingDeg, float force, float swingDeg, float swingHours)
    {
        BaseBearingDeg = bearingDeg; Force = force; SwingDeg = swingDeg; SwingHours = swingHours;
    }

    /// Game hours elapsed; drives the swing.
    public void Advance(float gameHours) => hours += gameHours;

    public float BearingDeg => SwingHours <= 0 ? BaseBearingDeg
        : BaseBearingDeg + SwingDeg * MathF.Sin(2 * MathF.PI * hours / SwingHours);

    public float BearingRad => BearingDeg * MathF.PI / 180f;

    /// How hard the wind pushes a boat on this heading, as a fraction of
    /// Force ALONG THE WIND'S OWN DIRECTION: 1 running dead downwind, 0 on a
    /// beam reach, and `beatScale`× of what it would give downwind when the
    /// boat tries to beat into it — a shove astern, since a square sail
    /// can't point. Always >= 0: the direction is the wind's, not ours.
    public static float SailFactor(float windBearingRad, float headingRad, float beatScale)
    {
        float dot = MathF.Cos(windBearingRad - headingRad);
        return dot >= 0 ? dot : -dot * beatScale;
    }

    /// The force on a hull under sail on this heading, along the wind's own
    /// direction (so a quartering wind sets you sideways too).
    public (float X, float Y) ForceOn(float headingRad, float beatScale)
    {
        float b = BearingRad;
        float f = Force * SailFactor(b, headingRad, beatScale);
        return (MathF.Cos(b) * f, MathF.Sin(b) * f);
    }

    /// How much of the rig's pull the wind is actually giving, 0..1: a dead
    /// calm leaves the boat helpless, but the curve is generous — it climbs
    /// steeply to `kneeDrive` by `kneeFrac` of `fullWind` and only then
    /// eases on to 1, so a light breeze already beats the oars. Force above
    /// `fullWind` gives nothing more.
    public static float Drive(float force, float fullWind, float kneeFrac, float kneeDrive)
    {
        if (fullWind <= 0) return 1;
        float f = Math.Clamp(force / fullWind, 0f, 1f);
        if (kneeFrac <= 0) return kneeDrive + (1 - kneeDrive) * f;
        return f <= kneeFrac
            ? kneeDrive * f / kneeFrac
            : kneeDrive + (1 - kneeDrive) * (f - kneeFrac) / (1 - kneeFrac);
    }

    /// This wind's own drive, for callers holding an instance.
    public float Drive(float fullWind, float kneeFrac, float kneeDrive)
        => Drive(Force, fullWind, kneeFrac, kneeDrive);

    /// "NE" etc. for the readout — the compass point the wind blows towards.
    public string Towards()
    {
        string[] points = { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };
        float d = BearingDeg % 360f; if (d < 0) d += 360f;
        return points[(int)MathF.Round(d / 45f) % 8];
    }
}

// The rowers. Full at 1, spent at 0. Pulling on the oars (sails down and a
// helm order given) tires them; sailing, or lying on the oars with no order,
// rests them. Running out costs nothing yet — the bar just sits at 0.
public sealed class CrewStamina
{
    public float RowHoursToEmpty;   // game hours of unbroken rowing from full to spent
    public float RestHoursToFull;   // game hours of rest from spent to full

    public float Level { get; private set; } = 1f;
    public bool Exhausted => Level <= 0f;
    /// True while the crew is actually pulling — the UI labels the bar with it.
    public bool Rowing { get; private set; }

    public CrewStamina(float rowHoursToEmpty, float restHoursToFull)
    {
        RowHoursToEmpty = rowHoursToEmpty; RestHoursToFull = restHoursToFull;
    }

    public void Update(float gameHours, bool rowing)
    {
        Rowing = rowing;
        float rate = rowing
            ? (RowHoursToEmpty > 0 ? -1f / RowHoursToEmpty : -1e9f)
            : (RestHoursToFull > 0 ? 1f / RestHoursToFull : 1e9f);
        Level = Math.Clamp(Level + rate * gameHours, 0f, 1f);
    }

    public void Set(float level) => Level = Math.Clamp(level, 0f, 1f);
}
