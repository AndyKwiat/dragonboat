using Godot;
using System.Collections.Generic;

// Shared input aggregation so camera controllers don't each own listeners.
// Deltas accumulate between frames; the active controller reads them in
// Update() and then calls ConsumeFrameDeltas(). Lives in _UnhandledInput so
// anything the GUI (city popup) consumes never reaches the map.
public partial class InputState : Node
{
    public readonly HashSet<Key> Keys = new();
    /// Keys that went down since the last ConsumeFrameDeltas() — for one-shot actions.
    public readonly HashSet<Key> Pressed = new();
    public bool Dragging;
    /// A click (press+release without dragging) happened this frame.
    public bool Clicked;
    public Vector2 DragDeltaPx;
    public float WheelDelta;
    public Vector2 CursorPx;
    /// False until the mouse has been seen at all (scripted runs never move it).
    public bool HasCursor;

    private Vector2? pressAt;
    private const float ClickSlopPx = 4f;
    // One wheel notch in the browser is ~100 deltaY px; trackpad pans arrive
    // as pan gestures with small deltas.
    private const float WheelNotchPx = 100f;
    private const float PanGesturePx = 40f;

    public void ConsumeFrameDeltas()
    {
        DragDeltaPx = Vector2.Zero;
        WheelDelta = 0;
        Pressed.Clear();
        Clicked = false;
    }

    public bool Down(params Key[] keys)
    {
        foreach (var k in keys) if (Keys.Contains(k)) return true;
        return false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) Keys.Clear();
    }

    // A scripted run takes its input from the command line — --hold, --press,
    // --fire — and Main injects those straight into the sets below. Real
    // devices are a hazard there rather than a feature: the window takes
    // focus when it opens, so a keystroke meant for another application
    // lands in the game and silently changes the run. A stray space strikes
    // the sails; a stray click fires a cannon. Set for every --screenshot
    // run (see Main.ParseArgs), which is exactly the runs nobody is driving.
    public bool IgnoreDevices;

    public override void _UnhandledInput(InputEvent e)
    {
        if (IgnoreDevices) return;
        switch (e)
        {
            case InputEventKey key:
                if (key.CtrlPressed || key.MetaPressed) return;
                var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                if (key.Pressed)
                {
                    if (!key.Echo) Pressed.Add(code);
                    Keys.Add(code);
                }
                else Keys.Remove(code);
                break;

            case InputEventMouseButton mb:
                CursorPx = mb.Position; HasCursor = true;
                if (mb.ButtonIndex == MouseButton.WheelUp) WheelDelta -= WheelNotchPx * mb.Factor;
                else if (mb.ButtonIndex == MouseButton.WheelDown) WheelDelta += WheelNotchPx * mb.Factor;
                else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Middle)
                {
                    if (mb.Pressed)
                    {
                        Dragging = true;
                        pressAt = mb.ButtonIndex == MouseButton.Left ? mb.Position : null;
                    }
                    else
                    {
                        if (pressAt is Vector2 p && mb.ButtonIndex == MouseButton.Left &&
                            p.DistanceTo(mb.Position) <= ClickSlopPx)
                            Clicked = true;
                        pressAt = null;
                        Dragging = false;
                    }
                }
                break;

            case InputEventMouseMotion mm:
                CursorPx = mm.Position; HasCursor = true;
                if (Dragging) DragDeltaPx += mm.Relative;
                break;

            case InputEventPanGesture pan:
                CursorPx = pan.Position;
                WheelDelta += pan.Delta.Y * PanGesturePx;
                break;
        }
    }
}
