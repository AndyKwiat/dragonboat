using System;
using Xunit;

namespace DragonboatGame.Tests;

public class GameConfigTests
{
    [Fact]
    public void ParsesEveryKey()
    {
        var config = GameConfig.Parse("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n");
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.Equal(20, config.CityCount);
        Assert.Equal((12, 6, 2), (config.SmallCities, config.MediumCities, config.LargeCities));
        Assert.Equal(100, config.CurrentDensity);
        Assert.Equal((20, 4), (config.PriceEvents, config.VipEvents));
        Assert.Equal(20, config.HostileBoats);
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndQuotes()
    {
        var config = GameConfig.Parse("# a comment\n\nterrainFile: \"other.terrain\"   # trailing\nsmallCities: 4\nmediumCities: 2\nlargeCities: 1\ncurrentDensity: 12.5\npriceEvents: 0\nvipEvents: 0\nhostileBoats: 0\n");
        Assert.Equal("other.terrain", config.TerrainFile);
        Assert.Equal(7, config.CityCount);
        Assert.Equal(12.5, config.CurrentDensity);
        Assert.Equal((0, 0), (config.PriceEvents, config.VipEvents));   // 0 is a valid count: none
        Assert.Equal(0, config.HostileBoats);                           // ...and an empty sea
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
        Assert.True(config.PriceEvents >= 0 && config.VipEvents >= 0);
        Assert.True(config.HostileBoats >= 0);
    }

    // The size mix used to be a code constant behind one cityCount; anyone
    // with the old key gets told what to write instead.
    [Fact]
    public void TheOldCityCountKeySaysWhatToDoNow()
    {
        var e = Assert.ThrowsAny<Exception>(() =>
            GameConfig.Parse("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n"));
        Assert.Contains("smallCities", e.Message);
    }

    [Theory]
    [InlineData("smallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]                  // no terrainFile
    [InlineData("terrainFile: map1.terrain\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]      // no city counts
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]   // no mediumCities
    [InlineData("terrainFile: map1.terrain\nsmallCities: 0\nmediumCities: 0\nlargeCities: 0\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]  // no cities at all
    [InlineData("terrainFile: map1.terrain\nsmallCities: -1\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]
    [InlineData("terrainFile: map1.terrain\ncityCount: 20\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]   // the old key, now gone
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\n")]            // no currentDensity
    [InlineData("terrainFile: map1.terrain\nsmallCities: lots\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\nhostileBoats: 20\n")]
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: thick\n")]
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\nvipEvents: 4\nhostileBoats: 20\n")]    // no priceEvents
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nhostileBoats: 20\n")]  // no vipEvents
    [InlineData("terrainFile: map1.terrain\nsmallCities: 12\nmediumCities: 6\nlargeCities: 2\ncurrentDensity: 100\npriceEvents: 20\nvipEvents: 4\n")]  // no hostileBoats
    public void RejectsIncompleteConfigs(string text) => Assert.ThrowsAny<Exception>(() => GameConfig.Parse(text));

    [Fact]
    public void RejectsNonsenseValues()
    {
        Assert.ThrowsAny<Exception>(() => new GameConfig("", 12, 6, 2, 100, 20, 4, 20));
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 0, 0, 0, 100, 20, 4, 20));    // no cities
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", -3, 6, 2, 100, 20, 4, 20));   // negative
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 12, 6, 2, -1, 20, 4, 20));    // density
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 12, 6, 2, 100, -1, 4, 20));   // events
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 12, 6, 2, 100, 20, -1, 20));  // events
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 12, 6, 2, 100, 20, 4, -1));   // hostile boats
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
        Assert.Equal(expected, new GameConfig("map1.terrain", 12, 6, 2, density, 20, 4, 20).CurrentCount((long)size * size));

    internal static string RepoFile(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name);
}
