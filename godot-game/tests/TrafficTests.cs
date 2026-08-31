using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

// Keeping consorts out of each other's water.
public class TrafficTests
{
    private const float Clear = 24f;
    private static List<Berth> None => new();

    [Fact]
    public void EmptyWaterIsLeftAlone()
    {
        var (x, y) = Traffic.MakeRoom(100, 50, Clear, None, null);
        Assert.Equal(100, x, 3);
        Assert.Equal(50, y, 3);
    }

    [Fact]
    public void SomebodyElsesWaterIsLeftAloneToo()
    {
        var taken = new List<Berth> { new(0, 0) };
        var (x, y) = Traffic.MakeRoom(100, 50, Clear, taken, null);
        Assert.Equal(100, x, 3);
        Assert.Equal(50, y, 3);
    }

    // With nothing to circle, a blocked boat stops short of the obstruction
    // rather than sailing into it.
    [Fact]
    public void WithNothingToCircleSheStopsShort()
    {
        var taken = new List<Berth> { new(100, 0) };
        var (x, y) = Traffic.MakeRoom(100, 0, Clear, taken, null);
        float gap = MathF.Sqrt((x - 100) * (x - 100) + y * y);
        Assert.Equal(Clear, gap, 2);
        Assert.True(Traffic.IsClear(x, y, Clear, taken));
    }

    // The case this exists for: three boats closing on one enemy all want the
    // same spot off her beam, and should end up spread around her instead of
    // in a heap — at the same range, because the range is what the tactic
    // worked out.
    [Fact]
    public void BoatsCirclingOneEnemySpreadOutAtTheSameRange()
    {
        var enemy = (X: 0f, Y: 0f);
        var wanted = (X: 110f, Y: 0f);          // off her beam, at fighting range
        var taken = new List<Berth>();
        var berths = new List<(float X, float Y)>();
        for (int i = 0; i < 3; i++)
        {
            var spot = Traffic.MakeRoom(wanted.X, wanted.Y, Clear, taken, enemy);
            berths.Add(spot);
            taken.Add(new Berth(spot.X, spot.Y));
        }
        // All three still at fighting range...
        foreach (var b in berths)
            Assert.Equal(110f, MathF.Sqrt(b.X * b.X + b.Y * b.Y), 1);
        // ...and none of them on top of another.
        for (int i = 0; i < berths.Count; i++)
            for (int j = i + 1; j < berths.Count; j++)
            {
                float dx = berths[i].X - berths[j].X, dy = berths[i].Y - berths[j].Y;
                Assert.True(MathF.Sqrt(dx * dx + dy * dy) >= Clear - 0.01f,
                    $"berths {i} and {j} are on top of each other");
            }
    }

    // It should give way by the least it can: the first free slot round the
    // pivot, not the far side of the map.
    [Fact]
    public void SheGivesWayByTheLeastSheCan()
    {
        var taken = new List<Berth> { new(110, 0) };
        var (x, y) = Traffic.MakeRoom(110, 0, Clear, taken, (0f, 0f));
        float turned = MathF.Abs(MathF.Atan2(y, x)) * 180f / MathF.PI;
        Assert.InRange(turned, 1f, 40f);       // a nudge round, not a lap
        Assert.True(Traffic.IsClear(x, y, Clear, taken));
    }

    [Fact]
    public void ARingThatIsCompletelyFullFallsBackToStandingOff()
    {
        // Ring every angle at fighting range, so there is nowhere to slide to.
        var taken = new List<Berth>();
        for (int deg = 0; deg < 360; deg += 5)
        {
            float a = deg * MathF.PI / 180f;
            taken.Add(new Berth(MathF.Cos(a) * 110, MathF.Sin(a) * 110));
        }
        var (x, y) = Traffic.MakeRoom(110, 0, Clear, taken, (0f, 0f));
        // No exception, no NaN, and it has moved off the blocked spot.
        Assert.False(float.IsNaN(x) || float.IsNaN(y));
        Assert.True(MathF.Abs(x - 110) > 0.01f || MathF.Abs(y) > 0.01f);
    }

    [Fact]
    public void IsClearIsAboutDistanceNotIdentity()
    {
        var taken = new List<Berth> { new(0, 0) };
        Assert.False(Traffic.IsClear(0, 0, 10, taken));
        Assert.False(Traffic.IsClear(9.9f, 0, 10, taken));
        Assert.True(Traffic.IsClear(10.1f, 0, 10, taken));
    }
}
