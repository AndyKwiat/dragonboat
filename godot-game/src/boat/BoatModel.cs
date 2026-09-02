using Godot;

// The combat prototype's boat, rebuilt from primitives: box hull, a wedge
// bow (rotated box), and one mast with a triangular sail per mast the class
// carries. Laid out on a 12x4 hull and scaled to the class's HullLength and
// HullWidth, so a launch, a merchantman and anything else added to boats.yml
// all come out of this one builder. Vessel frame: forward +X, up +Y,
// starboard +Z; the hull sits on the water (y = 0) and reaches Height above.
// Lit (not unshaded like the terrain) so the sails and hull sides read.
public partial class BoatModel : Node3D
{
    // Where the masts stand on the nominal 12-unit hull: the fore-and-aft
    // span three or more are spread evenly across.
    private const float MastAft = -4.5f, MastFore = 3.5f;

    private StandardMaterial3D hullMaterial = null!;
    private readonly System.Collections.Generic.List<MeshInstance3D> sails = new();

    public BoatModel(BoatClass cls, Color hullColor)
    {
        Name = "Model";
        float s = cls.HullLength / 12f;         // along the keel and upwards
        float b = cls.HullWidth / 4f;           // abeam: a big hull is beamy too
        hullMaterial = new StandardMaterial3D { AlbedoColor = hullColor };
        var mastMaterial = new StandardMaterial3D { AlbedoColor = BoatConfig.MastColor };
        var sailMaterial = new StandardMaterial3D { AlbedoColor = BoatConfig.SailColor, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

        Add(new BoxMesh { Size = new Vector3(12 * s, 2 * s, 4 * b) }, hullMaterial, new Vector3(0, 1, 0) * s);
        // Bow: a square box turned 45° so a corner points forward.
        Add(new BoxMesh { Size = new Vector3(2.83f * s, 2 * s, 2.83f * b) }, hullMaterial, new Vector3(6, 1, 0) * s,
            new Vector3(0, Mathf.Pi / 4, 0));
        for (int i = 0; i < cls.Sails; i++)
        {
            // Evenly spread over the mast span: one amidships, three strung
            // out along a big hull. Two stand on ±2.5 — exactly where the
            // game's one boat has always carried them.
            float fwd = cls.Sails == 1 ? -0.5f
                      : cls.Sails == 2 ? (i == 0 ? -2.5f : 2.5f)
                      : Mathf.Lerp(MastAft, MastFore, i / (float)(cls.Sails - 1));
            Add(new BoxMesh { Size = new Vector3(0.3f, 8, 0.3f) * s }, mastMaterial, new Vector3(fwd, 6, 0) * s);
            // Triangular sail hanging aft of the mast, a touch to starboard.
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            st.SetNormal(Vector3.Back);
            st.AddVertex(new Vector3(fwd * s, 3.5f * s, 0.2f * b));
            st.AddVertex(new Vector3(fwd * s, 9f * s, 0.2f * b));
            st.AddVertex(new Vector3((fwd - 3.5f) * s, 3.5f * s, 0.2f * b));
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
