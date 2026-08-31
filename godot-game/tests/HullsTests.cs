using System;
using Xunit;

namespace DragonboatGame.Tests;

public class HullsTests
{
    // The boat the game has today: 12 long, 4 in the beam.
    private static Hull Boat(float x, float y, float heading = 0, float length = 12, float radius = 2, float mass = 48) =>
        new(x, y, heading, length, radius, mass);

    [Fact]
    public void TheShellIsThreeCirclesAlongTheKeel()
    {
        Span<(float X, float Y)> c = stackalloc (float, float)[Hull.CircleCount];
        Boat(0, 0, heading: 0).Circles(c);
        Assert.Equal(-4, c[0].X, 3);   // astern
        Assert.Equal(0, c[1].X, 3);
        Assert.Equal(4, c[2].X, 3);    // bow
        foreach (var p in c) Assert.Equal(0, p.Y, 3);
    }

    [Fact]
    public void TheShellTurnsWithTheHull()
    {
        Span<(float X, float Y)> c = stackalloc (float, float)[Hull.CircleCount];
        Boat(0, 0, heading: MathF.PI / 2).Circles(c);   // pointing south (world y is down)
        Assert.Equal(0, c[2].X, 3);
        Assert.Equal(4, c[2].Y, 3);
    }

    [Fact]
    public void HullsWellApartDoNotTouch()
    {
        Assert.False(Hulls.Overlap(Boat(0, 0), Boat(100, 0), out _, out _, out _));
        // Just outside reach: 12/2 + 2 = 8 each, so 16 apart is clear.
        Assert.False(Hulls.Overlap(Boat(0, 0), Boat(16.1f, 0), out _, out _, out _));
    }

    [Fact]
    public void HullsAbeamOfEachOtherPushApartSideways()
    {
        // Both pointing east, one three units north of the other: the circles
        // overlap by a unit and the push is straight across.
        Assert.True(Hulls.Overlap(Boat(0, 0), Boat(0, 3), out float nx, out float ny, out float depth));
        Assert.Equal(0, nx, 3);
        Assert.Equal(1, ny, 3);          // b is pushed further north
        Assert.Equal(1, depth, 3);       // 2 + 2 touching, 3 apart
    }

    [Fact]
    public void HullsBowToSternPushApartAlongTheKeel()
    {
        Assert.True(Hulls.Overlap(Boat(0, 0), Boat(11, 0), out float nx, out float ny, out float depth));
        Assert.Equal(1, nx, 3);
        Assert.Equal(0, ny, 3);
        // Bow circle at +4 against the other's stern circle at 11-4 = 7.
        Assert.Equal(1, depth, 3);
    }

    // Two hulls exactly on top of each other have no direction to separate
    // along; without a fallback the normal would be zero and they would stay
    // welded together for ever.
    [Fact]
    public void HullsExactlyOnTopOfEachOtherStillFindAWayOut()
    {
        Assert.True(Hulls.Overlap(Boat(50, 50), Boat(50, 50), out float nx, out float ny, out float depth));
        Assert.Equal(1, MathF.Sqrt(nx * nx + ny * ny), 3);   // a real direction
        Assert.True(depth > 0);
    }

    [Fact]
    public void TheNormalAlwaysPointsFromTheFirstHullToTheSecond()
    {
        Hulls.Overlap(Boat(0, 0), Boat(0, 3), out float nx1, out float ny1, out _);
        Hulls.Overlap(Boat(0, 3), Boat(0, 0), out float nx2, out float ny2, out _);
        Assert.Equal(nx1, -nx2, 3);
        Assert.Equal(ny1, -ny2, 3);
    }

    [Fact]
    public void ABiggerHullGivesWayLess()
    {
        var (small, big) = Hulls.Share(massA: 10, massB: 90);
        Assert.Equal(0.9f, small, 3);    // the light one moves nine tenths of it
        Assert.Equal(0.1f, big, 3);
        var (a, b) = Hulls.Share(48, 48);
        Assert.Equal(0.5f, a, 3);
        Assert.Equal(0.5f, b, 3);
    }

    // Different classes of vessel are the point of carrying size per hull.
    [Fact]
    public void ALongerHullReachesFurther()
    {
        var launch = Boat(0, 0, length: 8, radius: 1.5f);
        var trader = Boat(0, 0, length: 30, radius: 5);
        Assert.Equal(5.5f, launch.Reach, 3);
        Assert.Equal(20f, trader.Reach, 3);
        // A trader lying 18 units off overlaps the launch; another launch would not.
        Assert.True(Hulls.Overlap(launch, Boat(18, 0, length: 30, radius: 5), out _, out _, out _));
        Assert.False(Hulls.Overlap(launch, Boat(18, 0, length: 8, radius: 1.5f), out _, out _, out _));
    }
}
