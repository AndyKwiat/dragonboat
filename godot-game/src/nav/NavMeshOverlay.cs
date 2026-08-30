using Godot;
using System;

// Debug view of the nav mesh (N key, or --nav at launch): one translucent
// quad per node, inset so the gaps draw the node boundaries, plus an outline
// pass so the structure survives being zoomed out. Nothing else reads the
// mesh yet — this is how you look at it (docs/nav.md).
//
// Built the way Currents draws its zones: SurfaceTool triangles on plain
// MeshInstance3D children, unshaded and alpha-blended, with the height set
// on the child's Position. The Y ladder is water 0, boat shadow 0.03,
// current zones 0.05, current arrows 0.09, US, then the lowest land column
// top at 0.3 (Config.TerrainHeights) — so land still occludes us properly.
public partial class NavMeshOverlay : Node3D
{
    private const float FillY = 0.12f, LineY = 0.13f;
    // World units trimmed off each side of a node's quad, so neighbours don't
    // touch and every node reads as its own tile.
    private const float InsetUnits = 0.5f;
    private const float FillAlpha = 0.30f, LineAlpha = 0.85f;

    private NavMesh nav = null!;
    private bool built;

    public void Init(NavMesh nav)
    {
        Name = "NavMeshOverlay";
        this.nav = nav;
        Visible = false;
    }

    // First show pays for the mesh; a run that never presses N pays nothing.
    public void Toggle()
    {
        if (!built) { Build(); built = true; }
        Visible = !Visible;
    }

    private void Build()
    {
        var fills = new SurfaceTool();
        fills.Begin(Mesh.PrimitiveType.Triangles);
        fills.SetNormal(Vector3.Up);
        var lines = new SurfaceTool();
        lines.Begin(Mesh.PrimitiveType.Lines);
        for (int i = 0; i < nav.Nodes.Count; i++)
        {
            nav.WorldRect(i, out float x0, out float y0, out float x1, out float y1);
            x0 += InsetUnits; y0 += InsetUnits; x1 -= InsetUnits; y1 -= InsetUnits;
            var c = ColorFor(nav.Nodes[i]);
            // The colour must be set before the FIRST vertex of a surface or
            // the surface is committed without a colour array at all and
            // every later SetColor is silently dropped.
            fills.SetColor(new Color(c, FillAlpha));
            Quad(fills, P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1));
            lines.SetColor(new Color(c, LineAlpha));
            Outline(lines, x0, y0, x1, y1);
        }
        AddChild(new MeshInstance3D { Mesh = fills.Commit(), MaterialOverride = Mat(), Position = new Vector3(0, FillY, 0) });
        AddChild(new MeshInstance3D { Mesh = lines.Commit(), MaterialOverride = Mat(), Position = new Vector3(0, LineY, 0) });
    }

    // Colour by node SIZE, so the quadtree itself is what you see: the open
    // sea's huge leaves and a river's 1x1 chain come out at opposite ends of
    // the ramp. Both real maps are a single body, so colouring by body would
    // paint the whole world one flat colour and show nothing — the body only
    // shifts the hue when a map actually has more than one.
    private Color ColorFor(in NavNode node)
    {
        int steps = 0;
        for (int s = node.Size; s > 1; s >>= 1) steps++;
        float t = Math.Min(steps / 6f, 1f);              // 1x1 .. 64x64 and up
        float hue = 0.58f - t * 0.5f;                    // blue (small) -> red (large)
        if (nav.BodyCount > 1) hue = Mathf.PosMod(hue + node.Body * 0.37f, 1f);
        return Color.FromHsv(hue, 0.75f, 1f);
    }

    // World (x, y) -> Godot (x, 0, y); the height is on the node.
    private static Vector3 P(float x, float y) => new(x, 0, y);

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    private static void Outline(SurfaceTool st, float x0, float y0, float x1, float y1)
    {
        Vector3 a = P(x0, y0), b = P(x1, y0), c = P(x1, y1), d = P(x0, y1);
        st.AddVertex(a); st.AddVertex(b);
        st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(c); st.AddVertex(d);
        st.AddVertex(d); st.AddVertex(a);
    }

    // AlbedoColor stays opaque white: with vertex colours as albedo the two
    // alphas MULTIPLY, so the material must not dim what the vertices asked
    // for. RenderPriority puts us above the current zones — both meshes span
    // the whole world, so the transparency sort between them is otherwise
    // arbitrary.
    private static StandardMaterial3D Mat() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(1, 1, 1, 1),
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = 1,
    };
}
