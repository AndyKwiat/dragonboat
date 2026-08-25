using Godot;

// The flat 2D terrain: one plane covering the whole world, textured with the
// map's colours (nearest filtered) and a shader-drawn tile grid that fades in
// when zoomed close. In 3D this is the ground and the water; raised terrain
// is streamed on top of it (TerrainChunks).
public partial class TerrainPlane : MeshInstance3D
{
    private ShaderMaterial material = null!;

    public static ImageTexture BuildTexture(TerrainMap map)
    {
        var bytes = new byte[map.Width * map.Height * 3];
        for (int i = 0; i < map.Data.Length; i++)
        {
            var c = Config.TerrainColors[map.Data[i]];
            bytes[i * 3] = (byte)c.R8;
            bytes[i * 3 + 1] = (byte)c.G8;
            bytes[i * 3 + 2] = (byte)c.B8;
        }
        var image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgb8, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    public ImageTexture Texture { get; private set; } = null!;

    public void Build(TerrainMap map)
    {
        float w = map.WorldW, h = map.WorldH;
        Mesh = new PlaneMesh { Size = new Vector2(w, h) };
        Position = new Vector3(w / 2, 0, h / 2);
        material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain_plane.gdshader") };
        Texture = BuildTexture(map);
        material.SetShaderParameter("map", Texture);
        material.SetShaderParameter("world_size", new Vector2(w, h));
        material.SetShaderParameter("tile_scale", 1f);
        material.SetShaderParameter("grid_color", Config.GridColor);
        material.SetShaderParameter("grid_opacity", 0f);
        MaterialOverride = material;
    }

    public void SetGridOpacity(float a) => material.SetShaderParameter("grid_opacity", a);

    // Grid opacity for a given zoom (screen px per world unit).
    public static float GridOpacityFor(float zoom)
    {
        float pxPerTile = zoom;   // one cell = one world unit
        float t = (pxPerTile - Config.GridFadeMinPxPerTile) / (Config.GridFadeMaxPxPerTile - Config.GridFadeMinPxPerTile);
        return Mathf.Clamp(t, 0, 1) * Config.GridMaxAlpha;
    }
}
