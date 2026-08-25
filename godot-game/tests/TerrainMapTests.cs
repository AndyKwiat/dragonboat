using System;
using Xunit;

namespace DragonboatGame.Tests;

public class TerrainMapTests
{
    private static byte[] Tiles(int size, params byte[] head)
    {
        var tiles = new byte[size * size];
        head.CopyTo(tiles, 0);
        return tiles;
    }

    [Fact]
    public void ParsesRawFileSkippingTheHeightmap()
    {
        var map = TerrainMap.Parse(TerrainFixture.Encode(4, Tiles(4, TerrainMap.Water, TerrainMap.Grass, TerrainMap.Rock)));
        Assert.Equal(4, map.Width);
        Assert.Equal(4, map.Height);
        Assert.Equal(TerrainMap.Grass, map.TerrainAt(1, 0));
        Assert.Equal(TerrainMap.Rock, map.TerrainAt(2, 0));
    }

    [Fact]
    public void ParsesGzippedFile()
    {
        var raw = TerrainFixture.Encode(8, Tiles(8, TerrainMap.Forest));
        var map = TerrainMap.Parse(TerrainFixture.Gzip(raw));
        Assert.Equal(8, map.Width);
        Assert.Equal(TerrainMap.Forest, map.TerrainAt(0, 0));
    }

    [Fact]
    public void OneCellIsOneWorldUnit()
    {
        var map = TerrainMap.Parse(TerrainFixture.Encode(16, new byte[16 * 16]));
        Assert.Equal(16f, map.WorldW);
        Assert.Equal(16f, map.WorldH);
    }

    [Fact]
    public void OutsideTheMapIsWater()
    {
        var map = TerrainFixture.Island(8, 4);
        Assert.True(map.IsWater(-1, 3));
        Assert.True(map.IsWater(3, 8));
        Assert.False(map.IsWater(4, 4));
    }

    [Fact]
    public void OnlyWaterCountsAsWater()
    {
        Assert.True(TerrainMap.IsWaterIndex(TerrainMap.Water));
        foreach (int t in new[] { TerrainMap.Sand, TerrainMap.Grass, TerrainMap.Forest, TerrainMap.Rock, TerrainMap.City })
            Assert.False(TerrainMap.IsWaterIndex(t));
    }

    [Fact]
    public void RejectsWrongMagic() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(TerrainFixture.Encode(4, new byte[16], magic: "DMAP")));

    [Fact]
    public void RejectsTruncatedFile()
    {
        var raw = TerrainFixture.Encode(4, new byte[16]);
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(raw[..^3]));
    }

    [Fact]
    public void RejectsNonPowerOfTwoSize()
    {
        var raw = TerrainFixture.Encode(4, new byte[16]);
        BitConverter.GetBytes(3u).CopyTo(raw, 4);
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(raw));
    }

    [Fact]
    public void RejectsUnknownTileId() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(TerrainFixture.Encode(4, Tiles(4, 9))));

    [Fact]
    public void LoadsTheRepoTerrainFile()
    {
        var path = GameConfigTests.RepoFile("map1.terrain");
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(path));
        Assert.Equal(8192, map.Width);
        Assert.Equal(map.Width, map.Height);
        Assert.Contains(map.Data, t => t == TerrainMap.Water);
        Assert.Contains(map.Data, t => t == TerrainMap.Grass);
    }
}
