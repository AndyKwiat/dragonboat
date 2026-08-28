using Godot;

// Bottom left: which way the wind is blowing and how hard, as an arrow
// pointing the way the map does — the world direction is projected through
// the live camera rig, so the arrow swings when the rig changes. The line
// above it is the same thing in words. Rules: boat/Sailing.cs.
public partial class WindDial : PanelContainer
{
    // The arrow itself; drawn rather than built out of nodes so it can spin.
    private sealed partial class Face : Control
    {
        public float AngleRad;      // screen angle the wind blows towards
        public float NorthRad;      // screen angle of world north, for the tick
        public float Strength;      // 0..1 of Config.WindDialFullForce

        public override void _Draw()
        {
            float r = Config.WindDialSizePx / 2f;
            var c = new Vector2(r, r);
            DrawArc(c, r - 1, 0, Mathf.Tau, 48, Config.WindDialRingColor, 1, true);
            // A north tick, projected the same way, so the dial reads against
            // the map however the rig is turned.
            var n = new Vector2(Mathf.Cos(NorthRad), Mathf.Sin(NorthRad));
            DrawLine(c + n * (r - 6), c + n * (r - 1), Config.WindDialRingColor, 1, true);
            var size = Fonts.MonoBold.GetStringSize("N", fontSize: Config.WindDialTickFontPx);
            DrawString(Fonts.MonoBold, c + n * (r - 10) + new Vector2(-size.X / 2, size.Y / 3),
                "N", HorizontalAlignment.Left, -1, Config.WindDialTickFontPx, Config.WindDialRingColor);
            var dir = new Vector2(Mathf.Cos(AngleRad), Mathf.Sin(AngleRad));
            var side = new Vector2(-dir.Y, dir.X);
            var colour = Config.WindDialCalmColor.Lerp(Config.WindDialBlowColor, Strength);
            // A calm still draws a stub, so the dial never looks broken.
            float len = (r - 12) * Mathf.Lerp(0.55f, 1f, Strength);
            float head = Config.WindDialHeadPx;
            var tip = c + dir * len;
            DrawLine(c - dir * len * 0.55f, tip - dir * head * 0.8f, colour, 3, true);
            DrawColoredPolygon(new[]
            {
                tip,
                tip - dir * head + side * head * 0.55f,
                tip - dir * head - side * head * 0.55f,
            }, colour);
        }
    }

    private Label caption = null!;
    private Face face = null!;
    private Wind wind = null!;

    public void Init(Wind wind)
    {
        this.wind = wind;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        caption = Fonts.MakeLabel("", Fonts.MonoBold, Config.WindDialFontPx, Config.DayColor);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(caption);
        face = new Face
        {
            CustomMinimumSize = new Vector2(Config.WindDialSizePx, Config.WindDialSizePx),
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        box.AddChild(face);
        AddChild(box);
    }

    // `x, y` is the boat: the wind is projected at the boat's own spot on
    // the water, which is where the perspective rig's foreshortening is the
    // one the player is reading.
    public void Update(CameraRig rig, float x, float y, Vector2 view)
    {
        caption.Text = $"WIND {wind.Force:F0} TO THE {wind.Towards()}";
        float b = wind.BearingRad;
        float far = Config.WindDialProbeUnits;
        // Project the world direction to screen through the rig; if either
        // end falls behind the camera, keep the angle we last had.
        if (rig.WorldToScreen(x, y, 0, out var p0) &&
            rig.WorldToScreen(x + Mathf.Cos(b) * far, y + Mathf.Sin(b) * far, 0, out var p1) &&
            !p0.IsEqualApprox(p1))
            face.AngleRad = (p1 - p0).Angle();
        if (rig.WorldToScreen(x, y, 0, out var n0) &&
            rig.WorldToScreen(x, y - far, 0, out var n1) && !n0.IsEqualApprox(n1))
            face.NorthRad = (n1 - n0).Angle();
        // The same "fully powered" reference the sails use, so the arrow
        // filling out means the rig is filling out.
        face.Strength = BoatConfig.SailFullWind <= 0 ? 1
            : Mathf.Clamp(wind.Force / BoatConfig.SailFullWind, 0, 1);
        face.QueueRedraw();
        Position = new Vector2(8, view.Y - Size.Y - 8);
    }
}
