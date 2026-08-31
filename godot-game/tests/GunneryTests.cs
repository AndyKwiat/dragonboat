using System;
using Xunit;

namespace DragonboatGame.Tests;

public class GunneryTests
{
    // The game's ballistics (src/combat/CombatConfig.cs).
    private const float V = 70f, G = 30f;

    // Pinned against SolveLaunch's own closed form: max range is v²/g = 163.3.
    [Theory]
    [InlineData(60, 0.87)]
    [InlineData(100, 1.51)]
    [InlineData(120, 1.87)]
    [InlineData(150, 2.57)]
    public void FlightTimeMatchesTheBallistics(float distance, double expected) =>
        Assert.Equal(expected, Gunnery.FlightTime(distance, V, G), 2);

    [Fact]
    public void BeyondMaxRangeThereIsNoSolution()
    {
        Assert.False(float.IsNaN(Gunnery.FlightTime(163f, V, G)));
        Assert.True(float.IsNaN(Gunnery.FlightTime(170f, V, G)));
        Assert.False(Gunnery.Solve(0, 0, 170, 0, 0, 0, V, G, 1f, out _, out _));
    }

    // The control that makes the lead provable: with no lead at all this is
    // the moored fleet's naive aim, exactly.
    [Fact]
    public void NoLeadAimsWhereTheTargetIsRightNow()
    {
        Assert.True(Gunnery.Solve(0, 0, 100, 0, 0, 40, V, G, lead: 0f, out float x, out float y));
        Assert.Equal(100, x, 4);
        Assert.Equal(0, y, 4);
    }

    [Fact]
    public void AStationaryTargetIsNeverLed()
    {
        Assert.True(Gunnery.Solve(0, 0, 100, 0, 0, 0, V, G, lead: 1f, out float x, out float y));
        Assert.Equal(100, x, 4);
        Assert.Equal(0, y, 4);
    }

    // A boat under oars crosses the line of fire at ~43 units/s. Leading it
    // is not just "speed x flight time": the lead opens the range, which
    // lengthens the flight, which lengthens the lead. At 100 units that
    // compounds a naive 65 into nearly 90.
    [Fact]
    public void ACrossingTargetIsLedByMoreThanItsNaiveTravel()
    {
        Assert.True(Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, lead: 1f, out float x, out float y));
        Assert.Equal(100, x, 3);
        Assert.True(y > 43 * Gunnery.FlightTime(100, V, G), "the lead must account for the range it opens");
        Assert.InRange(y, 80f, 95f);
    }

    // At the default lead the loop settles: a fourth pass would barely move
    // the answer, which is what makes three enough.
    [Fact]
    public void TheSolutionSettlesAtTheDefaultLead()
    {
        float lead = AiConfig.AiLeadFraction;
        Assert.True(Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, lead, out float x, out float y));
        float again = 43 * Gunnery.FlightTime(MathF.Sqrt(x * x + y * y), V, G) * lead;
        Assert.True(MathF.Abs(again - y) < 3f, $"another pass moved the aim by {MathF.Abs(again - y)}");
    }

    // A target fast enough to outrun the ball has no exact solution at all:
    // every extra unit of lead opens the range and buys the target more
    // time. Three passes under-lead it rather than chasing a fixed point
    // that is not there, which is the right way to be wrong — that shot was
    // never going to land, and a fast crossing target SHOULD be hard.
    [Fact]
    public void AnUnhittableCrossingTargetIsUnderLedRatherThanRunaway()
    {
        Assert.True(Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, lead: 1f, out _, out float y));
        float next = 43 * Gunnery.FlightTime(MathF.Sqrt(100 * 100 + y * y), V, G);
        Assert.True(next > y, "at full lead against a fast crosser the iteration is still climbing");
        Assert.True(y < 163f, "but it stays inside the gun's reach rather than running away");
    }

    [Fact]
    public void MoreLeadAimsFurtherAhead()
    {
        Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, 1f, out _, out float full);
        Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, 0.5f, out _, out float half);
        Gunnery.Solve(0, 0, 100, 0, 0, 43, V, G, 0f, out _, out float none);
        Assert.Equal(0, none, 4);
        Assert.True(none < half && half < full, $"none {none} half {half} full {full}");
    }
}
