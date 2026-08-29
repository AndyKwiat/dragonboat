using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

// A labelled circle painted in ../terrain-gen: centre and radius in map
// cells (= world units), plus a free-text tag. Markers are not rasterized
// into the tile grid; they ride alongside it, in placement order (later
// entries paint on top and win hit-tests first).
public readonly struct TerrainMarker
{
    public float X { get; }
    public float Y { get; }
    public float R { get; }
    public string Tag { get; }

    public TerrainMarker(float x, float y, float r, string tag)
    {
        X = x; Y = y; R = r; Tag = tag;
    }

    public bool Contains(float x, float y)
    {
        float dx = x - X, dy = y - Y;
        return dx * dx + dy * dy <= R * R;
    }
}

// The world's tile grid, parsed from a ../terrain-gen `.terrain` file (spec:
// ../terrain-gen/docs/FILE_FORMAT.md). The 4-byte magic is the version:
// 'TGN1' is header + heights + tiles, 'TGN2' appends a marker section and is
// otherwise byte-identical. Header: u32 LE size (cells per side, a power of
// two), u32 flags, f32 worldSize; then f32[size²] heights and u8[size²] tile
// ids, row-major top to bottom; the whole thing usually gzipped. The
// heightmap is skipped for now (land gets flat per-tile column heights,
// Config.TerrainHeights). One cell is one world unit.
//
// Tile ids 0-4 come from the file; City is stamped at runtime by CityPlacer.
public sealed class TerrainMap
{
    public const int Water = 0, Sand = 1, Grass = 2, Forest = 3, Rock = 4, City = 5;
    public const int TileCount = 6;
    private const int HeaderBytes = 16;
    private const int MarkerHeaderBytes = 14;  // f32 x, f32 y, f32 r, u16 tagLen
    private const int MaxMarkers = 1_000_000;
    private const int MaxTagLength = 100;      // characters, not bytes

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    // In file order; empty for a version-1 file.
    public IReadOnlyList<TerrainMarker> Markers { get; }

    public float WorldW => Width;
    public float WorldH => Height;

    public TerrainMap(int width, int height, byte[] data, IReadOnlyList<TerrainMarker>? markers = null)
    {
        if (data.Length != width * height) throw new ArgumentException("data length does not match size");
        Width = width; Height = height; Data = data;
        Markers = markers ?? Array.Empty<TerrainMarker>();
    }

    public int TerrainAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Water;
        return Data[y * Width + x];
    }

    public static bool IsWaterIndex(int t) => t == Water;
    public bool IsWater(int x, int y) => IsWaterIndex(TerrainAt(x, y));

    // The topmost marker covering a point, or null if none does.
    public TerrainMarker? MarkerAt(float x, float y)
    {
        for (int i = Markers.Count - 1; i >= 0; i--)
            if (Markers[i].Contains(x, y)) return Markers[i];
        return null;
    }

    public static TerrainMap Parse(byte[] bytes)
    {
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) bytes = Gunzip(bytes);
        if (bytes.Length < HeaderBytes || bytes[0] != 'T' || bytes[1] != 'G' || bytes[2] != 'N')
            throw new Exception("not a terrain file");
        int version = bytes[3] == '1' ? 1 : bytes[3] == '2' ? 2 : 0;
        if (version == 0) throw new Exception($"terrain: unsupported version 'TGN{(char)bytes[3]}'");
        uint size = BitConverter.ToUInt32(bytes, 4);
        if (size < 1 || size > 65536 || (size & (size - 1)) != 0) throw new Exception($"terrain: invalid size {size}");
        long n = (long)size * size;
        long pixelsEnd = HeaderBytes + n * 4 + n;
        if (version == 1 && bytes.Length != pixelsEnd)
            throw new Exception($"terrain: expected {pixelsEnd} bytes for size {size}, got {bytes.Length}");
        if (version == 2 && bytes.Length < pixelsEnd + 4)
            throw new Exception($"terrain: expected at least {pixelsEnd + 4} bytes for size {size}, got {bytes.Length}");
        var tiles = new byte[n];
        Array.Copy(bytes, HeaderBytes + n * 4, tiles, 0, n);
        foreach (var t in tiles)
            if (t > Rock) throw new Exception($"terrain: unknown tile id {t}");
        var markers = version >= 2 ? ParseMarkers(bytes, pixelsEnd) : Array.Empty<TerrainMarker>();
        return new TerrainMap((int)size, (int)size, tiles, markers);
    }

    private static TerrainMarker[] ParseMarkers(byte[] bytes, long p)
    {
        uint count = BitConverter.ToUInt32(bytes, (int)p); p += 4;
        if (count > MaxMarkers) throw new Exception($"terrain: too many markers ({count})");
        var markers = new TerrainMarker[count];
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (int i = 0; i < count; i++)
        {
            if (p + MarkerHeaderBytes > bytes.Length) throw new Exception($"terrain: truncated marker {i}");
            float x = BitConverter.ToSingle(bytes, (int)p);
            float y = BitConverter.ToSingle(bytes, (int)p + 4);
            float r = BitConverter.ToSingle(bytes, (int)p + 8);
            int tagLen = BitConverter.ToUInt16(bytes, (int)p + 12);
            p += MarkerHeaderBytes;
            if (!(r > 0)) throw new Exception($"terrain: marker {i} has radius {r}");
            if (p + tagLen > bytes.Length) throw new Exception($"terrain: truncated marker {i} tag");
            string tag;
            try { tag = utf8.GetString(bytes, (int)p, tagLen); }
            catch (Exception e) { throw new Exception($"terrain: marker {i} tag is not valid UTF-8", e); }
            p += tagLen;
            if (tag.Length == 0 || tag.Length > MaxTagLength)
                throw new Exception($"terrain: marker {i} has an invalid tag length ({tag.Length} characters)");
            markers[i] = new TerrainMarker(x, y, r, tag);
        }
        if (p != bytes.Length) throw new Exception($"terrain: {bytes.Length - p} trailing bytes after markers");
        return markers;
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
