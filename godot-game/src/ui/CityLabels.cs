using Godot;
using System.Collections.Generic;

// Overlay drawing a name label for every city on screen, plus the
// hit-testing for hover/click: a hit is "within CityHitRadiusPx of the city
// centre, OR inside the label box padded by CityHitPadPx". Labels hang off
// the top of the city block.
public partial class CityLabels : Control
{
    private sealed class Entry
    {
        public City City = null!;
        public Label Label = null!;
        public float HalfWidthPx, HeightPx, MinZoom;
        public bool Visible, Hovered;
        public Vector2 Screen;
        public Color Color;
    }

    private readonly List<Entry> entries = new();

    public void Init(List<City> cities)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var city in cities)
        {
            var style = Config.CityLabel[city.Size];
            var font = style.Heavy ? Fonts.UiBold : style.Bold ? Fonts.UiSemiBold : Fonts.Ui;
            var label = Fonts.MakeLabel(city.Name, font, style.FontPx, style.Color);
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            label.AddThemeConstantOverride("shadow_outline_size", 2);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
            label.AddThemeConstantOverride("outline_size", 2);
            label.Visible = false;
            AddChild(label);
            entries.Add(new Entry
            {
                City = city, Label = label,
                HalfWidthPx = font.GetStringSize(city.Name, HorizontalAlignment.Left, -1, style.FontPx).X / 2,
                HeightPx = style.FontPx * 1.3f,
                MinZoom = style.MinZoom,
                Color = style.Color,
            });
        }
    }

    private void SetHovered(Entry e, bool hovered)
    {
        if (e.Hovered == hovered) return;
        e.Hovered = hovered;
        Fonts.SetColor(e.Label, hovered ? Config.CityHoverColor : e.Color);
    }

    /// Positions visible labels and returns the city under the cursor, if any.
    public City? Update(CameraState state, Vector2 view, Vector2 cursorPx, CameraRig rig)
    {
        const float margin = 200; // px of slack so labels don't pop at the edges
        float cityTop = Config.TerrainHeights[DMap.City];
        Entry? best = null;
        float bestDistance = float.PositiveInfinity;

        foreach (var e in entries)
        {
            bool onScreen = rig.WorldToScreen(e.City.X, e.City.Y, cityTop, out var p);
            bool visible = onScreen && state.Zoom >= e.MinZoom &&
                p.X > -margin && p.X < view.X + margin && p.Y > -margin && p.Y < view.Y + margin;
            if (visible != e.Visible)
            {
                e.Label.Visible = visible;
                e.Visible = visible;
            }
            if (!visible) { SetHovered(e, false); continue; }

            e.Screen = p;
            e.Label.Position = new Vector2(p.X - e.HalfWidthPx, p.Y + Config.CityLabelOffsetPx);

            float dx = cursorPx.X - p.X, dy = cursorPx.Y - p.Y;
            bool inCircle = dx * dx + dy * dy <= Config.CityHitRadiusPx * Config.CityHitRadiusPx;
            bool inLabel = Mathf.Abs(dx) <= e.HalfWidthPx + Config.CityHitPadPx &&
                dy >= Config.CityLabelOffsetPx - Config.CityHitPadPx &&
                dy <= Config.CityLabelOffsetPx + e.HeightPx + Config.CityHitPadPx;
            if (inCircle || inLabel)
            {
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance) { bestDistance = distance; best = e; }
            }
        }
        foreach (var e in entries) SetHovered(e, e == best);
        return best?.City;
    }
}
