using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Streamed 3D terrain: the map is cut into chunks of CellsPerChunk² columns,
// each chunk one ArrayMesh (top face + only the exposed side faces of every
// raised column; water columns at height 0 emit nothing — the flat terrain
// plane under everything is the water). Chunks are built on demand around
// the camera within a per-frame CPU budget and cached; when the visible area
// needs more chunks than MaxVisibleChunks, the LOD step doubles (each column
// covers step×step map cells, majority terrain) so the whole world is always
// covered by a bounded number of meshes.
public partial class TerrainChunks : Node3D
{
    private sealed class Chunk
    {
        public string Key = "";
        public MeshInstance3D? Mesh;   // null when the chunk has no raised columns
        public long LastUsed;
    }

    public struct Stats { public int Visible, Cached, Step, Pending; }

    private TerrainMap map = null!;
    private readonly Dictionary<string, Chunk> cache = new();
    private ShaderMaterial material = null!;
    private long frame;
    private Stats stats;
    private readonly int[] counts = new int[Config.TerrainColors.Length];
    // Colours: top = the config colour; sides darkened in linear space (like
    // the three.js version) and stored back as sRGB for the vertex colours.
    private Color[] topColors = null!, sideXColors = null!, sideYColors = null!;

    public void Init(TerrainMap map)
    {
        this.map = map;
        material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain_chunk.gdshader") };
        topColors = Config.TerrainColors;
        sideXColors = Config.TerrainColors.Select(c => Shade(c, Config.SideShadeX)).ToArray();
        sideYColors = Config.TerrainColors.Select(c => Shade(c, Config.SideShadeY)).ToArray();
    }

    private static Color Shade(Color c, float k)
    {
        var lin = c.SrgbToLinear();
        return new Color(lin.R * k, lin.G * k, lin.B * k).LinearToSrgb();
    }

    public Stats GetStats() => stats;

    // Majority terrain over a step×step block of map cells (top-left cell cx, cy).
    private int BlockType(int cx, int cy, int step)
    {
        if (cx < 0 || cy < 0 || cx >= map.Width || cy >= map.Height) return -1;
        if (step == 1) return map.Data[cy * map.Width + cx];
        Array.Clear(counts);
        int x1 = Math.Min(cx + step, map.Width);
        int y1 = Math.Min(cy + step, map.Height);
        for (int y = cy; y < y1; y++)
        {
            int row = y * map.Width;
            for (int x = cx; x < x1; x++) counts[map.Data[row + x]]++;
        }
        int best = 0;
        for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
        return best;
    }

    private MeshInstance3D? BuildChunk(int ci, int cj, int step)
    {
        int C = Config.CellsPerChunk;
        float cellW = step;   // one cell = one world unit
        int x0 = ci * C, y0 = cj * C;   // in LOD-cell units
        int N = C + 2;
        var types = new int[N * N];     // with a 1-cell border for side exposure at the edges
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                types[j * N + i] = BlockType((x0 + i - 1) * step, (y0 + j - 1) * step, step);
        float HOf(int i, int j) { int t = types[j * N + i]; return t < 0 ? 0 : Config.TerrainHeights[t]; }

        var positions = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int v = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            indices.Add(v); indices.Add(v + 1); indices.Add(v + 2);
            indices.Add(v); indices.Add(v + 2); indices.Add(v + 3);
        }
        // World (x, y, height) -> Godot (x, height, y).
        static Vector3 P(float x, float y, float h) => new(x, h, y);

