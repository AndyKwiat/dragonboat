using Godot;
using System;

// Parser for the world-generator .dmap format: 'DMAP' magic, uint32 LE
// width, uint32 LE height, then width*height uint8 terrain indices,
// row-major, top-to-bottom.
public sealed class DMap
{
    public const int Ocean = 0, Beach = 1, Grassland = 2, Forest = 3, Mountain = 4, River = 5, Canal = 6, City = 7;

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public float WorldW => Width * Config.TileScale;
    public float WorldH => Height * Config.TileScale;

    private DMap(int width, int height, byte[] data)
    {
        Width = width; Height = height; Data = data;
    }

    public int TerrainAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Ocean;
        return Data[y * Width + x];
    }

    public static DMap Load(string path)
    {
        var bytes = FileAccess.GetFileAsBytes(path);
        if (bytes.Length < 12 || bytes[0] != (byte)'D' || bytes[1] != (byte)'M' || bytes[2] != (byte)'A' || bytes[3] != (byte)'P')
            throw new Exception($"{path}: not a DMAP file");
        int width = BitConverter.ToInt32(bytes, 4);
        int height = BitConverter.ToInt32(bytes, 8);
        if (bytes.Length != 12 + width * height)
            throw new Exception($"DMAP size mismatch: expected {12 + width * height} bytes, got {bytes.Length}");
        var data = new byte[width * height];
        Array.Copy(bytes, 12, data, 0, data.Length);
        return new DMap(width, height, data);
    }
}
