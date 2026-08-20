using Godot;

// A fake shadow: a flat dark translucent disc sitting on the ground directly
// below a thing. No light math — cheap, readable at any zoom, and it tells
// you where an airborne thing is over the ground, which real shadows at our
// unshaded-terrain setup wouldn't. Callers own placement: set Position (just
// above the ground), Scale and RotationY each frame; SetStrength fades it.
public partial class BlobShadow : MeshInstance3D
{
    private static Mesh? disc;
    private readonly StandardMaterial3D material;
    private float lastStrength = -1;

    // A filled unit circle in the XZ plane, facing up.
    public static Mesh DiscMesh()
    {
        if (disc != null) return disc;
        const int segs = 24;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < segs; i++)
        {
            float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
            st.AddVertex(Vector3.Zero);
            st.AddVertex(new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)));
            st.AddVertex(new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)));
        }
        disc = st.Commit();
        return disc;
    }

    public BlobShadow(float strength)
    {
        Mesh = DiscMesh();
        material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0, 0, 0, strength),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            // Don't write depth: the disc hugs the ground, and fighting the
            // water plane / impact rings for depth just causes flicker.
            NoDepthTest = false,
        };
        MaterialOverride = material;
        lastStrength = strength;
        SetStrength(strength);
    }

    public void SetStrength(float a)
    {
        if (a == lastStrength) return;
        lastStrength = a;
        var c = material.AlbedoColor; c.A = a; material.AlbedoColor = c;
    }

    // 0 at ground level → 1 at shrinkHeight and above; drives both the
    // shrink and the fade of an airborne caster's shadow.
    public static float HeightT(float height, float shrinkHeight) =>
        shrinkHeight <= 0 ? 0 : Mathf.Clamp(height / shrinkHeight, 0, 1);
}