        for (int j = 0; j < C; j++)
        {
            for (int i = 0; i < C; i++)
            {
                int t = types[(j + 1) * N + (i + 1)];
                if (t < 0) continue;
                float h = Config.TerrainHeights[t];
                if (h <= 0) continue;
                float wx0 = (x0 + i) * cellW, wx1 = wx0 + cellW;
                float wy0 = (y0 + j) * cellW, wy1 = wy0 + cellW;
                Quad(P(wx0, wy0, h), P(wx1, wy0, h), P(wx1, wy1, h), P(wx0, wy1, h), topColors[t]);
                float hw = HOf(i, j + 1), he = HOf(i + 2, j + 1), hn = HOf(i + 1, j), hs = HOf(i + 1, j + 2);
                if (hw < h) Quad(P(wx0, wy0, hw), P(wx0, wy1, hw), P(wx0, wy1, h), P(wx0, wy0, h), sideXColors[t]);
                if (he < h) Quad(P(wx1, wy0, he), P(wx1, wy1, he), P(wx1, wy1, h), P(wx1, wy0, h), sideXColors[t]);
                if (hn < h) Quad(P(wx0, wy0, hn), P(wx1, wy0, hn), P(wx1, wy0, h), P(wx0, wy0, h), sideYColors[t]);
                if (hs < h) Quad(P(wx0, wy1, hs), P(wx1, wy1, hs), P(wx1, wy1, h), P(wx0, wy1, h), sideYColors[t]);
            }
        }
        if (positions.Count == 0) return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        AddChild(instance);
        return instance;
    }

    private void Evict()
    {
        int max = Config.MaxCachedChunks;
        if (cache.Count <= max) return;
        var sorted = cache.Values.OrderBy(c => c.LastUsed).ToList();
        int toDrop = cache.Count - max;
        for (int i = 0; i < toDrop; i++)
        {
            var chunk = sorted[i];
            if (chunk.LastUsed == frame) break;
            chunk.Mesh?.QueueFree();
            cache.Remove(chunk.Key);
        }
    }

    /// Called once per frame with the visible ground bounds in world units.
    public void Update(Rect2 bounds, float focusX, float focusY)
    {
        frame++;
        int C = Config.CellsPerChunk;
        float pad = Config.BoundsPadCells;
        float left = Math.Max(0, bounds.Position.X - pad);
        float top = Math.Max(0, bounds.Position.Y - pad);
        float right = Math.Min(map.WorldW, bounds.End.X + pad);
        float bottom = Math.Min(map.WorldH, bounds.End.Y + pad);

        // Coarsest-necessary LOD: the smallest step whose chunk count fits.
        int step = 1;
        float chunkW;
        int ci0, ci1, cj0, cj1;
        for (;;)
        {
            chunkW = C * step;
            ci0 = (int)Math.Floor(left / chunkW);
            ci1 = Math.Min((int)Math.Ceiling(right / chunkW), (int)Math.Ceiling(map.Width / (double)(C * step))) - 1;
            cj0 = (int)Math.Floor(top / chunkW);
            cj1 = Math.Min((int)Math.Ceiling(bottom / chunkW), (int)Math.Ceiling(map.Height / (double)(C * step))) - 1;
            int count = (ci1 - ci0 + 1) * (cj1 - cj0 + 1);
            if (count <= Config.MaxVisibleChunks || step >= Config.MaxLodStep) break;
            step *= 2;
        }

        // Wanted chunks, nearest to the focus first so they build first.
        var wanted = new List<(int ci, int cj, float d)>();
        for (int cj = cj0; cj <= cj1; cj++)
            for (int ci = ci0; ci <= ci1; ci++)
            {
                float dx = (ci + 0.5f) * chunkW - focusX;
                float dy = (cj + 0.5f) * chunkW - focusY;
                wanted.Add((ci, cj, dx * dx + dy * dy));
            }
        wanted.Sort((a, b) => a.d.CompareTo(b.d));

        // Hide everything, then show/build what's wanted within the budget.
        foreach (var c in cache.Values) if (c.Mesh != null) c.Mesh.Visible = false;
        ulong start = Time.GetTicksUsec();
        int pending = 0, visible = 0;
        foreach (var w in wanted)
        {
            string key = $"{step}:{w.ci}:{w.cj}";
            if (!cache.TryGetValue(key, out var chunk))
            {
                if ((Time.GetTicksUsec() - start) / 1000.0 > Config.BuildBudgetMs) { pending++; continue; }
                chunk = new Chunk { Key = key, Mesh = BuildChunk(w.ci, w.cj, step), LastUsed = frame };
                cache[key] = chunk;
            }
            chunk.LastUsed = frame;
            if (chunk.Mesh != null) chunk.Mesh.Visible = true;
            visible++;
        }
        Evict();
        stats = new Stats { Visible = visible, Cached = cache.Count, Step = step, Pending = pending };
    }
}
