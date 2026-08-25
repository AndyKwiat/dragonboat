using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

public class DockingTests
{
    private static readonly City Port = new() { Name = "Port", X = 1000, Y = 1000, Cells = 100, Size = "large" };
    private static readonly City Next = new() { Name = "Next", X = 1200, Y = 1000, Cells = 60, Size = "small" };
    private const float Frame = 1 / 60f, Cooldown = 5f;

    [Fact]
    public void OpensTheMarketOnContact()
    {
        var docking = new Docking(Cooldown);
        Assert.Null(docking.Update(Frame, null, false));
        Assert.Same(Port, docking.Update(Frame, Port, false));
    }

    [Fact]
    public void DoesNotReopenWhileStillAtTheQuay()
    {
        var docking = new Docking(Cooldown);
        docking.Update(Frame, Port, false);          // opens
        docking.Update(Frame, Port, true);           // market open, boat pinned
        // Closed, but the hull never left: no reopening, however long we wait.
        for (int i = 0; i < 60 * 30; i++) Assert.Null(docking.Update(Frame, Port, false));
    }

    [Fact]
    public void CooldownBlocksBumpingStraightBackIn()
    {
        var docking = new Docking(Cooldown);
        docking.Update(Frame, Port, false);
        docking.Update(Frame, Port, true);
        docking.Update(Frame, Port, false);          // closed here: cooldown starts
        Assert.Equal(Cooldown, docking.CooldownLeft, 3);
        // Leave and come straight back — the cooldown still has it.
        Assert.Null(docking.Update(Frame, null, false));
        for (float t = 0; t < Cooldown - 0.5f; t += Frame)
            Assert.Null(docking.Update(Frame, Port, false));
    }

    [Fact]
    public void ReopensAfterLeavingAndTheCooldownLapsing()
    {
        var docking = new Docking(Cooldown);
        docking.Update(Frame, Port, false);
        docking.Update(Frame, Port, true);
        docking.Update(Frame, Port, false);          // closed
        for (float t = 0; t < Cooldown + Frame; t += Frame) docking.Update(Frame, null, false);
        Assert.Equal(0, docking.CooldownLeft);
        Assert.Same(Port, docking.Update(Frame, Port, false));
    }

    [Fact]
    public void TheCooldownAlsoCoversTheCityNextDoor()
    {
        var docking = new Docking(Cooldown);
        docking.Update(Frame, Port, false);
        docking.Update(Frame, Port, true);
        docking.Update(Frame, Port, false);          // closed
        Assert.Null(docking.Update(Frame, Next, false));   // a different quay, still too soon
        for (float t = 0; t < Cooldown; t += Frame) docking.Update(Frame, null, false);
        Assert.Same(Next, docking.Update(Frame, Next, false));
    }

    [Fact]
    public void NeverOpensOverAnAlreadyOpenMarket()
    {
        var docking = new Docking(Cooldown);
        // The player clicked a label: the market is open before any contact.
        Assert.Null(docking.Update(Frame, Port, true));
        Assert.Null(docking.Update(Frame, Port, true));
        // Closing it starts the cooldown, and the hull is still at the quay.
        Assert.Null(docking.Update(Frame, Port, false));
        Assert.Equal(Cooldown, docking.CooldownLeft, 3);
    }

    [Fact]
    public void ContactUsesTheFootprintNotTheCentre()
    {
        var cities = new List<City> { Port };
        // 100-cell footprint: the edge is 50 from the centre.
        Assert.Same(Port, CityPlacer.Touching(cities, 1000 + 50 + 7, 1000, 8));
        Assert.Null(CityPlacer.Touching(cities, 1000 + 50 + 9, 1000, 8));
        Assert.Same(Port, CityPlacer.Touching(cities, 1000, 1000, 8));   // inside
        Assert.Equal(0, CityPlacer.DistanceTo(Port, 1000, 1000));
        // Corners measure diagonally, not by the wider of the two axes.
        Assert.Equal(MathF.Sqrt(2) * 6, CityPlacer.DistanceTo(Port, 1056, 1056), 3);
    }

    [Fact]
    public void PicksTheNearestCityWhenTwoAreInReach()
    {
        var cities = new List<City> { Port, Next };
        // Between the two footprints, closer to Next's (edge at 1170).
        Assert.Same(Next, CityPlacer.Touching(cities, 1165, 1000, 60));
        Assert.Same(Port, CityPlacer.Touching(cities, 1055, 1000, 60));
    }
}
