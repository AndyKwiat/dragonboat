using System;
using Xunit;

namespace DragonboatGame.Tests;

// The orders and the tactic: what a boat decides to do, given what it can
// see. All of it Godot-free, so a fight is a table of numbers rather than a
// game that has to be watched.
public class AiOrderTests
{
    private const float Deg = MathF.PI / 180f;

    private static AiSituation Sees(float x, float y, float heading, AiContact? target,
        float range = 0, float bearingDeg = 0, float speed = 0) =>
        new(Dt: 0.1f, Team: 0, X: x, Y: y, Heading: heading, Speed: speed,
            Target: target, Range: range, Bearing: bearingDeg * Deg, LineOfSight: true, LoadedGuns: 7);

    private static AiContact At(float x, float y) => new(1, 1, x, y, 0, 0, 0);

    private static (ProtectOrder o, AiMemory m) Guard(float sx = 0, float sy = 0)
    {
        var m = new AiMemory { StationX = sx, StationY = sy };
        return (new ProtectOrder(sx, sy, new StandOffTactic()), m);
    }

    // ---- ProtectOrder, with nothing to fight -------------------------------

    [Fact]
    public void OnStationWithNothingToFightSheHolds()
    {
        var (order, m) = Guard();
        var goal = AiPilot.Decide(Sees(0, 0, 0, null), m, order);
        Assert.Equal(AiState.Holding, m.State);
        Assert.False(goal.KeepWay);
        Assert.False(goal.OpenFire);
        Assert.Equal(0, goal.X);
    }

    [Fact]
    public void OffStationWithNothingToFightSheSailsHome()
    {
        var (order, m) = Guard();
        var goal = AiPilot.Decide(Sees(200, 0, 0, null), m, order);
        Assert.Equal(AiState.Sailing, m.State);
        Assert.True(goal.KeepWay);
        Assert.Equal(0, goal.X);      // the station
        Assert.Equal(0, goal.Y);
    }

    [Fact]
    public void TheLeashKeepsHerNearWhatSheGuards()
    {
        var (order, _) = Guard();
        var (x, y) = order.Leash(1000, 0);
        Assert.Equal(AiConfig.AiLeashRadius, x, 2);
        var (nx, ny) = order.Leash(50, 0);      // already inside: untouched
        Assert.Equal(50, nx, 2);
        Assert.Equal(0, ny, 2);
    }

    // ---- StandOffTactic ----------------------------------------------------

    [Fact]
    public void TooFarOffSheCloses()
    {
        var (order, m) = Guard();
        var goal = AiPilot.Decide(Sees(0, 0, 0, At(200, 0), range: 200, bearingDeg: 0), m, order);
        Assert.Equal(AiState.Pursuing, m.State);
        Assert.True(goal.KeepWay);
        // She makes for a point short of the target, at fighting range.
        Assert.Equal(200 - AiConfig.AiFightRange, goal.X, 1);
    }

    [Fact]
    public void TooCloseSheOpensTheRange()
    {
        var (order, m) = Guard();
        var goal = AiPilot.Decide(Sees(0, 0, 0, At(20, 0), range: 20, bearingDeg: 0), m, order);
        Assert.Equal(AiState.Pursuing, m.State);
        Assert.True(goal.KeepWay);
        Assert.True(goal.X < 0, "the goal must be away from the target, not through it");
        Assert.False(goal.OpenFire);
    }

    [Fact]
    public void InRangeSheStopsAndFights()
    {
        var (order, m) = Guard();
        var goal = AiPilot.Decide(Sees(0, 0, 0, At(110, 0), range: 110, bearingDeg: 90), m, order);
        Assert.Equal(AiState.Engaging, m.State);
        Assert.False(goal.KeepWay);        // stopped: she pivots rather than orbits
        Assert.True(goal.OpenFire);
    }

    // The blind arc is 120 degrees wide astern, so turning to the FAR beam
    // would sweep the target through the blind spot instead of out of it.
    [Theory]
    [InlineData(150)]    // well abaft the starboard beam
    [InlineData(-150)]   // and to port
    [InlineData(5)]
    [InlineData(-5)]
    public void SheAlwaysTurnsToTheBeamTheTargetIsAlreadyOn(float bearingDeg)
    {
        var (order, m) = Guard();
        // Pointing east, target somewhere around her.
        float b = bearingDeg * Deg;
        var target = At(110 * MathF.Cos(b), 110 * MathF.Sin(b));
        var goal = AiPilot.Decide(Sees(0, 0, 0, target, range: 110, bearingDeg: bearingDeg), m, order);
        Assert.Equal(AiState.Engaging, m.State);

        // Steering for that goal points her along it; check the target then
        // sits within the broadside arc (60..120 degrees off the bow) on the
        // side it started.
        float wanted = MathF.Atan2(goal.Y - 0, goal.X - 0);
        float after = Helmsman.Bearing(0, 0, wanted, target.X, target.Y);
        Assert.InRange(MathF.Abs(after) / Deg, 60f, 120f);
        Assert.Equal(MathF.Sign(bearingDeg), MathF.Sign(after));
    }

