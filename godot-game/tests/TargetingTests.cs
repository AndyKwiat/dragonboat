using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

public class TargetingTests
{
    private static AiSituation At(float x, float y, int team = 0) =>
        new(Dt: 0.1f, Team: team, X: x, Y: y, Heading: 0, Speed: 0,
            Target: null, Range: 0, Bearing: 0, LineOfSight: true, LoadedGuns: 7);

    private static AiContact Enemy(int id, float x, float y, int team = 1) => new(id, team, x, y, 0, 0, 0);

    // A guard stationed at the origin.
    private static (ProtectOrder order, AiMemory memory) Guard()
    {
        var m = new AiMemory { StationX = 0, StationY = 0 };
        return (new ProtectOrder(0, 0, new StandOffTactic()), m);
    }

    [Fact]
    public void AFriendIsNeverATarget()
    {
        var (order, m) = Guard();
        var contacts = new List<AiContact> { Enemy(1, 10, 0, team: 0) };   // same side
        Assert.Null(Targeting.Pick(0, 0, 0, contacts, order, m, At(0, 0)));
    }

    [Fact]
    public void AcquiresInsideTheEngageRingAndNotOutsideIt()
    {
        var (order, m) = Guard();
        float r = AiConfig.AiEngageRadius;
        Assert.NotNull(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, r - 1, 0) }, order, m, At(0, 0)));
        var (order2, m2) = Guard();
        Assert.Null(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, r + 1, 0) }, order2, m2, At(0, 0)));
    }

    [Fact]
    public void OnceAcquiredATargetIsKeptOutToTheDisengageRing()
    {
        var (order, m) = Guard();
        var near = new List<AiContact> { Enemy(1, 100, 0) };
        Assert.NotNull(Targeting.Pick(0, 0, 0, near, order, m, At(0, 0)));
        m.SecondsEngaged = 99;   // past the commit window, so distance decides

        // Between the rings: acquired, so kept — even though it could not be
        // acquired from here.
        float between = (AiConfig.AiEngageRadius + AiConfig.AiDisengageRadius) / 2;
        Assert.NotNull(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, between, 0) }, order, m, At(0, 0)));
        // Beyond the far ring: gone.
        Assert.Null(Targeting.Pick(0, 0, 0,
            new List<AiContact> { Enemy(1, AiConfig.AiDisengageRadius + 1, 0) }, order, m, At(0, 0)));
    }

    // A target loitering exactly on the far ring must not flicker.
    [Fact]
    public void ATargetIsCommittedToForAFewSecondsWhateverItDoes()
    {
        var (order, m) = Guard();
        Assert.NotNull(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, 100, 0) }, order, m, At(0, 0)));
        var far = new List<AiContact> { Enemy(1, 900, 0) };
        m.SecondsEngaged = AiConfig.AiEngageMinSec / 2;
        Assert.NotNull(Targeting.Pick(0, 0, 0, far, order, m, At(0, 0)));   // still committed
        m.SecondsEngaged = AiConfig.AiEngageMinSec + 0.1f;
        Assert.Null(Targeting.Pick(0, 0, 0, far, order, m, At(0, 0)));      // now dropped
    }

    // THE one that matters: the radii are measured from the station, so the
    // boat's own charging about cannot make it acquire and drop in a loop.
    [Fact]
    public void ChasingDoesNotChangeWhoIsWorthChasing()
    {
        var (order, m) = Guard();
        var parked = new List<AiContact> { Enemy(1, 250, 0) };   // between the rings, from the station
        m.SecondsEngaged = 99;

        // Never acquirable from the station, wherever the boat itself goes...
        foreach (float boatX in new[] { 0f, 100f, 249f, 400f, -300f })
            Assert.Null(Targeting.Pick(0, boatX, 0, parked, order, m, At(boatX, 0)));

        // ...and once acquired inside the ring, kept wherever the boat goes.
        var (order2, m2) = Guard();
        Assert.NotNull(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, 100, 0) }, order2, m2, At(0, 0)));
        m2.SecondsEngaged = 99;
        foreach (float boatX in new[] { 0f, 50f, 300f, -200f })
            Assert.NotNull(Targeting.Pick(0, boatX, 0, parked, order2, m2, At(boatX, 0)));
    }

    [Fact]
    public void TheNearestHostileWins()
    {
        var (order, m) = Guard();
        var contacts = new List<AiContact> { Enemy(1, 150, 0), Enemy(2, 40, 0), Enemy(3, 90, 0) };
        Assert.Equal(2, Targeting.Pick(0, 0, 0, contacts, order, m, At(0, 0))!.Value.Id);
    }

    [Fact]
    public void AnOrderThatDoesNotFightNeverPicksATarget()
    {
        var m = new AiMemory();
        Assert.Null(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, 10, 0) }, new IdleOrder(), m, At(0, 0)));
        Assert.Null(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, 10, 0) }, new VoyageOrder(), m, At(0, 0)));
    }

    [Fact]
    public void ATargetThatVanishesIsForgotten()
    {
        var (order, m) = Guard();
        Assert.NotNull(Targeting.Pick(0, 0, 0, new List<AiContact> { Enemy(1, 100, 0) }, order, m, At(0, 0)));
        Assert.Null(Targeting.Pick(0, 0, 0, new List<AiContact>(), order, m, At(0, 0)));
        Assert.Null(m.TargetId);
    }
}
