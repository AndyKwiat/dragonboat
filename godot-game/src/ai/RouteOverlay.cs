using Godot;

// Debug view of what the AI boats are actually steering for: the route each
// one has left to sail, drawn as a line through its remaining waypoints,
// with a tick at the leg it is making for now and a cross where it is bound.
// Toggled with the nav mesh on N (docs/nav.md, docs/ai.md).
//
// Rebuilt every frame while it is up — routes are consumed as the boats
// sail, and a handful of line segments a frame is nothing. Sits just above
// the nav mesh's own overlay so the route reads on top of the nodes.
public partial class RouteOverlay : Node3D
{
    private const float DrawY = 0.15f;
    private const float TickSize = 4f;    // the cross at a waypoint, world units
    private static readonly Color RouteColor = new Color(1f, 0.95f, 0.3f, 0.9f);
    private static readonly Color TargetColor = new Color(1f, 0.4f, 0.2f, 1f);

    private AiBoats boats = null!;
    private MeshInstance3D lines = null!;

    public void Init(AiBoats boats)
    {
        Name = "RouteOverlay";
        this.boats = boats;
        Visible = false;
    }

    public override void _Ready()
    {
        lines = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1, 1, 1, 1),
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                RenderPriority = 2,   // over the nav mesh, which is over the currents
            },
            Position = new Vector3(0, DrawY, 0),
        };
        AddChild(lines);
    }

    public void Toggle() => Visible = !Visible;

    public override void _Process(double delta)
    {
        if (!Visible || boats.Count == 0) return;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        bool any = false;
        foreach (var boat in boats.Boats)
        {
            if (boat.Route == null || boat.Arrived) continue;
            any = true;
            // From the boat to the leg it is steering for, then on through
            // everything still ahead of it.
            st.SetColor(RouteColor);
            var from = new Vector2(boat.X, boat.Y);
            for (int i = boat.Leg; i < boat.Route.Waypoints.Count; i++)
            {
                var to = new Vector2(boat.Route.Waypoints[i].X, boat.Route.Waypoints[i].Y);
                Line(st, from, to);
                Cross(st, to, TickSize / 2);
                from = to;
            }
            if (boat.Target is Vector2 target)
            {
                st.SetColor(TargetColor);
                Cross(st, target, TickSize);
                st.SetColor(RouteColor);
            }
        }
        lines.Mesh = any ? st.Commit() : null;
    }

    private static void Line(SurfaceTool st, Vector2 a, Vector2 b)
    {
        st.AddVertex(new Vector3(a.X, 0, a.Y));
        st.AddVertex(new Vector3(b.X, 0, b.Y));
    }

    private static void Cross(SurfaceTool st, Vector2 at, float r)
    {
        Line(st, new Vector2(at.X - r, at.Y), new Vector2(at.X + r, at.Y));
        Line(st, new Vector2(at.X, at.Y - r), new Vector2(at.X, at.Y + r));
    }
}
