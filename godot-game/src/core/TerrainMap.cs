using System;
using System.IO;
using System.IO.Compression;

// The world's tile grid, parsed from a ../terrain-gen `.terrain` file (spec:
// ../terrain-gen/src/core/fileFormat.ts): 'TGN1' magic, u32 LE size (cells
// per side, a power of two), u32 flags, f32 worldSize, f32[size²] heights,
// then u8[size²] tile ids, row-major top to bottom; the whole thing usually
// gzipped. The heightmap is skipped for now (land gets flat per-tile
// column heights, Config.TerrainHeights). One cell is one world unit.
//
// Tile ids 0-4 come from the file; City is stamped at runtime by CityPlacer.
public sealed class TerrainMap
{
    public const int Water = 0, Sand = 1, Grass = 2, Forest = 3, Rock = 4, City = 5;
    public const int TileCount = 6;
    private const int HeaderBytes = 16;

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public float WorldW => Width;
    public float WorldH => Height;

    public TerrainMap(int width, int height, byte[] data)
    {
        if (data.Length != width * height) throw new ArgumentException("data length does not match size");
        Width = width; Height = height; Data = data;
    }

    public int TerrainAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Water;
        return Data[y * Width + x];
    }

    public static bool IsWaterIndex(int t) => t == Water;
    public bool IsWater(int x, int y) => IsWaterIndex(TerrainAt(x, y));

    public static TerrainMap Parse(byte[] bytes)
    {
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) bytes = Gunzip(bytes);
        if (bytes.Length < HeaderBytes || bytes[0] != 'T' || bytes[1] != 'G' || bytes[2] != 'N' || bytes[3] != '1')
            throw new Exception("not a TGN1 terrain file");
        uint size = BitConverter.ToUInt32(bytes, 4);
        if (size < 1 || size > 65536 || (size & (size - 1)) != 0) throw new Exception($"terrain: invalid size {size}");
        long n = (long)size * size;
        long expected = HeaderBytes + n * 4 + n;
        if (bytes.Length != expected) throw new Exception($"terrain: expected {expected} bytes for size {size}, got {bytes.Length}");
        var tiles = new byte[n];
        Array.Copy(bytes, HeaderBytes + n * 4, tiles, 0, n);
        foreach (var t in tiles)
            if (t > Rock) throw new Exception($"terrain: unknown tile id {t}");
        return new TerrainMap((int)size, (int)size, tiles);
    }

    private static byte[] Gunzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}
