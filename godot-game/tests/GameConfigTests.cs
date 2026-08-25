using System;
using Xunit;

namespace DragonboatGame.Tests;

public class GameConfigTests
{
    [Fact]
    public void ParsesEveryKey()
    {
        var config = GameConfig.Parse("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: 100\n");
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.Equal(20, config.CityCount);
        Assert.Equal(100, config.CurrentDensity);
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndQuotes()
    {
        var config = GameConfig.Parse("# a comment\n\nterrainFile: \"other.terrain\"   # trailing\ncityCount: 7\ncurrentDensity: 12.5\n");
        Assert.Equal("other.terrain", config.TerrainFile);
        Assert.Equal(7, config.CityCount);
        Assert.Equal(12.5, config.CurrentDensity);
    }

    [Fact]
    public void ShipsWithTheRepoAndHasBothKeys()
    {
        var config = GameConfig.Parse(System.IO.File.ReadAllText(RepoFile("game-config.yml")));
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.True(config.CityCount > 0);
        Assert.True(config.CurrentDensity >= 0);
    }

    [Theory]
    [InlineData("cityCount: 20\ncurrentDensity: 100\n")]                  // no terrainFile
    [InlineData("terrainFile: map1.terrain\ncurrentDensity: 100\n")]      // no cityCount
    [InlineData("terrainFile: map1.terrain\ncityCount: 20\n")]            // no currentDensity
    [InlineData("terrainFile: map1.terrain\ncityCount: lots\ncurrentDensity: 100\n")]
    [InlineData("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: thick\n")]
    public void RejectsIncompleteConfigs(string text) => Assert.ThrowsAny<Exception>(() => GameConfig.Parse(text));

    [Fact]
    public void RejectsNonsenseValues()
    {
        Assert.ThrowsAny<Exception>(() => new GameConfig("", 20, 100));
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 0, 100));
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 20, -1));
    }

    // 100 is the baseline: 3600 arrows on the 8192² map, and the count is
    // linear in both the density and the map's area.
    [Theory]
    [InlineData(100, 8192, 3600)]
    [InlineData(200, 8192, 7200)]
    [InlineData(10, 8192, 360)]
    [InlineData(0, 8192, 0)]
    [InlineData(100, 4096, 900)]      // quarter the area, quarter the arrows
    [InlineData(400, 2048, 900)]
    public void ScalesCurrentCountByDensityAndMapArea(double density, int size, int expected) =>
        Assert.Equal(expected, new GameConfig("map1.terrain", 20, density).CurrentCount((long)size * size));

    [Theory]
    [InlineData(100, 40, 40)]     // the counts are quoted per 100 cities
    [InlineData(20, 40, 8)]
    [InlineData(20, 10, 2)]
    [InlineData(5, 10, 1)]        // never rounds down to nothing
    public void ScalesCountsByCityCount(int cities, int per100, int expected) =>
        Assert.Equal(expected, new GameConfig("map1.terrain", cities, 100).ScaledByCities(per100));

    internal static string RepoFile(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name);
}
