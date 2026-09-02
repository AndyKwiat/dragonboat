using Xunit;

namespace DragonboatGame.Tests;

// Crew morale: the sag, the lifts, what it costs in speed, and how long a
// night in the pub takes off the clock (src/crew/Morale.cs).
public class MoraleTests
{
    private static long Minutes(int day, int hour, int minute = 0) =>
        (day - 1) * 24L * 60 + hour * 60 + minute;

    [Fact]
    public void StartsHalfFull()
    {
        Assert.Equal(0.5f, new CrewMorale().Level, 5);
    }

    [Fact]
    public void FallsOnePercentEveryFourHours()
    {
        var morale = new CrewMorale();
        morale.Update(4);
        Assert.Equal(0.49f, morale.Level, 5);
        morale.Update(40);      // ten more percent over ten four-hour watches
        Assert.Equal(0.39f, morale.Level, 5);
    }

    [Fact]
    public void CallingAtACityLiftsItTenPercent()
    {
        var morale = new CrewMorale();
        morale.CalledAt();
        Assert.Equal(0.6f, morale.Level, 5);
    }

    [Fact]
    public void HitsCostFiveEachAndKillsPayFive()
    {
        var morale = new CrewMorale();
        morale.Hit();
        Assert.Equal(0.45f, morale.Level, 5);
        morale.Hit(3);
        Assert.Equal(0.30f, morale.Level, 5);
        morale.Killed();
        Assert.Equal(0.35f, morale.Level, 5);
    }

    [Fact]
    public void APubNightFillsIt()
    {
        var morale = new CrewMorale();
        morale.Hit(8);
        Assert.Equal(0.10f, morale.Level, 5);
        morale.PubNight();
        Assert.Equal(1f, morale.Level, 5);
    }

    [Fact]
    public void NeverLeavesZeroToOne()
    {
        var morale = new CrewMorale();
        morale.Hit(100);
        Assert.Equal(0f, morale.Level, 5);
        morale.Update(1000);
        Assert.Equal(0f, morale.Level, 5);
        for (int i = 0; i < 100; i++) morale.CalledAt();
        Assert.Equal(1f, morale.Level, 5);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(0.75f, 1f)]
    [InlineData(0.5f, 1f)]        // half morale is still a full day's work
    [InlineData(0.25f, 0.75f)]
    [InlineData(0f, 0.5f)]        // and none of it is half speed
    public void SpeedFallsAwayOnlyBelowHalf(float level, float speed)
    {
        Assert.Equal(speed, CrewMorale.SpeedFactor(level), 5);
    }

    [Fact]
    public void SpeedTracksTheLevel()
    {
        var morale = new CrewMorale();
        Assert.Equal(1f, morale.Speed, 5);
        morale.Hit(5);            // 25%
        Assert.Equal(0.75f, morale.Speed, 5);
    }

    // The pub shuts them out at eight in the morning; being up past three
    // costs the whole of the next day too.
    [Theory]
    [InlineData(20, 0, 12 * 60)]        // 20:00 → 08:00 tomorrow
    [InlineData(23, 30, 8 * 60 + 30)]   // half past eleven → 08:00
    [InlineData(1, 0, 7 * 60)]          // 01:00, still before three → 08:00 today
    [InlineData(2, 59, 5 * 60 + 1)]
    [InlineData(3, 0, 29 * 60)]         // three exactly: a day and a night
    [InlineData(5, 0, 27 * 60)]
    [InlineData(8, 0, 24 * 60)]         // eight in the morning: a full day
    [InlineData(9, 0, 23 * 60)]
    public void PubNightRunsToEightTheNextMorning(int hour, int minute, long expected)
    {
        Assert.Equal(expected, CrewMorale.PubNightMinutes(Minutes(3, hour, minute)));
    }

    [Fact]
    public void PubNightAlwaysMovesTheClockForward()
    {
        for (long m = 0; m < 24 * 60; m++)
            Assert.True(CrewMorale.PubNightMinutes(Minutes(1, 0) + m) > 0);
    }

    [Fact]
    public void OnlyMediumAndLargeCitiesHaveAPub()
    {
        Assert.False(CrewMorale.HasPub(new City { Size = "small" }));
        Assert.True(CrewMorale.HasPub(new City { Size = "medium" }));
        Assert.True(CrewMorale.HasPub(new City { Size = "large" }));
    }
}
