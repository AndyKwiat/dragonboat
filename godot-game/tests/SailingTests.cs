using System;
using Xunit;

namespace DragonboatGame.Tests;

// The sails-up / sails-down rules: what the wind gives a boat on a heading,
// and how the rowers tire and recover. docs/boat.md.
public class SailingTests
{
    private const float Beat = 0.35f;

    private static Wind Steady(float bearingDeg, float force = 20f) => new(bearingDeg, force, 0, 0);

    [Fact]
    public void DeadDownwindCatchesTheWholeWind()
    {
        var w = Steady(90);   // blowing towards +y
        var (x, y) = w.ForceOn(MathF.PI / 2, Beat);   // sailing +y
        Assert.Equal(0, x, 3);
        Assert.Equal(20, y, 3);
    }

    [Fact]
    public void BeamReachCatchesNothing()
    {
        var w = Steady(90);
        var (x, y) = w.ForceOn(0, Beat);   // sailing +x, wind on the beam
        Assert.Equal(0, x, 3);
        Assert.Equal(0, y, 3);
    }

    [Fact]
    public void BeatingIntoItPushesYouBack()
    {
        var w = Steady(90);
        var (_, y) = w.ForceOn(-MathF.PI / 2, Beat);   // sailing straight into it
        // The push stays along the wind — +y, i.e. astern of us — but weak.
        Assert.Equal(20 * Beat, y, 3);
    }

    [Fact]
    public void QuarteringWindSetsYouSideways()
    {
        var w = Steady(90);
        var (x, y) = w.ForceOn(MathF.PI / 4, Beat);   // 45° off the wind
        Assert.Equal(0, x, 3);                        // the force is along the wind...
        Assert.Equal(20 * MathF.Cos(MathF.PI / 4), y, 3);   // ...and only partly earned
    }

    [Fact]
    public void BearingSwingsThereAndBackOverSwingHours()
    {
        var w = new Wind(180, 20, 60, 96);
        Assert.Equal(180, w.BearingDeg, 3);
        w.Advance(24);                       // a quarter of the swing
        Assert.Equal(240, w.BearingDeg, 3);
        w.Advance(24);
        Assert.Equal(180, w.BearingDeg, 3);
        w.Advance(24);
        Assert.Equal(120, w.BearingDeg, 3);
        w.Advance(24);
        Assert.Equal(180, w.BearingDeg, 3);
    }

    [Fact]
    public void TowardsNamesTheCompassPoint()
    {
        Assert.Equal("E", Steady(0).Towards());
        Assert.Equal("S", Steady(90).Towards());
        Assert.Equal("W", Steady(180).Towards());
        Assert.Equal("N", Steady(-90).Towards());
        Assert.Equal("NE", Steady(315).Towards());
    }

    [Fact]
    public void RowingEmptiesTheCrewOverRowHours()
    {
        var crew = new CrewStamina(72, 36);
        Assert.Equal(1f, crew.Level, 3);
        crew.Update(36, rowing: true);
        Assert.Equal(0.5f, crew.Level, 3);
        crew.Update(36, rowing: true);
        Assert.Equal(0f, crew.Level, 3);
        Assert.True(crew.Exhausted);
    }

    [Fact]
    public void StaminaStopsAtZeroAndAtFull()
    {
        var crew = new CrewStamina(72, 36);
        crew.Update(1000, rowing: true);
        Assert.Equal(0f, crew.Level, 3);
        crew.Update(1000, rowing: false);
        Assert.Equal(1f, crew.Level, 3);
        Assert.False(crew.Exhausted);
    }

    [Fact]
    public void RestingRefillsFasterThanRowingDrains()
    {
        var crew = new CrewStamina(72, 36);
        crew.Update(36, rowing: true);        // half spent
        crew.Update(9, rowing: false);        // a quarter of a rest
        Assert.Equal(0.75f, crew.Level, 3);
        Assert.False(crew.Rowing);
    }
}
