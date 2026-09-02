using System;

// Crew morale: how willing the crew are, 0 (mutinous) to 1 (cheerful). It
// starts half full and sags all the time the boat is under way. What moves it:
//
//   - time. FallPerHour of game time, always, wherever she is.
//   - calling at a city. Any city, any size: +CityCall the moment the hull
//     comes alongside (src/trading/Docking.cs decides that, not this file).
//   - a pub night. Medium and large ports only: the crew drink until eight
//     the next morning and come back new men — morale full.
//   - a cannonball in the hull. -HitTaken a ball.
//   - sinking somebody. +KillMade a hull.
//
// And what it costs: below half, the crew work her slower — half morale and
// up is a full day's work, none of it is half speed (SpeedFactor).
//
// Godot-free on purpose (compiled into `dotnet test tests`); the wiring is in
// Main, the bar under the oars is ui/MoraleBar.cs. See docs/crew.md.
public sealed class CrewMorale
{
    public const float Start = 0.5f;
    public const float FallPerHour = 0.01f / 4f;   // 1% every four game hours
    public const float CityCall = 0.10f;           // stepping ashore anywhere
    public const float HitTaken = 0.05f;           // a ball in the hull
    public const float KillMade = 0.05f;           // another hull sent down

    // Speed: full at FullSpeedLevel and above, falling away in a straight
    // line to MinSpeedFactor at nothing left.
    public const float FullSpeedLevel = 0.5f;
    public const float MinSpeedFactor = 0.5f;

    // Pub night: ashore until PubEndHour the next morning — or the morning
    // after, if the crew are still up past PubLateHour, which is how a night
    // out can cost a day and a night.
    public const int PubEndHour = 8;
    public const int PubLateHour = 3;

    // These mirror GameTime, which the Godot-free test build cannot compile
    // (it reads Config, and Config is full of Godot types), so the pub-night
    // arithmetic keeps its own copy of the clock rather than drag that in.
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

    public float Level { get; private set; } = Start;

    /// The fraction of her best speed this crew will work the boat to.
    public float Speed => SpeedFactor(Level);

    public void Set(float level) => Level = Math.Clamp(level, 0f, 1f);
    private void Add(float delta) => Set(Level + delta);

    /// Game hours passing wear on them. Called with the same sailing hours
    /// the wind and the rowers' stamina run on, so fast-forward tires the
    /// crew's temper at the rate it eats the calendar.
    public void Update(float gameHours)
    {
        if (gameHours > 0) Add(-FallPerHour * gameHours);
    }

    /// The hull came alongside a city — any city, any size.
    public void CalledAt() => Add(CityCall);

    /// `balls` cannonballs landed in the hull.
    public void Hit(int balls = 1) => Add(-HitTaken * balls);

    /// Another vessel went down under our guns.
    public void Killed() => Add(KillMade);

    /// A night in the pub: they come back as good as new.
    public void PubNight() => Set(1f);

    /// Which ports have a pub worth the name: the same medium and large
    /// cities that have a harbour and a bank.
    public static bool HasPub(City city) => city.Size is "medium" or "large";

    /// How many game minutes a pub night eats: on to eight tomorrow morning,
    /// or the morning after that if it is already past three. Always > 0.
    public static long PubNightMinutes(long nowMinutes)
    {
        long sinceMidnight = nowMinutes % MinutesPerDay;
        long end = nowMinutes - sinceMidnight + PubEndHour * MinutesPerHour;
        if (sinceMidnight / MinutesPerHour >= PubLateHour) end += MinutesPerDay;
        return end - nowMinutes;
    }

    /// Speed as a fraction of the boat's best, for a morale level.
    public static float SpeedFactor(float level)
    {
        float l = Math.Clamp(level, 0f, 1f);
        if (l >= FullSpeedLevel) return 1f;
        return MinSpeedFactor + (1 - MinSpeedFactor) * l / FullSpeedLevel;
    }
}
