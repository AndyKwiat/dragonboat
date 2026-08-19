using System;

// Keeping a crew costs rice and coin as GAME time passes, so it only bites
// while sailing or trading. Each Due counts periods elapsed since the last
// check, catching up over big jumps.
public sealed class Due
{
    private readonly GameTime time;
    private readonly long period;
    private long nextDue;

    public Due(GameTime time, double periodMinutes)
    {
        this.time = time;
        period = Math.Max(1, (long)Math.Round(periodMinutes));
        nextDue = time.TotalMinutes + period;
    }

    public int Take()
    {
        long now = time.TotalMinutes;
        if (now < nextDue) return 0;
        int due = 0;
        while (now >= nextDue) { due++; nextDue += period; }
        return due;
    }

    public long MinutesToNext => Math.Max(0, nextDue - time.TotalMinutes);
}

// The crew eats: one unit of the ration product per crew member per period.
public sealed class Rations
{
    private readonly Player player;
    private readonly Due due;
    public Rations(Player player, GameTime time)
    {
        this.player = player;
        due = new Due(time, Config.RationsHoursPerUnit * GameTime.MinutesPerHour);
    }
    public int Update()
    {
        int meals = due.Take();
        return meals == 0 ? 0 : player.Consume(Config.RationsProductId, meals * player.CrewCount);
    }
    public int Remaining => player.Held(Config.RationsProductId);
}

// The crew gets paid daily; money may go negative.
public sealed class Wages
{
    private readonly Player player;
    private readonly Due due;
    public Wages(Player player, GameTime time)
    {
        this.player = player;
        due = new Due(time, GameTime.MinutesPerDay);
    }
    public int Update()
    {
        int days = due.Take();
        if (days == 0) return 0;
        int owed = days * Config.WagesMoneyPerDay * player.CrewCount;
        player.Money -= owed;
        return owed;
    }
}
