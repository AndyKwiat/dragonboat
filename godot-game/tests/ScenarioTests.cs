using System;
using Xunit;

namespace DragonboatGame.Tests;

public class ScenarioTests
{
    private const string Minimal = "terrain: t.terrain\nplayer: p0\n";

    [Fact]
    public void EverythingButTheTerrainFileHasADefault()
    {
        var s = Scenario.Parse("bare", "terrain: t.terrain\n");
        Assert.Equal("bare", s.Name);
        Assert.Equal("t.terrain", s.TerrainFile);
        Assert.True(s.Cities);
        Assert.True(s.Currents);
        Assert.True(s.EnemyFleet);
        Assert.Null(s.PlayerMarker);
        Assert.Empty(s.Boats);
    }

    [Fact]
    public void ReadsTheWorldItLeavesOut()
    {
        var s = Scenario.Parse("bare", Minimal + "cities: false\ncurrents: false\nenemyFleet: false\n");
        Assert.False(s.Cities);
        Assert.False(s.Currents);
        Assert.False(s.EnemyFleet);
        Assert.Equal("p0", s.PlayerMarker);
    }

    [Fact]
    public void ReadsOneAiBoatPerMarker()
    {
        var s = Scenario.Parse("bare", Minimal + "boats: p1, p2\n");
        Assert.Collection(s.Boats,
            b => { Assert.Equal("p1", b.Marker); Assert.Equal(AiStates.Default, b.State); },
            b => Assert.Equal("p2", b.Marker));
    }

    [Fact]
    public void ABoatCanNameItsOwnState()
    {
        var s = Scenario.Parse("bare", Minimal + "boats: p1:idle, p2\nai: idle\n");
        Assert.Equal(AiState.Idle, s.Boats[0].State);
        Assert.Equal(AiState.Idle, s.Boats[1].State);
    }

    [Fact]
    public void RejectsAnUnknownAiState()
    {
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", Minimal + "ai: rampage\n"));
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", Minimal + "boats: p1:rampage\n"));
    }

    [Fact]
    public void RejectsAMissingTerrainFile() =>
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", "cities: false\n"));

    // A typo in a scenario would otherwise be a silent default, and the run
    // it produced would quietly not be the test that was meant.
    [Fact]
    public void RejectsAKeyItDoesNotKnow() =>
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", Minimal + "citys: false\n"));

    [Fact]
    public void RejectsANonBooleanFlag() =>
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", Minimal + "cities: sometimes\n"));

    // Without cities there is no shore to start the player off, so the
    // scenario has to say which marker to use.
    [Fact]
    public void ACityLessScenarioMustPlaceThePlayerOnAMarker() =>
        Assert.ThrowsAny<Exception>(() => Scenario.Parse("bare", "terrain: t.terrain\ncities: false\n"));

    [Fact]
    public void FindsAMarkerByTagAndTheLastOneWins()
    {
        var map = TerrainFixture.Encode(4, new byte[16], markers: new[]
        {
            new TerrainMarker(1f, 1f, 5f, "p0"),
            new TerrainMarker(2f, 2f, 5f, "p1"),
            new TerrainMarker(3f, 3f, 5f, "p0"),
        });
        var s = Scenario.Parse("bare", Minimal);
        var parsed = TerrainMap.Parse(map);
        Assert.Equal(3f, s.Marker(parsed, "p0").X);
        Assert.Equal(2f, s.Marker(parsed, "p1").X);
    }

    [Fact]
    public void SaysSoWhenTheMapHasNoSuchMarker()
    {
        var map = TerrainMap.Parse(TerrainFixture.Encode(4, new byte[16],
            markers: new[] { new TerrainMarker(1f, 1f, 5f, "p0") }));
        var s = Scenario.Parse("bare", Minimal);
        var e = Assert.ThrowsAny<Exception>(() => s.Marker(map, "p9"));
        Assert.Contains("p9", e.Message);
        Assert.Contains("p0", e.Message);   // and what it does have
    }

    // The scenario in the repo, against the map in the repo: every marker it
    // spawns something on has to exist, or ./pathingTest.sh dies at launch.
    [Fact]
    public void ThePathingScenarioMatchesThePathingMap()
    {
        var s = Scenario.Parse("pathing", System.IO.File.ReadAllText(GameConfigTests.RepoFile("scenarios/pathing.yml")));
        Assert.False(s.Cities);
        Assert.Equal("p0", s.PlayerMarker);
        Assert.Equal(2, s.Boats.Count);
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile(s.TerrainFile)));
        Assert.Equal("p0", s.Marker(map, s.PlayerMarker!).Tag);
        foreach (var b in s.Boats) Assert.Equal(b.Marker, s.Marker(map, b.Marker).Tag);
    }

    [Fact]
    public void AiStateNamesAreCaseInsensitiveAndClosed()
    {
        Assert.Equal(AiState.Idle, AiStates.Parse("IDLE"));
        Assert.Equal(AiState.Idle, AiStates.Parse(" idle "));
        Assert.Null(AiStates.Parse("7"));        // the underlying enum value, not a name
        Assert.Null(AiStates.Parse("wander"));
    }
}
