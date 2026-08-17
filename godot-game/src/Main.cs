using Godot;
using System;
using System.Collections.Generic;

// Wiring: load map → world (terrain plane + chunks) → boat → camera rigs and
// modes → UI overlays → per-frame loop (mode/rig toggles → boat → camera →
// clamp → apply → chunks → labels/popup → upkeep → clock/minimap/hud).
//
// Test hooks (user args after `--`): see ParseArgs. Example:
//   Godot --path . -- --screenshot=shot.png --frames=90 --zoom=0.5 --rig=perspective
public partial class Main : Node3D
{
    private DMap map = null!;
    private TerrainPlane terrain = null!;
    private TerrainChunks chunks = null!;
    private Boat boat = null!;
    private InputState input = null!;
    private CameraRig rig = null!;
    private readonly Dictionary<string, CameraRig> rigs = new();
    private FreeCameraController freeCamera = null!;
    private FollowCameraController followCamera = null!;
    private readonly Dictionary<string, ICameraController> controllers = new();
    private static readonly string[] ModeCycle = { "follow", "free" };
    private ICameraController active = null!;

    private Player player = null!;
    private GameTime time = null!;
    private Rations rations = null!;
    private Wages wages = null!;
    private Hud hud = null!;
    private Clock clock = null!;
    private Minimap minimap = null!;
    private CityLabels cityLabels = null!;
    private CityPopup cityPopup = null!;

    // Test hooks.
    private string? screenshotPath;
    private int screenshotFrames = 90;
    private int frameCount;
    private readonly List<Key> heldKeys = new();

    private Vector2 View => GetViewport().GetVisibleRect().Size;

    public override void _Ready()
    {
        var args = ParseArgs();
        GameData.Load();
        map = DMap.Load(Config.MapPath);

        // The world: terrain plane (ground/water) + streamed raised chunks + boat.
        terrain = new TerrainPlane();
        terrain.Build(map);
        AddChild(terrain);
        chunks = new TerrainChunks();
        chunks.Init(map);
        AddChild(chunks);
        boat = new Boat();
        AddChild(boat);

        input = new InputState();
        AddChild(input);

        // Camera rigs (projection + view angle), cycled with V; camera modes
        // decide where the focus is and are independent of the rig.
        foreach (var name in Config.RigCycle)
        {
            var r = new CameraRig(name);
            rigs[name] = r;
            AddChild(r.Camera);
        }
        SetRig(args.TryGetValue("rig", out var rigName) && rigs.ContainsKey(rigName) ? rigName : Config.DefaultRig);

        freeCamera = new FreeCameraController(rig);
        followCamera = new FollowCameraController(boat);
        controllers["free"] = freeCamera;
        controllers["follow"] = followCamera;
        active = followCamera;
        Resize();
        // Start on the boat at the follow camera's own zoom.
        active.Enable(new CameraState(boat.X, boat.Y, Config.BoatFollowZoom));

        player = new Player();
        time = new GameTime();
        rations = new Rations(player, time);
        wages = new Wages(player, time);

        // UI overlays, in draw order (labels under the panels, popup on top).
        var ui = new CanvasLayer();
        AddChild(ui);
        cityLabels = new CityLabels();
        cityLabels.Init(GameData.Cities);
        ui.AddChild(cityLabels);
        hud = new Hud();
        hud.Init(map.WorldW, map.WorldH);
        ui.AddChild(hud);
        clock = new Clock();
        clock.Init(time, rations, player);
        ui.AddChild(clock);
        minimap = new Minimap();
        minimap.Init(map);
        ui.AddChild(minimap);
        cityPopup = new CityPopup();
        cityPopup.Init(player, time);
        ui.AddChild(cityPopup);

        GetTree().Root.SizeChanged += Resize;
        ApplyArgs(args);
    }

