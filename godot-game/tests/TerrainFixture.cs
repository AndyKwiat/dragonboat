using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DragonboatGame.Tests;

// Builds .terrain files (the ../terrain-gen TGN1/TGN2 format) in memory for tests.
public static class TerrainFixture
{
    // A version-2 file: header, heights, tiles, then the marker section. Pass
    // `magic: "TGN1"` (and no markers) for a version-1 file.
    public static byte[] Encode(int size, byte[] tiles, float worldSize = 1024f, string magic = "TGN2",
                                IEnumerable<TerrainMarker>? markers = null)
    {
        int n = size * size;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        foreach (char c in magic) w.Write((byte)c);
        w.Write((uint)size);
        w.Write(0u);
        w.Write(worldSize);
        for (int i = 0; i < n; i++) w.Write(1.5f * i);
        w.Write(tiles, 0, n);
        if (magic != "TGN1")
        {
            var list = new List<TerrainMarker>(markers ?? Array.Empty<TerrainMarker>());
            w.Write((uint)list.Count);
            foreach (var m in list)
            {
                w.Write(m.X); w.Write(m.Y); w.Write(m.R);
                var tag = Encoding.UTF8.GetBytes(m.Tag);
                w.Write((ushort)tag.Length);
                w.Write(tag);
            }
        }
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionMode.Compress)) gz.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    // A map painted as ASCII art, one char per cell: '.' water, '#' grass,
    // 'R' rock, 'C' city. Rows are y, characters are x — so a fixture reads
    // on screen exactly as it sits on the map, which is worth a great deal
    // when the thing under test is a grid algorithm.
    public static TerrainMap FromAscii(params string[] rows)
    {
        int h = rows.Length, w = h == 0 ? 0 : rows[0].Length;
        var tiles = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            if (rows[y].Length != w) throw new ArgumentException($"row {y} is {rows[y].Length} cells, not {w}");
            for (int x = 0; x < w; x++)
                tiles[y * w + x] = rows[y][x] switch
                {
                    '.' => TerrainMap.Water,
                    '#' => TerrainMap.Grass,
                    'R' => TerrainMap.Rock,
                    'C' => TerrainMap.City,
                    var c => throw new ArgumentException($"unknown map character '{c}'"),
                };
        }
        return new TerrainMap(w, h, tiles);
    }

    // Land everywhere except a horizontal band of `width` water rows whose
    // top edge is row `top`: one straight channel of known width, which is
    // what the nav mesh's clearance and alignment rules are measured against.
    public static TerrainMap Channel(int size, int width, int top)
    {
        var tiles = new byte[size * size];
        Array.Fill(tiles, (byte)TerrainMap.Grass);
        for (int y = top; y < Math.Min(top + width, size); y++)
            for (int x = 0; x < size; x++)
                tiles[y * size + x] = TerrainMap.Water;
        return new TerrainMap(size, size, tiles);
    }

    // Nothing but water, edge to edge.
    public static TerrainMap OpenSea(int size) => new TerrainMap(size, size, new byte[size * size]);

    // A map with an island of grass in the middle of the water: a square of
    // `land` cells centred in a `size`-cell map, so every edge is coast.
    public static TerrainMap Island(int size, int land)
    {
        var tiles = new byte[size * size];
        int lo = (size - land) / 2;
        for (int y = lo; y < lo + land; y++)
            for (int x = lo; x < lo + land; x++)
                tiles[y * size + x] = TerrainMap.Grass;
        return new TerrainMap(size, size, tiles);
    }

    // Water on the left half, grass on the right: one straight coastline
    // running down the middle of the map.
    public static TerrainMap Coastline(int size)
    {
        var tiles = new byte[size * size];
        for (int y = 0; y < size; y++)
            for (int x = size / 2; x < size; x++)
                tiles[y * size + x] = TerrainMap.Grass;
        return new TerrainMap(size, size, tiles);
    }
}
