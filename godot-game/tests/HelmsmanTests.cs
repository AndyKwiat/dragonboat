using System;
using Xunit;

namespace DragonboatGame.Tests;

public class HelmsmanTests
{
    private const float East = 0, South = MathF.PI / 2, West = MathF.PI, North = -MathF.PI / 2;

    [Fact]
    public void SteersTowardsSomethingOffTheStarboardBow()
    {
        // Pointing east, target to the south: turn to starboard (headings
        // turn +x towards +y), and keep pulling.
        var helm = Helmsman.SteerTo(0, 0, East, 10, 10);
        Assert.True(helm.Starboard);
        Assert.False(helm.Port);
        Assert.True(helm.Ahead);
    }

    [Fact]
    public void SteersTowardsSomethingOffThePortBow()
    {
        var helm = Helmsman.SteerTo(0, 0, East, 10, -10);
        Assert.True(helm.Port);
        Assert.False(helm.Starboard);
        Assert.True(helm.Ahead);
    }

    [Fact]
    public void DeadAheadMeansNoTiller()
    {
        var helm = Helmsman.SteerTo(0, 0, East, 100, 0);
        Assert.False(helm.Port);
        Assert.False(helm.Starboard);
        Assert.True(helm.Ahead);
        Assert.False(helm.Astern);
    }

    // The one that matters for a river: never turn the long way round.
    [Fact]
    public void TurnsTheShortWayRound()
    {
        // Pointing a little west of north, target a little east of north:
        // that is a small turn to starboard, not a 340-degree sweep to port.
        float heading = North - 0.2f;
        float tx = MathF.Cos(North + 0.2f) * 100, ty = MathF.Sin(North + 0.2f) * 100;
        float error = Helmsman.Bearing(0, 0, heading, tx, ty);
        Assert.InRange(error, 0.3f, 0.5f);
        Assert.True(Helmsman.SteerTo(0, 0, heading, tx, ty).Starboard);
        // ...and the mirror image turns to port, not the long way round.
        Assert.True(Helmsman.SteerTo(0, 0, North + 0.2f, MathF.Cos(North - 0.2f) * 100,
            MathF.Sin(North - 0.2f) * 100).Port);
    }

    [Fact]
    public void BearingIsAlwaysTheSignedShortestAngle()
    {
        foreach (float heading in new[] { East, South, West, North, 3f, -3f })
            foreach (float wanted in new[] { East, South, West, North, 2f, -2f })
            {
                float error = Helmsman.Bearing(0, 0, heading, MathF.Cos(wanted) * 50, MathF.Sin(wanted) * 50);
                Assert.InRange(error, -MathF.PI - 0.001f, MathF.PI + 0.001f);
            }
    }

    // A target dead astern is not something to row towards: back the oars,
    // spin on the spot, and only then pull.
    [Fact]
    public void SomethingAsternIsTurnedToBeforeRowing()
    {
        var helm = Helmsman.SteerTo(0, 0, East, -100, 1);
        Assert.False(helm.Ahead);
        Assert.True(helm.Port || helm.Starboard);
    }

    [Fact]
    public void ArrivalIsWithinABoatLength()
    {
        Assert.True(Helmsman.Reached(0, 0, 5, 5));            // 7.1 units off
        Assert.False(Helmsman.Reached(0, 0, 20, 0));
        Assert.True(Helmsman.Reached(0, 0, 20, 0, radius: 25));
    }
}
