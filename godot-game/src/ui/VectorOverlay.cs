using Godot;

// Debug readout of the boat's physics: lines from the hull for velocity,
// heading, and each force acting on it, drawn in screen space over the
// world. Velocity is in world units/s, forces in units/s²; both are drawn
// at PxPerUnit so the drag arrow matching the thrust arrow means "at
// terminal speed". Toggle with F (Main.cs).
public partial class VectorOverlay : Control
{
    public const float PxPerUnit = 2.5f;
    private static readonly Color VelColor = new(0.3f, 1f, 0.3f);
    private static readonly Color HeadColor = new(1f, 1f, 1f, 0.8f);
    private static readonly Color ThrustColor = new(1f, 0.85f, 0.2f);
    private static readonly Color DragColor = new(1f, 0.35f, 0.3f);
    private static readonly Color CurrentColor = new(0.3f, 0.9f, 1f);
    private static readonly Color WindColor = new(0.9f, 0.6f, 1f);

    private Boat boat = null!;
    private CameraRig rig = null!;
    private Vector2 origin;
    private bool onScreen;
    private Label legend = null!;

    public void Init(Boat boat)
    {
        this.boat = boat;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        legend = Fonts.MakeLabel("", Fonts.Mono, 11, new Color("#dddddd"));
        legend.Position = new Vector2(8, 200);
        AddChild(legend);
        Visible = false;
    }

    public void Toggle() => Visible = !Visible;

    public void Update(CameraRig rig)
    {
        if (!Visible) return;
        this.rig = rig;
        onScreen = rig.WorldToScreen(boat.X, boat.Y, BoatConfig.Height, out origin);
        var v = boat.Velocity;
        float fwd = boat.Speed;
        float lat = -v.X * Mathf.Sin(boat.Heading) + v.Y * Mathf.Cos(boat.Heading);
        legend.Text =
            $"vel {v.Length():F1} u/s  (fwd {fwd:F1}, lat {lat:F1})  terminal {BoatConfig.TerminalSpeed():F1}\n" +
            $"thrust {boat.ThrustForce.Length():F1}  drag {boat.DragForce.Length():F1}  " +
            $"current {boat.CurrentForce.Length():F1}  wind {boat.WindForce.Length():F1}\n" +
            "green=velocity white=heading yellow=thrust red=drag cyan=current violet=wind";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!onScreen) return;
        // World-plane directions on screen: project a unit step along each axis.
        // (The rig may be rotated/isometric, so screen axes ≠ world axes.)
        rig.WorldToScreen(boat.X + 1, boat.Y, BoatConfig.Height, out var ex);
        rig.WorldToScreen(boat.X, boat.Y + 1, BoatConfig.Height, out var ey);
        Vector2 ax = (ex - origin).Normalized(), ay = (ey - origin).Normalized();
        Vector2 ToScreen(Vector2 w) => (ax * w.X + ay * w.Y) * PxPerUnit;

        Arrow(ToScreen(boat.Forward * 15), HeadColor, 1.5f);
        Arrow(ToScreen(boat.ThrustForce), ThrustColor, 3);
        Arrow(ToScreen(boat.DragForce), DragColor, 3);
        Arrow(ToScreen(boat.CurrentForce), CurrentColor, 3);
        Arrow(ToScreen(boat.WindForce), WindColor, 3);
        Arrow(ToScreen(boat.Velocity), VelColor, 4);
    }

    private void Arrow(Vector2 d, Color c, float width)
    {
        if (d.LengthSquared() < 4) return;
        var tip = origin + d;
        DrawLine(origin, tip, c, width, true);
        var n = d.Normalized();
        var side = new Vector2(-n.Y, n.X);
        float h = Mathf.Min(10, d.Length() / 2);
        DrawLine(tip, tip - n * h + side * h * 0.5f, c, width, true);
        DrawLine(tip, tip - n * h - side * h * 0.5f, c, width, true);
    }
}
