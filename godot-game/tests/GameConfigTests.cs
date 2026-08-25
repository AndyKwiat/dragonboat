using System;
using Xunit;

namespace DragonboatGame.Tests;

public class GameConfigTests
{
    [Fact]
    public void ParsesEveryKey()
    {
        var config = GameConfig.Parse("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\n");
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.Equal(20, config.CityCount);
        Assert.Equal((12, 6, 2), (config.SmallCities, config.MediumCities, config.LargeCities));
        Assert.Equal(100, config.CurrentDensity);
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndQuotes()
    {
        var config = GameConfig.Parse("# a comment\n\nterrainFile: \"other.terrain\"   # trailing\nsmallCities: 4\nmediumCities: 2\nlargeCities: 1\ncurrentDensity: 12.5\n");
        Assert.Equal("other.terrain", config.TerrainFile);
        Assert.Equal(7, config.CityCount);
        Assert.Equal(12.5, config.CurrentDensity);
    }

    [Fact]
    public void ShipsWithTheRepoAndHasEveryKey()
    {
        var config = GameConfig.Parse(System.IO.File.ReadAllText(RepoFile("game-config.yml")));
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.True(config.CityCount > 0);
        Assert.Equal(config.SmallCities + config.MediumCities + config.LargeCities, config.CityCount);
        Assert.True(config.LargeCities > 0, "the boat starts off a large city, and monks are bound for them");
        Assert.True(config.CurrentDensity >= 0);
    }

    // The size mix used to be a code constant behind one cityCount; anyone
    // with the old key gets told what to write instead.
    [Fact]
    public void TheOldCityCountKeySaysWhatToDoNow()
    {
        var e = Assert.ThrowsAny<Exception>(() =>
            GameConfig.Parse("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: 100\n"));
        Assert.Contains("smallCities", e.Message);
    }

    [Theory]
    [InlineData("smallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\n")]                  // no terrainFile
    [InlineData("terrainFile: map1.terrain\ncurrentDensity: 100\n")]      // no city counts
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nlargeCities: 2\ncurrentDensity: 100\n")]   // no mediumCities
    [InlineData("terrainFile: map1.terrain\nsmallCities: 0\nmediumCities: 0\nlargeCities: 0\ncurrentDensity: 100\n")]  // no cities at all
    [InlineData("terrainFile: map1.terrain\nsmallCities: -1\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\n")]
    [InlineData("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: 100\n")]   // the old key, now gone
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\n")]            // no currentDensity
    [InlineData("terrainFile: map1.terrain\nsmallCities: lots\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\n")]
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: thick\n")]
    public void RejectsIncompleteConfigs(string text) => Assert.ThrowsAny<Exception>(() => GameConfig.Parse(text));

    [Fact]
    public void RejectsNonsenseValues()
    {
        Assert.ThrowsAny<Exception>(() => new GameConfig("", 12, 6, 2, 100));
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 0, 0, 0, 100));    // no cities
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", -3, 6, 2, 100));   // negative
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 12, 6, 2, -1));    // density
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
        Assert.Equal(expected, new GameConfig("map1.terrain", 12, 6, 2, density).CurrentCount((long)size * size));

    [Theory]
    [InlineData(100, 40, 40)]     // the counts are quoted per 100 cities
    [InlineData(20, 40, 8)]
    [InlineData(20, 10, 2)]
    [InlineData(5, 10, 1)]        // never rounds down to nothing
    public void ScalesCountsByCityCount(int cities, int per100, int expected) =>
        Assert.Equal(expected, new GameConfig("map1.terrain", cities, 0, 0, 100).ScaledByCities(per100));

    internal static string RepoFile(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name);
}
