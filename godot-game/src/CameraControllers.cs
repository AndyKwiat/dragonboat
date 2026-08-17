using Godot;
using System;

// A camera mode. Main owns a set of these and switches between them, handing
// the current CameraState across via Enable() so transitions are seamless.
// Controllers mutate their state in Update(); Main clamps and applies it.
public interface ICameraController
{
    string Name { get; }
    CameraState State { get; }
    void Enable(CameraState state);
    void Disable();
    void Update(float dt, InputState input, Vector2 view);
}

// Follow camera: locked to a target (the boat stays screen-centred). Wheel
// still zooms, but there's no cursor anchoring — the centre is not ours to move.
public sealed class FollowCameraController : ICameraController
{
    public string Name => "follow";
    public CameraState State { get; private set; } = new(0, 0, Config.BoatFollowZoom);
    public float MinZoom;
    private readonly Boat target;

    public FollowCameraController(Boat target) { this.target = target; }

    public void Enable(CameraState state)
    {
        State = new CameraState(target.X, target.Y, State.Zoom > 0 ? State.Zoom : state.Zoom);
    }
    public void Disable() { }

    public void Update(float dt, InputState input, Vector2 view)
    {
        var s = State;
        if (input.WheelDelta != 0)
            s.Zoom = Math.Clamp(s.Zoom * Mathf.Exp(-input.WheelDelta * Config.ZoomWheelSensitivity), MinZoom, Config.MaxZoom);
        s.CenterX = target.X;
        s.CenterY = target.Y;
        input.ConsumeFrameDeltas();
    }
}

// Free camera: WASD/arrow pan, drag pan, wheel zoom anchored at the cursor.
public sealed class FreeCameraController : ICameraController
{
    public string Name => "free";
    public CameraState State { get; private set; } = new(0, 0, 1);
    public float MinZoom;
    public CameraRig Rig;

    public FreeCameraController(CameraRig rig) { Rig = rig; }

    public void Enable(CameraState state) { State = state.Clone(); }
    public void Disable() { }

    public void Update(float dt, InputState input, Vector2 view)
    {
        var s = State;
        // Keyboard pan, constant screen speed regardless of zoom.
        float panPx = Config.KeyPanPxPerSec * dt;
        float dx = 0, dy = 0;
        if (input.Down(Key.A, Key.Left)) dx -= 1;
        if (input.Down(Key.D, Key.Right)) dx += 1;
        if (input.Down(Key.W, Key.Up)) dy -= 1;
        if (input.Down(Key.S, Key.Down)) dy += 1;
        // Screen-space pan and drag, turned into ground movement by the rig
        // (yaw rotates it, pitch foreshortens the vertical axis).
        var g = Rig.ScreenDeltaToGround(dx * panPx - input.DragDeltaPx.X, dy * panPx - input.DragDeltaPx.Y, s.Zoom);
        s.CenterX += g.X;
        s.CenterY += g.Y;

        // Wheel zoom toward the cursor: the world point under the cursor stays put.
        if (input.WheelDelta != 0)
        {
            float oldZoom = s.Zoom;
            float newZoom = Math.Clamp(oldZoom * Mathf.Exp(-input.WheelDelta * Config.ZoomWheelSensitivity), MinZoom, Config.MaxZoom);
            if (newZoom != oldZoom)
            {
                float px = input.CursorPx.X - view.X / 2;
                float py = input.CursorPx.Y - view.Y / 2;
                var a = Rig.ScreenDeltaToGround(px, py, 1);
                s.CenterX += a.X * (1 / oldZoom - 1 / newZoom);
                s.CenterY += a.Y * (1 / oldZoom - 1 / newZoom);
                s.Zoom = newZoom;
            }
        }
        input.ConsumeFrameDeltas();
    }
}
