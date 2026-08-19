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

// Follow camera: locked to a target. Stationary, the boat is screen-centred;
// sailing straight for a while, the centre leads it along its heading ("look
// ahead"), by a screen distance proportional to speed so it feels the same at
// any zoom. Steering bleeds the lead back off, so turns don't swing the view. Wheel still zooms, but there's no cursor
// anchoring — the centre is not ours to move.
public sealed class FollowCameraController : ICameraController
{
    public string Name => "follow";
    public CameraState State { get; private set; } = new(0, 0, Config.BoatFollowZoom);
    public float MinZoom;
    private readonly Boat target;
    private Vector2 lookAhead;   // smoothed world-space offset from boat to centre
    private float straightness;  // 0..1: how long we've been sailing without steering

    public FollowCameraController(Boat target) { this.target = target; }

    public void Enable(CameraState state)
    {
        lookAhead = Vector2.Zero;
        straightness = 0;
        State = new CameraState(target.X, target.Y, State.Zoom > 0 ? State.Zoom : state.Zoom);
    }
    public void Disable() { }

    public void Update(float dt, InputState input, Vector2 view)
    {
        var s = State;
        if (input.WheelDelta != 0)
            s.Zoom = Math.Clamp(s.Zoom * Mathf.Exp(-input.WheelDelta * Config.ZoomWheelSensitivity), MinZoom, Config.MaxZoom);
        // Straightness ramps up while not steering, down while steering, and is
        // eased (smoothstep) so both ends of the ramp are gentle.
        float rampRate = target.Turning ? -1 / Config.FollowLookAheadRampDownSec : 1 / Config.FollowLookAheadRampUpSec;
        straightness = Math.Clamp(straightness + rampRate * dt, 0, 1);
        float eased = straightness * straightness * (3 - 2 * straightness);
        // Target offset: screen px scaled by speed fraction and straightness,
        // converted to world units at the current zoom, pointing along the
        // heading (behind when reversing, since Speed is signed).
        float speedFrac = target.GroundSpeed / BoatConfig.MaxSpeed;   // actual progress, so a boat held by the shore stays centred
        float worldPx = Config.FollowLookAheadPx * speedFrac * eased / s.Zoom;
        var want = new Vector2(Mathf.Cos(target.Heading), Mathf.Sin(target.Heading)) * worldPx;
        // Framerate-independent exponential ease toward the target offset,
        // then a slew cap so a long lead can't whip round with the heading.
        float k = 1 - Mathf.Exp(-dt / Config.FollowLookAheadSmoothSec);
        var step = (want - lookAhead) * k;
        float maxStep = Config.FollowLookAheadMaxPxPerSec / s.Zoom * dt;
        if (step.Length() > maxStep) step = step.Normalized() * maxStep;
        lookAhead += step;
        s.CenterX = target.X + lookAhead.X;
        s.CenterY = target.Y + lookAhead.Y;
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
