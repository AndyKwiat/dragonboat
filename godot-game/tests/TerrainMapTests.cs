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

    private static TerrainMarker Marker(float x, float y, float r, string tag) => new TerrainMarker(x, y, r, tag);

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
    public void ParsesAVersionOneFileAsHavingNoMarkers()
    {
        var map = TerrainMap.Parse(TerrainFixture.Encode(4, Tiles(4, TerrainMap.Sand), magic: "TGN1"));
        Assert.Equal(TerrainMap.Sand, map.TerrainAt(0, 0));
        Assert.Empty(map.Markers);
    }

    [Fact]
    public void ParsesMarkersInFileOrder()
    {
        var raw = TerrainFixture.Encode(4, new byte[16], markers: new[]
        {
            Marker(1.5f, 2.5f, 10f, "harbour"),
            Marker(3f, 3f, 0.25f, "reef \u00e5 \U0001F30A"),
        });
        var map = TerrainMap.Parse(TerrainFixture.Gzip(raw));
        Assert.Equal(2, map.Markers.Count);
        Assert.Equal(1.5f, map.Markers[0].X);
        Assert.Equal(2.5f, map.Markers[0].Y);
        Assert.Equal(10f, map.Markers[0].R);
        Assert.Equal("harbour", map.Markers[0].Tag);
        Assert.Equal(0.25f, map.Markers[1].R);
        Assert.Equal("reef \u00e5 \U0001F30A", map.Markers[1].Tag);
    }

    [Fact]
    public void MarkerAtPicksTheTopmostCircle()
    {
        var raw = TerrainFixture.Encode(4, new byte[16], markers: new[]
        {
            Marker(2f, 2f, 2f, "under"),
            Marker(2f, 2f, 1f, "over"),
        });
        var map = TerrainMap.Parse(raw);
        Assert.Equal("over", map.MarkerAt(2f, 2f)?.Tag);
        Assert.Equal("under", map.MarkerAt(3.5f, 2f)?.Tag);
        Assert.Null(map.MarkerAt(0f, 0f));
    }

    [Fact]
    public void RejectsAnUnknownVersion() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(TerrainFixture.Encode(4, new byte[16], magic: "TGN9")));

    [Fact]
    public void RejectsAMissingMarkerSection()
    {
        var raw = TerrainFixture.Encode(4, new byte[16]);
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(raw[..^4]));
    }

    [Fact]
    public void RejectsATruncatedMarker()
    {
        var raw = TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, "quay") });
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(raw[..^2]));
    }

    [Fact]
    public void RejectsTrailingBytesAfterTheMarkers()
    {
        var raw = TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, "quay") });
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse([.. raw, (byte)0]));
    }

    [Fact]
    public void RejectsANonPositiveRadius() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(
            TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 0f, "flat") })));

    [Fact]
    public void RejectsAnEmptyTag() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(
            TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, "") })));

    [Fact]
    public void RejectsAnOverlongTag() =>
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(
            TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, new string('x', 101)) })));

    [Fact]
    public void AcceptsAMultiByteTagLongerInBytesThanCharacters()
    {
        var tag = new string('\u00e5', 100);  // 100 characters, 200 UTF-8 bytes
        var map = TerrainMap.Parse(TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, tag) }));
        Assert.Equal(tag, Assert.Single(map.Markers).Tag);
    }

    [Fact]
    public void RejectsAnInvalidUtf8Tag()
    {
        var raw = TerrainFixture.Encode(4, new byte[16], markers: new[] { Marker(1f, 1f, 5f, "ok") });
        raw[^1] = 0xff;
        Assert.ThrowsAny<Exception>(() => TerrainMap.Parse(raw));
    }

    [Fact]
    public void LoadsTheRepoTerrainFile()
    {
        var path = GameConfigTests.RepoFile("map1.terrain");
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(path));
        Assert.Equal(8192, map.Width);
        Assert.Equal(map.Width, map.Height);
        Assert.Contains(map.Data, t => t == TerrainMap.Water);
        Assert.Contains(map.Data, t => t == TerrainMap.Grass);
        Assert.Empty(map.Markers);  // a TGN2 file that carries no markers
    }
}
