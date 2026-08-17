using Godot;

// Top-right readout: fps, view corners in world coords, zoom, camera mode
// and rig, sail-time factor, chunk stats. Money and cargo live in the clock.
public partial class Hud : PanelContainer
{
    private Label label = null!;
    private string lastText = "";
    private int fpsFrames;
    private ulong fpsWindowStart;
    private double fps;
    private float worldW, worldH;

    public void Init(float worldW, float worldH)
    {
        this.worldW = worldW; this.worldH = worldH;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 8, 6));
        label = Fonts.MakeLabel("", Fonts.Mono, 11, new Color("#dddddd"));
        label.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(label);
        SetAnchorsPreset(LayoutPreset.TopRight);
        GrowHorizontal = GrowDirection.Begin;
        fpsWindowStart = Time.GetTicksMsec();
    }

    public void Update(CameraState state, Vector2 view, CameraRig rig, float minZoom, string mode, float timeScale, TerrainChunks.Stats chunks)
    {
        // FPS over the last ~half second, refreshed on the boundary.
        fpsFrames++;
        ulong now = Time.GetTicksMsec();
        if (now - fpsWindowStart >= 500)
        {
            fps = fpsFrames * 1000.0 / (now - fpsWindowStart);
            fpsFrames = 0;
            fpsWindowStart = now;
        }
        var r = rig.ViewRect(state, view);
        float Clamp(float v, float max) => Mathf.Clamp(v, 0, max);
        string pending = chunks.Pending > 0 ? $" (+{chunks.Pending} building)" : "";
        string text =
            $"{fps:F0} fps\n" +
            $"TL ({Mathf.RoundToInt(Clamp(r.Position.X, worldW))}, {Mathf.RoundToInt(Clamp(r.Position.Y, worldH))})\n" +
            $"BR ({Mathf.RoundToInt(Clamp(r.End.X, worldW))}, {Mathf.RoundToInt(Clamp(r.End.Y, worldH))})\n" +
            $"zoom {state.Zoom:F3} px/u ({state.Zoom / minZoom:F1}x)\n" +
            $"cam {mode} [C]  rig {rig.Name} [V]\n" +
            $"sail time x{timeScale:F2}\n" +
            $"chunks {chunks.Visible} vis / {chunks.Cached} cached, lod x{chunks.Step}{pending}";
        if (text != lastText)
        {
            label.Text = text;
            lastText = text;
        }
        Position = new Vector2(view.X - Size.X - 8, 8);
    }
}
