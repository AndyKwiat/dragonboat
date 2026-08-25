// Two small things the systems that tick over at midnight share: whole-day
// arithmetic, and the shape of a line for the event log (ui/EventLog.cs).
// No Godot types (nor GameTime, which reaches Config) — the rules files that
// use these are compiled straight into tests/.

// Something the world did to the player while they were sailing.
public readonly record struct NewsEvent(string Text, bool Bad);

// Loans and VIP passengers both count whole days: they fall due, expire and
// charge at midnights, so their rules take an absolute day index (day 0 =
// the clock's first day) instead of the clock's minutes.
public static class Days
{
    public const int MinutesPerDay = 24 * 60;

    public static int Of(long totalMinutes) => (int)(totalMinutes / MinutesPerDay);
}

// The midnight gate every daily rule sits behind: Turned() is false until
// the day index moves, and the very first call only anchors it (so nothing
// fires for the day the game — or a loan, or a passenger — began on).
public sealed class DayTurn
{
    private int last = int.MinValue;

    public bool Turned(int today)
    {
        if (today == last) return false;
        bool first = last == int.MinValue;
        last = today;
        return !first;
    }
}
