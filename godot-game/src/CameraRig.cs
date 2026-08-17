using Godot;
using System;

// World coordinates: (0,0) top-left, y down; height is up. In Godot space
// that is X = world x, Z = world y, Y = height (see WorldToGodot), so
// "north" (world -y) is -Z. zoom = screen px per world unit at the camera's
// focus point on the ground.
public sealed class CameraState
{
    public float CenterX, CenterY, Zoom;
    public CameraState(float cx, float cy, float zoom) { CenterX = cx; CenterY = cy; Zoom = zoom; }
    public CameraState Clone() => new(CenterX, CenterY, Zoom);
}

// A camera rig: projection type plus the direction the camera looks from.
// Separate from camera modes (which decide *where* the focus is); Main
// switches rigs with V.
public sealed class CameraRig
{
    public readonly string Name;
    public readonly Camera3D Camera;
    public readonly bool Perspective;
    public readonly float Pitch;   // radians from the ground
    public readonly float Yaw;     // radians, 0 = looking north (world -y)
    public readonly float FovDeg;
    // Unit vectors on the ground (world xy): screen-up and screen-right.
    public readonly Vector2 Forward, Right;
    // Ground distances along screen-vertical are foreshortened by this at the focus.
    public readonly float GroundYScale;

    public CameraRig(string name)
    {
        var cfg = Config.Rigs[name];
        Name = name;
        Perspective = cfg.Perspective;
        Pitch = Mathf.DegToRad(cfg.PitchDeg);
        Yaw = Mathf.DegToRad(cfg.YawDeg);
        FovDeg = cfg.FovDeg;
        // yaw 0: forward = north (0,-1), right = east (1,0); yaw turns them clockwise on the map.
        Forward = new Vector2(Mathf.Sin(Yaw), -Mathf.Cos(Yaw));
        Right = new Vector2(Mathf.Cos(Yaw), Mathf.Sin(Yaw));
        GroundYScale = Mathf.Sin(Pitch);
        Camera = new Camera3D { Name = $"Camera_{name}", KeepAspect = Camera3D.KeepAspectEnum.Height };
        Camera.Projection = Perspective ? Camera3D.ProjectionType.Perspective : Camera3D.ProjectionType.Orthogonal;
        if (Perspective) Camera.Fov = FovDeg;
    }

    public static Vector3 WorldToGodot(float x, float y, float h) => new(x, h, y);

    // Screen px delta -> ground delta (world units) at the focus point.
    public Vector2 ScreenDeltaToGround(float dxPx, float dyPx, float zoom)
    {
        float rx = dxPx / zoom;
        float fy = -dyPx / (zoom * GroundYScale);   // screen-down is backwards along forward
        return Right * rx + Forward * fy;
    }

    // Distance from the camera to its focus point. Perspective: the focus
    // plane must show heightPx / zoom world units top to bottom.
    public float FocusDistanceFor(CameraState state, Vector2 view)
    {
        if (Perspective) return view.Y / (2 * state.Zoom) / Mathf.Tan(Mathf.DegToRad(FovDeg) / 2);
        return Config.OrthoDistance;
    }

    // Place the camera behind the focus (opposite Forward) looking along
    // Forward and down at Pitch, so screen-up is Forward on the ground.
    public void Apply(CameraState state, Vector2 view)
    {
        float d = FocusDistanceFor(state, view);
        float back = d * Mathf.Cos(Pitch);
        var focus = WorldToGodot(state.CenterX, state.CenterY, 0);
        var pos = focus + new Vector3(-Forward.X * back, d * Mathf.Sin(Pitch), -Forward.Y * back);
        Camera.Position = pos;
        Camera.LookAt(focus, Vector3.Up);
        if (Perspective)
        {
            Camera.Near = Mathf.Max(0.1f, d * 0.02f);
            Camera.Far = d * 4 + 100;
        }
        else
        {
            Camera.Size = view.Y / state.Zoom;   // KeepAspect=Height: size is the vertical extent
            Camera.Near = 1;
            Camera.Far = d * 3;
        }
    }

    // World point (x, y, height) -> screen px. False if behind the camera.
    public bool WorldToScreen(float x, float y, float h, out Vector2 px)
    {
        var p = WorldToGodot(x, y, h);
        if (Camera.IsPositionBehind(p)) { px = Vector2.Zero; return false; }
        px = Camera.UnprojectPosition(p);
        return true;
    }

    // Screen px -> world point on the ground plane. Rays that miss the ground
    // (or hit past maxDistance) are clamped to maxDistance along the ray.
    public Vector2 ScreenToGround(Vector2 px, float maxDistance)
    {
        var o = Camera.ProjectRayOrigin(px);
        var dir = Camera.ProjectRayNormal(px);
        float t = maxDistance;
        if (dir.Y < -1e-6f) t = Mathf.Min(t, -o.Y / dir.Y);
        var hit = o + dir * t;
        return new Vector2(hit.X, hit.Z);
    }

    // Approximate axis-aligned view rectangle in world coords (exact only
    // top-down; used by the HUD readout and minimap).
    public Rect2 ViewRect(CameraState state, Vector2 view)
    {
        float halfW = view.X / (2 * state.Zoom);
        float halfH = view.Y / (2 * state.Zoom * GroundYScale);
        return new Rect2(state.CenterX - halfW, state.CenterY - halfH, 2 * halfW, 2 * halfH);
    }

    // Zoom at which the whole world fits on screen (focus-plane scale).
    public static float MinZoomFor(Vector2 view, float worldW, float worldH) =>
        Mathf.Min(view.X / worldW, view.Y / worldH);

    // Keep the focus point inside the world; if the view is larger than the
    // world on an axis, lock that axis centered (letterboxing).
    public static void Clamp(CameraState s, Vector2 view, float worldW, float worldH)
    {
        float halfW = view.X / (2 * s.Zoom);
        float halfH = view.Y / (2 * s.Zoom);
        s.CenterX = 2 * halfW >= worldW ? worldW / 2 : Math.Clamp(s.CenterX, halfW, worldW - halfW);
        s.CenterY = 2 * halfH >= worldH ? worldH / 2 : Math.Clamp(s.CenterY, halfH, worldH - halfH);
    }
}
