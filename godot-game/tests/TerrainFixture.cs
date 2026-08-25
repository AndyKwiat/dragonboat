using System;
using System.IO;
using System.IO.Compression;

namespace DragonboatGame.Tests;

// Builds .terrain files (the ../terrain-gen TGN1 format) in memory for tests.
public static class TerrainFixture
{
    public static byte[] Encode(int size, byte[] tiles, float worldSize = 1024f, string magic = "TGN1")
    {
        int n = size * size;
        var bytes = new byte[16 + n * 4 + n];
        for (int i = 0; i < 4; i++) bytes[i] = (byte)magic[i];
        BitConverter.GetBytes((uint)size).CopyTo(bytes, 4);
        BitConverter.GetBytes(0u).CopyTo(bytes, 8);
        BitConverter.GetBytes(worldSize).CopyTo(bytes, 12);
        for (int i = 0; i < n; i++) BitConverter.GetBytes(1.5f * i).CopyTo(bytes, 16 + i * 4);
        tiles.CopyTo(bytes, 16 + n * 4);
        return bytes;
    }

    public static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionMode.Compress)) gz.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

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
