using Godot;
using System;
using System.Collections.Generic;

// TEST FEATURE: currents / rapids. Random arrows scattered on the water,
// all pointing away from the world centre (rivers flowing out to sea). Each
// arrow owns a rectangular zone ahead of it that pushes anything in it
// along the flow with a force of Strength, fading out over the zone's
// edges so crossing a boundary is a swell, not a shove. Overlapping zones
// add up. Drawn as a see-through tinted strip on the water with a big
// arrow on top. Strength is live-tunable (T panel).
public partial class Currents : Node3D
{
    public const int Count = 18000;            // arrows scattered over the map
    public const float ArrowLength = 30f;    // the arrow glyph itself
    public const float ZoneLength = 120f;     // zone extends this far from the arrow tail
    public const float ZoneWidth = 60f;
    [Tweak("Currents", 0, 150, 1)] public static float Strength = 40f;   // force (units/s²) at the zone's core
    [Tweak("Currents", 0, 0.5f, 0.05f)] public static float EdgeFade = 0.10f; // fraction of width/length over which it fades
    private static readonly Color ZoneColor = new Color(0.3f, 0.9f, 1f, 0.28f);
    private static readonly Color ArrowColor = new Color(1f, 1f, 1f, 0.85f);

    public readonly struct Current
    {
        public readonly float X, Y;      // arrow tail (zone starts here)
        public readonly float Dx, Dy;    // unit flow direction
        public Current(float x, float y, float dx, float dy) { X = x; Y = y; Dx = dx; Dy = dy; }
    }

    private readonly List<Current> currents = new();
    private DMap map = null!;

    public void Init(DMap map, Terrain terrain)
    {
        this.map = map;
        var rng = new Random();   // fresh layout every run
        float cx = map.WorldW / 2, cy = map.WorldH / 2;
        int tries = 0;
        while (currents.Count < Count && tries++ < Count * 50)
        {
            float x = (float)rng.NextDouble() * map.WorldW;
            float y = (float)rng.NextDouble() * map.WorldH;
            if (!terrain.IsWater(x, y)) continue;
            float dx = x - cx, dy = y - cy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1) continue;
            dx /= len; dy /= len;
            // Keep the whole zone on water so the arrow reads as a river mouth, not a land feature.
            if (!terrain.IsWater(x + dx * ZoneLength, y + dy * ZoneLength)) continue;
            currents.Add(new Current(x, y, dx, dy));
        }
    }

    public override void _Ready()
    {
        AddChild(new MeshInstance3D { Mesh = BuildZones(), MaterialOverride = Mat(ZoneColor), Position = new Vector3(0, 0.05f, 0) });
        AddChild(new MeshInstance3D { Mesh = BuildArrows(), MaterialOverride = Mat(ArrowColor), Position = new Vector3(0, 0.09f, 0) });
    }

    // Force on a body at (x, y): the sum of every zone it is in, each scaled
    // by a smooth falloff towards the zone's edges.
    public Vector2 ForceAt(float x, float y)
    {
        var total = Vector2.Zero;
        foreach (var c in currents)
        {
            float rx = x - c.X, ry = y - c.Y;
            float along = rx * c.Dx + ry * c.Dy;
            if (along < 0 || along > ZoneLength) continue;
            float across = Math.Abs(-rx * c.Dy + ry * c.Dx);
            if (across > ZoneWidth / 2) continue;
            float w = Fade(along / ZoneLength) * Fade(1 - across / (ZoneWidth / 2));
            total += new Vector2(c.Dx, c.Dy) * (Strength * w);
        }
        return total;
    }

    // 1 in the core, smoothstepping to 0 over the last EdgeFade of t∈[0,1]
    // at both ends (along) or the outer end (across).
    private static float Fade(float t)
    {
        if (EdgeFade <= 0) return 1;
        float e = Math.Min(t, 1 - t) / EdgeFade;
        if (e >= 1) return 1;
        return e * e * (3 - 2 * e);
    }

    private static StandardMaterial3D Mat(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // World (x, y) → Godot (x, 0, y), same mapping as the boat shadow.
    private static Vector3 P(float x, float y) => new(x, 0, y);

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    private Mesh BuildZones()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        foreach (var c in currents)
        {
            float px = -c.Dy * ZoneWidth / 2, py = c.Dx * ZoneWidth / 2;  // half-width, perpendicular
            float ex = c.X + c.Dx * ZoneLength, ey = c.Y + c.Dy * ZoneLength;
            Quad(st, P(c.X + px, c.Y + py), P(ex + px, ey + py), P(ex - px, ey - py), P(c.X - px, c.Y - py));
        }
        return st.Commit();
    }

    private Mesh BuildArrows()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        float shaft = ArrowLength * 0.6f, headLen = ArrowLength - shaft;
        float shaftHalf = ArrowLength * 0.08f, headHalf = ArrowLength * 0.25f;
        foreach (var c in currents)
        {
            // Arrow sits in the middle of its zone, pointing with the flow.
            float mx = c.X + c.Dx * (ZoneLength - ArrowLength) / 2, my = c.Y + c.Dy * (ZoneLength - ArrowLength) / 2;
            float nx = -c.Dy, ny = c.Dx;
            float sx = mx + c.Dx * shaft, sy = my + c.Dy * shaft;     // shaft end / head base
            float tx = mx + c.Dx * ArrowLength, ty = my + c.Dy * ArrowLength;  // tip
            Quad(st, P(mx + nx * shaftHalf, my + ny * shaftHalf), P(sx + nx * shaftHalf, sy + ny * shaftHalf),
                     P(sx - nx * shaftHalf, sy - ny * shaftHalf), P(mx - nx * shaftHalf, my - ny * shaftHalf));
            st.AddVertex(P(sx + nx * headHalf, sy + ny * headHalf));
            st.AddVertex(P(tx, ty));
            st.AddVertex(P(sx - nx * headHalf, sy - ny * headHalf));
        }
        return st.Commit();
    }
}
