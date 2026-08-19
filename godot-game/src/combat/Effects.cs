using Godot;
using System.Collections.Generic;

// Short-lived visual markers: flat rings that grow and fade (splashes, dirt
// puffs — the "where did it land" cue) and spheres that fade (muzzle flash,
// hit burst). Unshaded, like the prototype's MeshBasicMaterial.
public partial class Effects : Node3D
{
    private sealed class Effect
    {
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
        public float Life, MaxLife, Size;
        public bool Grow;
    }

    private readonly List<Effect> live = new();
    private static Mesh? ringMesh, sphereMesh;

    // A flat ring in the XZ plane (normal +Y), radius 1 outer. Shared with the reticle.
    public static Mesh RingMesh()
    {
        if (ringMesh != null) return ringMesh;
        const float inner = 0.6f, outer = 1f;
        const int segs = 32;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < segs; i++)
        {
            float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
            var i0 = new Vector3(Mathf.Cos(a0) * inner, 0, Mathf.Sin(a0) * inner);
            var o0 = new Vector3(Mathf.Cos(a0) * outer, 0, Mathf.Sin(a0) * outer);
            var i1 = new Vector3(Mathf.Cos(a1) * inner, 0, Mathf.Sin(a1) * inner);
            var o1 = new Vector3(Mathf.Cos(a1) * outer, 0, Mathf.Sin(a1) * outer);
            // Wound so the face points up (+Y): counter-clockwise seen from above.
            st.AddVertex(i0); st.AddVertex(i1); st.AddVertex(o0);
            st.AddVertex(o0); st.AddVertex(i1); st.AddVertex(o1);
        }
        ringMesh = st.Commit();
        return ringMesh;
    }

    public static Mesh SphereMesh()
    {
        sphereMesh ??= new SphereMesh { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 };
        return sphereMesh;
    }

    public static StandardMaterial3D UnshadedMaterial(Color color, bool transparent)
    {
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (transparent) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    // Grows to size × CombatConfig.RingGrow while fading. Sits just above the ground at pos.Y.
    public void SpawnRing(Vector3 pos, Color color, float life, float size)
    {
        pos.Y += 0.06f;
        Spawn(RingMesh(), pos, color, life, size, grow: true);
    }

    public void SpawnBurst(Vector3 pos, Color color, float life, float size) =>
        Spawn(SphereMesh(), pos, color, life, size, grow: false);

    private void Spawn(Mesh mesh, Vector3 pos, Color color, float life, float size, bool grow)
    {
        var mat = UnshadedMaterial(new Color(color, 0.9f), transparent: true);
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Scale = Vector3.One * size };
        AddChild(mi);
        live.Add(new Effect { Mesh = mi, Material = mat, Life = life, MaxLife = life, Size = size, Grow = grow });
    }

    public void Update(float dt)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var f = live[i];
            f.Life -= dt;
            float t = 1 - f.Life / f.MaxLife;
            var c = f.Material.AlbedoColor; c.A = 1 - t; f.Material.AlbedoColor = c;
            if (f.Grow) f.Mesh.Scale = Vector3.One * f.Size * (1 + t * (CombatConfig.RingGrow - 1));
            if (f.Life <= 0)
            {
                f.Mesh.QueueFree();
                live.RemoveAt(i);
            }
        }
    }
}
