using Godot;

// Bottom-right minimap: a window onto part of the map (MinimapWorldFraction
// of the world) that follows the camera, with a rectangle marking the view.
// The window is clamped to the world, so near an edge it stops scrolling.
public partial class Minimap : Control
{
    private ImageTexture mapTexture = null!;
    private TerrainMap map = null!;
    private CameraState state = new(0, 0, 1);
    private Vector2 view;
    private CameraRig? rig;
    private float windowW, windowH;

    // The map texture is the terrain plane's (shared — it's the whole world).
    public void Init(TerrainMap map, ImageTexture texture)
    {
        this.map = map;
        mapTexture = texture;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
        int s = Config.MinimapSizePx;
        // Border sits outside the drawing area, like the CSS 1px border.
        Size = new Vector2(s + 2, s + 2);
        windowW = Mathf.Min(map.WorldW * Config.MinimapWorldFraction, map.WorldW);
        windowH = Mathf.Min(map.WorldH * Config.MinimapWorldFraction, map.WorldH);
    }

    public void Update(CameraState state, Vector2 view, CameraRig rig)
    {
        this.state = state; this.view = view; this.rig = rig;
        int m = Config.MinimapMargin;
        Position = new Vector2(view.X - Size.X - m, view.Y - Size.Y - m);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (rig == null) return;
        int sizePx = Config.MinimapSizePx;
        DrawRect(new Rect2(0.5f, 0.5f, sizePx + 1, sizePx + 1), new Color("#666666"), false, 1);
        // Window in world coords, centred on the camera and clamped to bounds.
        float left = Mathf.Clamp(state.CenterX - windowW / 2, 0, map.WorldW - windowW);
        float top = Mathf.Clamp(state.CenterY - windowH / 2, 0, map.WorldH - windowH);
        float sx = left / map.WorldW * map.Width;
        float sy = top / map.WorldH * map.Height;
        float sw = windowW / map.WorldW * map.Width;
        float sh = windowH / map.WorldH * map.Height;
        DrawTextureRectRegion(mapTexture, new Rect2(1, 1, sizePx, sizePx), new Rect2(sx, sy, sw, sh));

        // View rect, clipped to the window. Like the web version this is the
        // top-down rectangle (no pitch foreshortening).
        float halfW = view.X / (2 * state.Zoom), halfH = view.Y / (2 * state.Zoom);
        var r = new Rect2(state.CenterX - halfW, state.CenterY - halfH, 2 * halfW, 2 * halfH);
        float ToX(float wx) => Mathf.Clamp((wx - left) / windowW * sizePx, 0, sizePx);
        float ToY(float wy) => Mathf.Clamp((wy - top) / windowH * sizePx, 0, sizePx);
        float x0 = ToX(r.Position.X), x1 = ToX(r.End.X);
        float y0 = ToY(r.Position.Y), y1 = ToY(r.End.Y);
        DrawRect(new Rect2(
            1 + Mathf.Clamp(x0, 0.5f, sizePx - 0.5f), 1 + Mathf.Clamp(y0, 0.5f, sizePx - 0.5f),
            Mathf.Max(x1 - x0 - 1, 1), Mathf.Max(y1 - y0 - 1, 1)),
            Config.MinimapRectColor, false, 1);
    }
}