    // `--key=value` pairs after `--` on the command line, for scripted runs.
    private static Dictionary<string, string> ParseArgs()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--")) continue;
            int eq = a.IndexOf('=');
            if (eq < 0) args[a[2..]] = "true";
            else args[a[2..eq]] = a[(eq + 1)..];
        }
        return args;
    }

    // --screenshot[=path]  save the viewport after --frames (default 90) and quit
    // --zoom=Z             starting zoom (px per world unit)
    // --rig=name           starting camera rig
    // --mode=free          start in free camera mode
    // --center=x,y         focus point (free mode)
    // --city=Name          open the city popup for this city
    // --size=WxH           window size
    // --hold=W,D           keys held down for the whole run (physical key names)
    private void ApplyArgs(Dictionary<string, string> args)
    {
        if (args.TryGetValue("size", out var size))
        {
            var parts = size.Split('x');
            if (parts.Length == 2) DisplayServer.WindowSetSize(new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])));
        }
        if (args.TryGetValue("mode", out var mode) && controllers.ContainsKey(mode)) SwitchMode(mode);
        var state = active.State;
        if (args.TryGetValue("zoom", out var zoom)) state.Zoom = float.Parse(zoom, System.Globalization.CultureInfo.InvariantCulture);
        if (args.TryGetValue("center", out var center))
        {
            var parts = center.Split(',');
            if (parts.Length == 2)
            {
                state.CenterX = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                state.CenterY = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (args.TryGetValue("city", out var cityName))
        {
            var city = GameData.Cities.Find(c => c.Name == cityName);
            if (city != null) cityPopup.Show(city);
        }
        if (args.TryGetValue("hold", out var hold))
            foreach (var k in hold.Split(',')) heldKeys.Add(Enum.Parse<Key>(k, true));
        if (args.TryGetValue("screenshot", out var shot))
        {
            screenshotPath = shot == "true" ? "screenshot.png" : shot;
            if (args.TryGetValue("frames", out var frames)) screenshotFrames = int.Parse(frames);
        }
    }

    private void SetRig(string name)
    {
        rig = rigs[name];
        foreach (var r in rigs.Values) r.Camera.Current = r == rig;
        if (freeCamera != null) freeCamera.Rig = rig;
    }

    public void SwitchMode(string name)
    {
        if (!controllers.TryGetValue(name, out var next) || next == active) return;
        var state = active.State;
        active.Disable();
        next.Enable(state);
        active = next;
    }

    private void Resize()
    {
        var view = View;
        float minZoom = CameraRig.MinZoomFor(view, map.WorldW, map.WorldH);
        freeCamera.MinZoom = minZoom;
        followCamera.MinZoom = minZoom;
        var state = active.State;
        state.Zoom = Math.Max(state.Zoom, minZoom);
        CameraRig.Clamp(state, view, map.WorldW, map.WorldH);
    }

    public override void _Process(double delta)
    {
        float dt = Math.Min((float)delta, 0.1f);
        var view = View;
        foreach (var k in heldKeys) input.Keys.Add(k);

        // C toggles camera mode. WASD drives the boat in follow mode and pans
        // the camera in free mode, so only one of them consumes the keys.
        if (input.Pressed.Contains(Key.C))
            SwitchMode(ModeCycle[(Array.IndexOf(ModeCycle, active.Name) + 1) % ModeCycle.Length]);
        // V cycles the camera rig (isometric / perspective).
        if (input.Pressed.Contains(Key.V))
        {
            var cycle = Config.RigCycle;
            SetRig(cycle[(Array.IndexOf(cycle, rig.Name) + 1) % cycle.Length]);
        }
        // Latched here: the active controller clears the frame deltas in Update().
        bool clicked = input.Clicked;
        bool escaped = input.Pressed.Contains(Key.Escape);

        // Zoomed out the whole voyage runs in fast-forward: one real second
        // buys more game seconds, so the boat covers more ground AND the
        // clock moves on by the same factor.
        float timeScale = GameTime.TimeScaleForZoom(active.State.Zoom);
        float gameDt = dt * timeScale;

        // The boat is drivable in every mode that follows it; in free mode
        // WASD belongs to the camera instead.
        if (active != freeCamera)
        {
            boat.MaxSpeedScale = Crew.SpeedMultiplier(player.CrewCount, Config.Crew);
            // Sub-stepped so a big time scale doesn't cut corners on turns.
            float remaining = gameDt;
            while (remaining > 0)
            {
                float step = Math.Min(remaining, Config.BoatMaxStepSeconds);
                boat.Update(step, input);
                remaining -= step;
            }
            // Being under way is what burns the day.
            if (boat.Speed != 0) time.AdvanceHours(Config.HoursPerSecondSailing * gameDt);
        }

        active.Update(dt, input, view);
        var state = active.State;
        CameraRig.Clamp(state, view, map.WorldW, map.WorldH);
        rig.Apply(state, view);
        terrain.SetGridOpacity(TerrainPlane.GridOpacityFor(state.Zoom));

        // Stream terrain chunks for the ground area the camera can see: the
        // four screen corners cast onto the ground, capped so a shallow view
        // doesn't ask for the whole world.
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (var corner in new[] { new Vector2(0, 0), new Vector2(view.X, 0), new Vector2(0, view.Y), view })
        {
            var g = rig.ScreenToGround(corner, Config.MaxViewDistance);
            minX = Math.Min(minX, g.X); maxX = Math.Max(maxX, g.X);
            minY = Math.Min(minY, g.Y); maxY = Math.Max(maxY, g.Y);
        }
        chunks.Update(new Rect2(minX, minY, maxX - minX, maxY - minY), state.CenterX, state.CenterY);

        // City labels: hit-testing is ours, recomputed after the camera settled.
        var hoveredCity = cityLabels.Update(state, view, input.CursorPx, rig);
        Input.SetDefaultCursorShape(hoveredCity != null ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        if (clicked)
        {
            if (hoveredCity != null) cityPopup.Show(hoveredCity);
            else cityPopup.Hide();
        }
        if (escaped) cityPopup.Hide();
        cityPopup.Update(view);

        // Crew upkeep runs on game time. An open market panel has to be told,
        // or it shows a stale hold and purse.
        int charged = rations.Update() + wages.Update();
        if (charged > 0 && cityPopup.IsOpen) cityPopup.Refresh();
        clock.Update();
        minimap.Update(state, view, rig);
        hud.Update(state, view, rig, CameraRig.MinZoomFor(view, map.WorldW, map.WorldH), active.Name, timeScale, chunks.GetStats());

        frameCount++;
        if (screenshotPath != null && frameCount >= screenshotFrames)
        {
            var img = GetViewport().GetTexture().GetImage();
            var err = img.SavePng(screenshotPath);
            GD.Print($"screenshot {(err == Error.Ok ? "saved to" : "FAILED " + err + " for")} {screenshotPath} at frame {frameCount}");
            screenshotPath = null;
            GetTree().Quit();
        }
    }
}
