using System;
using Xunit;

namespace DragonboatGame.Tests;

public class GameConfigTests
{
    [Fact]
    public void ParsesTerrainFileAndCityCount()
    {
        var config = GameConfig.Parse("terrainFile: map1.terrain\ncityCount: 20\n");
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.Equal(20, config.CityCount);
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndQuotes()
    {
        var config = GameConfig.Parse("# a comment\n\nterrainFile: \"other.terrain\"   # trailing\ncityCount: 7\n");
        Assert.Equal("other.terrain", config.TerrainFile);
        Assert.Equal(7, config.CityCount);
    }

    [Fact]
    public void ShipsWithTheRepoAndHasBothKeys()
    {
        var config = GameConfig.Parse(System.IO.File.ReadAllText(RepoFile("game-config.yml")));
        Assert.Equal("map1.terrain", config.TerrainFile);
        Assert.True(config.CityCount > 0);
    }

    [Theory]
    [InlineData("cityCount: 20\n")]                       // no terrainFile
    [InlineData("terrainFile: map1.terrain\n")]           // no cityCount
    [InlineData("terrainFile: map1.terrain\ncityCount: lots\n")]
    public void RejectsIncompleteConfigs(string text) => Assert.ThrowsAny<Exception>(() => GameConfig.Parse(text));

    [Fact]
    public void RejectsNonsenseValues()
    {
        Assert.ThrowsAny<Exception>(() => new GameConfig("", 20));
        Assert.ThrowsAny<Exception>(() => new GameConfig("map1.terrain", 0));
    }

    [Theory]
    [InlineData(100, 40, 40)]     // the counts are quoted per 100 cities
    [InlineData(20, 40, 8)]
    [InlineData(20, 10, 2)]
    [InlineData(5, 10, 1)]        // never rounds down to nothing
    public void ScalesCountsByCityCount(int cities, int per100, int expected) =>
        Assert.Equal(expected, new GameConfig("map1.terrain", cities).ScaledByCities(per100));

    internal static string RepoFile(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name);
}
