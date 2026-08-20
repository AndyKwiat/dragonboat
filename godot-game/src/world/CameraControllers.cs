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
// any zoom — but never so far the boat leaves the screen (the lead is capped
// in screen px against the view, since a full crew sails past MaxSpeed).
// Steering bleeds the lead back off, so turns don't swing the view. Wheel
// still zooms, but there's no cursor anchoring — the centre is not ours to
// move.
public sealed class FollowCameraController : ICameraController
{
    public string Name => "follow";
    public CameraState State { get; private set; } = new(0, 0, Config.BoatFollowZoom);
    public float MinZoom;
    public CameraRig Rig;
    private readonly Boat target;
    private Vector2 lookAhead;   // smoothed world-space offset from boat to centre
    private float straightness;  // 0..1: how long we've been sailing without steering

    public FollowCameraController(Boat target, CameraRig rig) { this.target = target; Rig = rig; }

    // Scale a world-space lead back until the boat (sitting at minus the
    // lead from centre) keeps FollowLookAheadMarginPx of screen on each
    // side — the same focus-plane projection the edge camera uses.
    private Vector2 CapToView(Vector2 lead, float zoom, Vector2 view)
    {
        float limX = Math.Max(view.X / 2 - Config.FollowLookAheadMarginPx, 0);
        float limY = Math.Max(view.Y / 2 - Config.FollowLookAheadMarginPx, 0);
        float sx = Math.Abs(lead.Dot(Rig.Right)) * zoom;
        float sy = Math.Abs(lead.Dot(Rig.Forward)) * zoom * Rig.GroundYScale;
        float scale = 1;
        if (sx > limX) scale = Math.Min(scale, limX / sx);
        if (sy > limY) scale = Math.Min(scale, limY / sy);
        return lead * scale;
    }

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
        var want = CapToView(new Vector2(Mathf.Cos(target.Heading), Mathf.Sin(target.Heading)) * worldPx, s.Zoom, view);
        // Framerate-independent exponential ease toward the target offset,
        // then a slew cap so a long lead can't whip round with the heading.
        float k = 1 - Mathf.Exp(-dt / Config.FollowLookAheadSmoothSec);
        var step = (want - lookAhead) * k;
        float maxStep = Config.FollowLookAheadMaxPxPerSec / s.Zoom * dt;
        if (step.Length() > maxStep) step = step.Normalized() * maxStep;
        // Re-capped after the step: a zoom-in can shove an already-eased
        // lead past the view, and the ease alone would bring it back slowly.
        lookAhead = CapToView(lookAhead + step, s.Zoom, view);
        s.CenterX = target.X + lookAhead.X;
        s.CenterY = target.Y + lookAhead.Y;
        input.ConsumeFrameDeltas();
    }
}

// Edge camera: the world holds still and the boat moves across the screen;
// the camera pans only when the boat would enter the EdgeCamMarginPx band at
// a screen edge, and then just enough to keep it out of the band — so it
// never scrolls while you manoeuvre mid-screen. Wheel zooms about the centre
// (the edge rule then reels the boat back in if the zoom pushed it out).
public sealed class EdgeCameraController : ICameraController
{
    public string Name => "edge";
    public CameraState State { get; private set; } = new(0, 0, Config.BoatFollowZoom);
    public float MinZoom;
    public CameraRig Rig;
    private readonly Boat target;

    public EdgeCameraController(Boat target, CameraRig rig) { this.target = target; Rig = rig; }

    // Keep the handed-over view: if the boat is on screen nothing moves until
    // it reaches an edge; if it is off screen the edge rule pans to it.
    public void Enable(CameraState state) { State = state.Clone(); }
    public void Disable() { }

    public void Update(float dt, InputState input, Vector2 view)
    {
        var s = State;
        if (input.WheelDelta != 0)
            s.Zoom = Math.Clamp(s.Zoom * Mathf.Exp(-input.WheelDelta * Config.ZoomWheelSensitivity), MinZoom, Config.MaxZoom);

        // The boat's offset from the focus in screen px — the same linear
        // focus-plane math as ScreenDeltaToGround, inverted (exact for the
        // orthographic rig, close enough near the focus for perspective).
        var d = new Vector2(target.X - s.CenterX, target.Y - s.CenterY);
        float px = d.Dot(Rig.Right) * s.Zoom;
        float py = -d.Dot(Rig.Forward) * s.Zoom * Rig.GroundYScale;
        // How far past the allowed box it is (margins can't exceed the view).
        float limitX = Math.Max(view.X / 2 - Config.EdgeCamMarginPx, 0);
        float limitY = Math.Max(view.Y / 2 - Config.EdgeCamMarginPx, 0);
        float ex = Math.Abs(px) > limitX ? px - Math.Sign(px) * limitX : 0;
        float ey = Math.Abs(py) > limitY ? py - Math.Sign(py) * limitY : 0;
        if (ex != 0 || ey != 0)
        {
            var g = Rig.ScreenDeltaToGround(ex, ey, s.Zoom);
            s.CenterX += g.X;
            s.CenterY += g.Y;
        }
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