    // A bearing drifting across the arc boundary must not flip the state
    // every frame. Once she is in the fight she commits to a volley.
    [Fact]
    public void SheDoesNotStrobeAtTheEdgeOfTheArc()
    {
        var (order, m) = Guard();
        // Settle into the fight square abeam.
        AiPilot.Decide(Sees(0, 0, 0, At(0, 110), range: 110, bearingDeg: 90), m, order);
        Assert.Equal(AiState.Engaging, m.State);
        Assert.True(m.FireDwellLeft > 0);

        // Now walk the bearing across the boundary a degree at a time; she
        // must stay in the fight the whole way.
        for (float b = 90; b > 40; b -= 1f)
        {
            AiPilot.Decide(Sees(0, 0, 0, At(110, 0), range: 110, bearingDeg: b), m, order);
            Assert.Equal(AiState.Engaging, m.State);
        }
    }

    [Fact]
    public void DriftingOutOfRangeSendsHerAfterThemAgain()
    {
        var (order, m) = Guard();
        AiPilot.Decide(Sees(0, 0, 0, At(0, 110), range: 110, bearingDeg: 90), m, order);
        Assert.Equal(AiState.Engaging, m.State);
        AiPilot.Decide(Sees(0, 0, 0, At(0, 200), range: 200, bearingDeg: 90), m, order);
        Assert.Equal(AiState.Pursuing, m.State);
    }

    // ---- the other orders --------------------------------------------------

    [Fact]
    public void AnIdleBoatDoesNothingAtAll()
    {
        var m = new AiMemory();
        var goal = AiPilot.Decide(Sees(10, 20, 0, null), m, new IdleOrder());
        Assert.Equal(AiState.Idle, m.State);
        Assert.False(goal.KeepWay);
        Assert.False(goal.OpenFire);
        Assert.Equal(10, goal.X);
    }

    [Fact]
    public void AVoyageFollowsItsRouteAndThenLiesTo()
    {
        var m = new AiMemory
        {
            Route = new NavRoute(new[] { (0f, 0f), (100f, 0f) }, reachesDestination: true),
        };
        var order = new VoyageOrder();
        var goal = AiPilot.Decide(Sees(0, 0, 0, null), m, order);
        Assert.Equal(AiState.Sailing, m.State);
        Assert.Equal(100, goal.X);        // waypoint 0 is where she already is
        Assert.True(goal.KeepWay);

        AiPilot.Decide(Sees(100, 0, 0, null), m, order);
        Assert.Equal(AiState.Idle, m.State);
    }

    [Fact]
    public void OrdersThatDoNotFightHaveNoTactic()
    {
        Assert.Null(new IdleOrder().Tactic);
        Assert.Null(new VoyageOrder().Tactic);
        Assert.NotNull(new ProtectOrder(0, 0, new StandOffTactic()).Tactic);
    }

    // ---- steering ----------------------------------------------------------

    [Fact]
    public void StoppingMeansBackingTheOarsButStillSteering()
    {
        var s = Sees(0, 0, 0, null, speed: 30);
        var helm = AiSteering.For(new AiGoal(0, 100, KeepWay: false, OpenFire: false), s);
        Assert.False(helm.Ahead);
        Assert.True(helm.Astern);                       // way still on: take it off
        Assert.True(helm.Port || helm.Starboard);       // ...but keep turning
    }

    [Fact]
    public void OnceStoppedSheJustPivots()
    {
        var s = Sees(0, 0, 0, null, speed: 1);
        var helm = AiSteering.For(new AiGoal(0, 100, KeepWay: false, OpenFire: false), s);
        Assert.False(helm.Ahead);
        Assert.False(helm.Astern);
        Assert.True(helm.Starboard);      // the goal is off to starboard (world y is down)
    }

    [Fact]
    public void KeepingWayIsJustTheHelmsman()
    {
        var s = Sees(0, 0, 0, null, speed: 20);
        var mine = AiSteering.For(new AiGoal(100, 40, KeepWay: true, OpenFire: false), s);
        var theirs = Helmsman.SteerTo(0, 0, 0, 100, 40);
        Assert.Equal(theirs.Ahead, mine.Ahead);
        Assert.Equal(theirs.Port, mine.Port);
        Assert.Equal(theirs.Starboard, mine.Starboard);
    }

    [Fact]
    public void ArrivingMeansNoOrdersAtAll()
    {
        var helm = AiSteering.For(new AiGoal(1, 1, KeepWay: true, OpenFire: false), Sees(0, 0, 0, null));
        Assert.False(helm.Ahead || helm.Astern || helm.Port || helm.Starboard);
    }

    // ---- the snapshot's geometry -------------------------------------------

    [Theory]
    [InlineData(90, 0, 1)]
    [InlineData(-90, 0, -1)]
    [InlineData(0, 90, 1)]
    [InlineData(179, 89, 1)]
    [InlineData(-150, 60, -1)]
    public void BeamErrorAndSideAreTheGeometryTheTacticNeeds(float bearingDeg, float errDeg, int side)
    {
        var s = Sees(0, 0, 0, At(1, 1), range: 10, bearingDeg: bearingDeg);
        Assert.Equal(errDeg, s.BeamError / Deg, 3);
        Assert.Equal(side, s.BeamSide);
    }
}
