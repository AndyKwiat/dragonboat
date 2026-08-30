using System;
using System.Linq;
using Xunit;

namespace DragonboatGame.Tests;

public class NavPathTests
{
    private static NavMesh Mesh(TerrainMap map, int cellSize = 1, int clearance = 0) =>
        NavMeshBuilder.Build(map, cellSize, clearance, offMapIsLand: false);

    // Every leg of a route has to stay on the water, or the route is a lie.
    private static void AssertOnTheWater(NavMesh nav, NavRoute route)
    {
        var w = route.Waypoints;
        for (int i = 1; i < w.Count; i++)
            Assert.True(NavPath.Visible(nav, w[i - 1].X, w[i - 1].Y, w[i].X, w[i].Y),
                $"leg {i} ({w[i - 1]} -> {w[i]}) crosses land");
    }

    [Fact]
    public void OpenWaterGivesAStraightLine()
    {
        var nav = Mesh(TerrainFixture.OpenSea(32));
        var route = NavPath.Find(nav, 1.5f, 1.5f, 30.5f, 30.5f);
        Assert.NotNull(route);
        Assert.True(route!.ReachesDestination);
        // Start and finish, and nothing in between worth stopping at.
        Assert.Equal(2, route.Waypoints.Count);
        Assert.Equal(30.5f, route.Waypoints[^1].X, 3);
        Assert.Equal(30.5f, route.Waypoints[^1].Y, 3);
    }

    [Fact]
    public void ARouteRoundsAnIsland()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "................",
            "................",
            "................",
            "................",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................"));
        var route = NavPath.Find(nav, 7.5f, 1.5f, 7.5f, 14.5f);
        Assert.NotNull(route);
        AssertOnTheWater(nav, route!);
        // It has to go round, so it is longer than the straight line...
        Assert.True(route!.Length() > 13f, $"length {route.Length()}");
        // ...but not absurdly: round one side, not a tour of the map.
        Assert.True(route.Length() < 32f, $"length {route.Length()}");
        Assert.True(route.Waypoints.Count >= 3, "rounding an island needs a corner");
    }

    [Fact]
    public void ThereIsNoRouteBetweenSeparateSeas()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##.."));
        Assert.Null(NavPath.Find(nav, 0.5f, 0.5f, 5.5f, 5.5f));
    }

    [Fact]
    public void ThereIsNoRouteFromDryLandFarFromTheWater()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "....",
            ".##.",
            ".##.",
            "...."));
        // The island's middle is close to water, so a short snap finds it...
        Assert.NotNull(NavPath.Find(nav, 1.5f, 1.5f, 3.5f, 3.5f, snapRadius: 4f));
        // ...but not if we refuse to snap at all.
        Assert.Null(NavPath.Find(nav, 1.5f, 1.5f, 3.5f, 3.5f, snapRadius: 0f));
    }

    // Water walled in on every side is its own sea, however close it looks.
    [Fact]
    public void AnEnclosedPoolIsItsOwnSea()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "........",
            "........",
            "..####..",
            "..#..#..",
            "..#..#..",
            "..####..",
            "........",
            "........"));
        Assert.Equal(2, nav.BodyCount);
        Assert.Null(NavPath.Find(nav, 0.5f, 0.5f, 3.5f, 3.5f, snapRadius: 0f));
    }

    // A destination that is not on the water — a quay, a beach — gets a route
    // to the water beside it, flagged as falling short rather than pretending.
    [Fact]
    public void ARouteToDryLandStopsShortAndSaysSo()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "................",
            "................",
            "................",
            "................",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            ".....######.....",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................"));
        // The middle of the island: no water there, but plenty alongside.
        var route = NavPath.Find(nav, 1.5f, 1.5f, 7.5f, 7.5f, snapRadius: 8f);
        Assert.NotNull(route);
        Assert.False(route!.ReachesDestination);
        AssertOnTheWater(nav, route);
        // ...whereas a destination actually on the water is reached.
        var wet = NavPath.Find(nav, 1.5f, 1.5f, 14.5f, 14.5f);
        Assert.True(wet!.ReachesDestination);
    }

    // The string-pull must not be allowed to cut a diagonal gap: two cells
    // meeting at a corner are not a passage a boat can use.
    [Fact]
    public void LineOfSightRefusesToSqueezeThroughADiagonalGap()
    {
        var nav = Mesh(TerrainFixture.FromAscii(
            "..##",
            "..##",
            "##..",
            "##.."));
        Assert.False(NavPath.Visible(nav, 0.5f, 0.5f, 3.5f, 3.5f));
        Assert.True(NavPath.Visible(nav, 0.5f, 0.5f, 1.5f, 1.5f));
    }

    [Fact]
    public void AStraightChannelIsNotZigZagged()
    {
        // Portal midpoints alone would put a waypoint on every cell boundary;
        // the string-pull should leave essentially a straight run.
        var nav = Mesh(TerrainFixture.Channel(64, width: 8, top: 20), cellSize: 4, clearance: 0);
        var route = NavPath.Find(nav, 2f, 24f, 62f, 24f);
        Assert.NotNull(route);
        AssertOnTheWater(nav, route!);
        Assert.True(route!.Waypoints.Count <= 4, $"{route.Waypoints.Count} waypoints for a straight channel");
        Assert.True(route.Length() < 70f, $"length {route.Length()} for a 60-unit run");
    }

    [Fact]
    public void ARouteToWhereYouAlreadyAreIsTrivial()
    {
        var nav = Mesh(TerrainFixture.OpenSea(16));
        var route = NavPath.Find(nav, 8f, 8f, 8f, 8f);
        Assert.NotNull(route);
        Assert.True(route!.Length() < 0.001f);
    }

    // The one that matters: the real bench map, between the markers the
    // pathing scenario actually spawns boats on.
    [Fact]
    public void TheBenchMapCanBeSailedFromP1ToP2()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("pathingTest.terrain")));
        var nav = NavMeshBuilder.Build(map, NavConfig.CellSize, NavConfig.ClearanceCells);
        var scenario = Scenario.Parse("pathing", System.IO.File.ReadAllText(GameConfigTests.RepoFile("scenarios/pathing.yml")));
        var p1 = scenario.Marker(map, "p1");
        var p2 = scenario.Marker(map, "p2");

        var route = NavPath.Find(nav, p1.X, p1.Y, p2.X, p2.Y);
        Assert.NotNull(route);
        AssertOnTheWater(nav, route!);
        // The markers are ~570 units apart in a straight line, and the rivers
        // wind, so a real route is longer than that but not a wild tour.
        float straight = MathF.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
        Assert.InRange(route!.Length(), straight, straight * 4);
        Assert.True(route.Waypoints.Count > 2, "a winding river needs turns");
    }

    [Fact]
    public void EveryPairOfTheBenchMapsMarkersIsReachable()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("pathingTest.terrain")));
        var nav = NavMeshBuilder.Build(map, NavConfig.CellSize, NavConfig.ClearanceCells);
        foreach (var a in map.Markers)
            foreach (var b in map.Markers)
                Assert.NotNull(NavPath.Find(nav, a.X, a.Y, b.X, b.Y));
    }
}
