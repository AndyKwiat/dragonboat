using Godot;

// The combat prototype's boat, rebuilt from primitives: box hull, a wedge
// bow (rotated box), two masts with triangular sails. Laid out on a 12-unit
// hull and scaled to BoatConfig.Length. Vessel frame: forward +X, up +Y,
// starboard +Z; the hull sits on the water (y = 0) and reaches Height above.
// Lit (not unshaded like the terrain) so the sails and hull sides read.
public partial class BoatModel : Node3D
{
    private StandardMaterial3D hullMaterial = null!;
    private readonly System.Collections.Generic.List<MeshInstance3D> sails = new();

    public BoatModel(float length, Color hullColor)
    {
        Name = "Model";
        float s = length / 12f;
        hullMaterial = new StandardMaterial3D { AlbedoColor = hullColor };
        var mastMaterial = new StandardMaterial3D { AlbedoColor = BoatConfig.MastColor };
        var sailMaterial = new StandardMaterial3D { AlbedoColor = BoatConfig.SailColor, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

        Add(new BoxMesh { Size = new Vector3(12, 2, 4) * s }, hullMaterial, new Vector3(0, 1, 0) * s);
        // Bow: a square box turned 45° so a corner points forward.
        Add(new BoxMesh { Size = new Vector3(2.83f, 2, 2.83f) * s }, hullMaterial, new Vector3(6, 1, 0) * s,
            new Vector3(0, Mathf.Pi / 4, 0));
        foreach (float fwd in new[] { -2.5f, 2.5f })
        {
            Add(new BoxMesh { Size = new Vector3(0.3f, 8, 0.3f) * s }, mastMaterial, new Vector3(fwd, 6, 0) * s);
            // Triangular sail hanging aft of the mast, a touch to starboard.
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            st.SetNormal(Vector3.Back);
            st.AddVertex(new Vector3(fwd, 3.5f, 0.2f) * s);
            st.AddVertex(new Vector3(fwd, 9f, 0.2f) * s);
            st.AddVertex(new Vector3(fwd - 3.5f, 3.5f, 0.2f) * s);
            sails.Add(Add(st.Commit(), sailMaterial, Vector3.Zero));
        }
    }

    private MeshInstance3D Add(Mesh mesh, Material material, Vector3 position, Vector3? rotation = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        if (rotation is Vector3 r) mi.Rotation = r;
        AddChild(mi);
        return mi;
    }

    // Sails up or down (the SPACE toggle): struck canvas simply disappears,
    // leaving the bare masts, so the state is readable at any zoom.
    public void SetSails(bool up)
    {
        foreach (var sail in sails) sail.Visible = up;
    }

    // Hull glow after a hit, 0..1.
    public void SetHitFlash(float intensity, Color color)
    {
        hullMaterial.EmissionEnabled = intensity > 0;
        hullMaterial.Emission = color;
        hullMaterial.EmissionEnergyMultiplier = intensity;
    }
}
