using System;

// Game time: 30-day months that keep counting up, a 24-hour clock. Time only
// moves while the player is under way — nothing advances it per frame on its
// own, and trading is instantaneous.
public sealed class GameTime
{
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
    public const int DaysPerMonth = 30;
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay;
    public const int MinutesPerMonth = MinutesPerDay * DaysPerMonth;

    public readonly record struct Stamp(int Month, int Day, int Hour, int Minute);

    // Fractional minutes accumulate while sailing; only whole ones are shown.
    private double minutes;

    public GameTime()
    {
        minutes = (Config.StartMonth - 1) * (double)MinutesPerMonth
                + (Config.StartDay - 1) * (double)MinutesPerDay
                + Config.StartHour * (double)MinutesPerHour
                + Config.StartMinute;
    }

    public long TotalMinutes => (long)Math.Floor(minutes);

    public void Advance(double m) { if (m > 0) minutes += m; }
    public void AdvanceHours(double h) => Advance(h * MinutesPerHour);

    public static Stamp StampFor(double totalMinutes)
    {
        long t = (long)Math.Floor(totalMinutes);
        return new Stamp(
            (int)(t / MinutesPerMonth) + 1,
            (int)((t % MinutesPerMonth) / MinutesPerDay) + 1,
            (int)((t % MinutesPerDay) / MinutesPerHour),
            (int)(t % MinutesPerHour));
    }

    public Stamp Now => StampFor(minutes);

    // Day boundaries, for things that tick over at midnight (story events).
    public static long StartOfDay(long totalMinutes) => totalMinutes / MinutesPerDay * MinutesPerDay;
    public static long MidnightAfter(long totalMinutes) => StartOfDay(totalMinutes) + MinutesPerDay;

    // Night wraps past midnight, so the test is an OR, not a range.
    public bool IsNight => IsNightHour(Now.Hour, Config.NightStartHour, Config.NightEndHour);
    public static bool IsNightHour(int hour, int start, int end) => hour >= start || hour < end;

    public string Format()
    {
        var s = Now;
        return $"Month {s.Month}, Day {s.Day}   {s.Hour:D2}:{s.Minute:D2}";
    }

    // How fast sailing burns the clock at a given zoom: linear between the
    // two anchors, flat outside them.
    public static float TimeScaleForZoom(float zoom)
    {
        float span = Config.ZoomTimeNearZoom - Config.ZoomTimeFarZoom;
        float t = span <= 0 ? 0 : Math.Clamp((Config.ZoomTimeNearZoom - zoom) / span, 0f, 1f);
        return Config.ZoomTimeNearRate + (Config.ZoomTimeFarRate - Config.ZoomTimeNearRate) * t;
    }
}
