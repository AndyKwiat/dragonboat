using Godot;
using System;
using System.Collections.Generic;

// Wiring: load map → world (terrain plane + chunks + light) → boat → combat
// (guns, targets) → camera rigs and modes → UI overlays → per-frame loop
// (mode/rig/panel toggles → boat → camera → clamp → apply → chunks → labels
// → combat (aim/fire) → popup → upkeep → clock/minimap/hud).
//
// Subsystems live in their own folders (world/, boat/, combat/, trading/, ui/)
// with a doc each under docs/; this file is the only place they all meet.
//
// Test hooks (user args after `--`): see ParseArgs. Example:
//   Godot --path . -- --screenshot=shot.png --frames=90 --zoom=0.5 --rig=perspective
public partial class Main : Node3D
{
    private TerrainMap map = null!;
    private TerrainPlane terrain = null!;
    private TerrainChunks chunks = null!;
    private Terrain terrainQuery = null!;
    private Boat boat = null!;
    private CombatSystem combat = null!;
    private EnemyShips enemies = null!;
    private InputState input = null!;
    private CameraRig rig = null!;
    private readonly Dictionary<string, CameraRig> rigs = new();
    private FreeCameraController freeCamera = null!;
    private FollowCameraController followCamera = null!;
    private EdgeCameraController edgeCamera = null!;
    private readonly Dictionary<string, ICameraController> controllers = new();
    private static readonly string[] ModeCycle = { "follow", "edge", "free" };
    private ICameraController active = null!;

    private Player player = null!;
    private GameTime time = null!;
    private Rations rations = null!;
    private Wages wages = null!;
    private StoryEvents storyEvents = null!;
    private Loans loans = null!;
    private Monasteries religion = null!;
    private Hud hud = null!;
    private StaminaBar staminaBar = null!;
    private WindDial windDial = null!;
    private Clock clock = null!;
    private Wind wind = null!;
    private CrewStamina crewStamina = null!;
    private Minimap minimap = null!;
    private CityLabels cityLabels = null!;
    private VectorOverlay vectors = null!;
    private CityPopup cityPopup = null!;
    private BankPopup bankPopup = null!;
    private MonasteryPopup monasteryPopup = null!;
    private EventLog eventLog = null!;
    private TweakPanel tweakPanel = null!;

    private Docking docking = null!;   // auto-docking: hull against a city opens its market
    // What every product fetches across the world, re-fixed each midnight;
    // the market panel and the map labels quote prices against it.
    private WorldPrices worldPrices = null!;
    private readonly DayTurn midnight = new();   // VIP passengers' daily pass
    private int lastRoster = int.MinValue;       // passengers/errands aboard, for the map tags
    private float fastForwardSec;   // how long Shift has been held, for the fast-forward ramp
    private bool combatCam;         // the edge cam was entered automatically by combat
    // Set when _Ready gave up on the world (see the producer check): Quit()
    // only lands at the end of the frame, so _Process must not run meanwhile.
    private bool fatal;

    // Test hooks.
    private string? screenshotPath;
    private int screenshotFrames = 90;
    private int frameCount;
    private readonly List<Key> heldKeys = new();
    private City? startCity;        // the city the boat starts off (--city with no name)
    private Vector2? fireOffset;                 // --fire=dx,dy: aim point relative to the boat
    private readonly List<int> fireFrames = new();
    private readonly List<(Key key, int frame)> pressFrames = new();   // --press=Key@frame

    private Vector2 View => GetViewport().GetVisibleRect().Size;

    public override void _Ready()
    {
        var args = ParseArgs();

        // Hi-dpi: render at the display's native density but keep every
        // coordinate in logical px (the web version's CSS px). The content
        // scale factor is the devicePixelRatio equivalent; the window is
        // enlarged to match so the logical size stays what project.godot asks
        // for. Fonts rasterise at the scaled size, so small text stays crisp.
        float screenScale = (float)DisplayServer.ScreenGetScale();
        if (screenScale > 1f) GetWindow().ContentScaleFactor = screenScale;
        // A human launch (./run.sh with no window flags) fills the screen,
        // windowed, at the top-left corner. A scripted run keeps the small
        // project.godot window (or whatever --size asks for) so screenshots
        // stay a predictable size.
        if (!args.ContainsKey("screenshot") && !args.ContainsKey("size"))
        {
            var usable = DisplayServer.ScreenGetUsableRect();
            DisplayServer.WindowSetSize(usable.Size);
            DisplayServer.WindowSetPosition(usable.Position);
        }
        else if (screenScale > 1f)
        {
            DisplayServer.WindowSetSize((Vector2I)((Vector2)DisplayServer.WindowGetSize() * screenScale));
        }
        Tweaks.Register(typeof(BoatConfig), typeof(CombatConfig), typeof(Currents));
        // Tweaks persist: the panel's dump button writes tweaks.dump, and it
        // is applied over the code defaults at every launch (--set on top of
        // that; delete the file to get the defaults back).
        if (FileAccess.FileExists(TweakPanel.DumpPath))
            foreach (var line in FileAccess.GetFileAsString(TweakPanel.DumpPath).Split('\n'))
                if (!Tweaks.ApplyDumpedLine(line))
                    GD.PushWarning($"tweaks.dump: could not apply '{line.Trim()}'");
        // game-config.yml picks the terrain file and the city count; the
        // cities are placed fresh on the coast every launch.
        GameConfig.Current = GameConfig.Parse(FileAccess.GetFileAsString(GameConfig.Path));
        GameData.Load();
        map = TerrainMap.Parse(FileAccess.GetFileAsBytes("res://" + GameConfig.Current.TerrainFile));
        // --seed makes a run reproducible: same cities, same enemy fleet.
        var worldRng = args.TryGetValue("seed", out var seed) ? new Random(int.Parse(seed)) : new Random();
        var cities = CityPlacer.Place(map, CityPlacer.SizesFor(GameConfig.Current),
            CityPlacer.ParseNames(FileAccess.GetFileAsString(Config.CityNamesPath)), worldRng);
        // Production is dealt and distance priced here. A world that cannot
        // support an economy — a good too few cities make, or a market left
        // inconsistent — is fatal: say so loudly rather than sail into it.
        var problems = GameData.SetCities(cities, worldRng, map.Width);
        if (problems.Count > 0) { Die("this world has no economy to trade in", problems); return; }
        worldPrices = new WorldPrices(GameData.Cities, GameData.Products, Config.Market);
        terrainQuery = new Terrain(map);

        // The world: terrain plane (ground/water) + streamed raised chunks + boat.
        terrain = new TerrainPlane();
        terrain.Build(map);
        AddChild(terrain);
        chunks = new TerrainChunks();
        chunks.Init(map);
        AddChild(chunks);
        var currents = new Currents();
        currents.Init(map, terrainQuery);
        AddChild(currents);
        boat = new Boat();
        PlaceBoatStart(cities, worldRng);
        boat.Init(terrainQuery);
        boat.Currents = currents;
        // Sailing: the world's wind (only felt with the sails up) and the
        // rowers' stamina (only spent with them down) — boat/Sailing.cs.
        wind = new Wind(BoatConfig.WindBearingDeg, BoatConfig.WindForce,
                        BoatConfig.WindSwingDeg, BoatConfig.WindSwingHours);
        boat.Wind = wind;
        crewStamina = new CrewStamina(BoatConfig.RowHoursToEmpty, BoatConfig.RestHoursToFull);
        AddChild(boat);
        // The terrain is unshaded, so this light only touches the boats,
        // guns and balls — enough to make the model read.
        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            Rotation = new Vector3(Mathf.DegToRad(-55), Mathf.DegToRad(35), 0),
            LightEnergy = 1.2f,
        });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.82f, 0.95f),
                AmbientLightEnergy = 0.9f,
            },
        });

        // Combat: the player's guns ride on the boat; the boat is itself a
        // target so enemy fire can hit it later.
        combat = new CombatSystem();
        combat.Init(terrainQuery);
        AddChild(combat);
        combat.PlayerGuns = combat.Arm(boat, CombatConfig.PlayerMounts, BoatConfig.Length);
        combat.AddTarget(boat);

        // Enemy fleet: stationary red ships on random water, same guns and
        // tweaks as the player (see src/enemies/EnemyShips.cs).
        enemies = new EnemyShips();
        AddChild(enemies);
        enemies.Init(map, terrainQuery, combat, boat, worldRng,
            () => player.Honour += EnemyShips.KillHonour);

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
        followCamera = new FollowCameraController(boat, rig);
        edgeCamera = new EdgeCameraController(boat, rig);
        controllers["free"] = freeCamera;
        controllers["follow"] = followCamera;
        controllers["edge"] = edgeCamera;
        active = followCamera;
        Resize();
        // Start on the boat at the follow camera's own zoom.
        active.Enable(new CameraState(boat.X, boat.Y, Config.BoatFollowZoom));

        player = new Player();
        time = new GameTime();
        rations = new Rations(player, time);
        wages = new Wages(player, time);
        storyEvents = new StoryEvents(time);
        loans = new Loans(Config.Bank);
        // Monasteries are placed off the world RNG, so --seed fixes which
        // small cities have one (and the errands they start with).
        religion = new Monasteries(GameData.Cities, Config.Religion, worldRng);
        var monasteryNames = new List<string>();
        foreach (var h in religion.Houses) monasteryNames.Add($"{h.City.Name} ({h.City.X:0},{h.City.Y:0})");
        GD.Print($"Monasteries in {string.Join(", ", monasteryNames)}");
        docking = new Docking(Config.CityDockCooldownSec);

        // UI overlays, in draw order (labels under the panels, popup on top).
        var ui = new CanvasLayer();
        AddChild(ui);
        cityLabels = new CityLabels();
        cityLabels.Init(GameData.Cities, storyEvents, worldPrices, time, loans, religion, player);
        ui.AddChild(cityLabels);
        vectors = new VectorOverlay();
        vectors.Init(boat);
        ui.AddChild(vectors);
        hud = new Hud();
        hud.Init(map.WorldW, map.WorldH);
        ui.AddChild(hud);
        clock = new Clock();
        clock.Init(time, rations, player, loans, religion);
        ui.AddChild(clock);
        staminaBar = new StaminaBar();
        staminaBar.Init(crewStamina);
        ui.AddChild(staminaBar);
        windDial = new WindDial();
        windDial.Init(wind);
        ui.AddChild(windDial);
        minimap = new Minimap();
        minimap.Init(map, terrain.Texture);
        ui.AddChild(minimap);
        cityPopup = new CityPopup();
        cityPopup.Init(player, time, storyEvents, worldPrices, loans, religion);
        ui.AddChild(cityPopup);
        // The bank window sits on top of the market panel it was opened from;
        // the event log is the world talking back while you sail (banking.md).
        bankPopup = new BankPopup();
        bankPopup.Init(player, time, loans);
        bankPopup.Announce = text => eventLog.Add(text);
        bankPopup.Changed = () => { cityPopup.Refresh(); cityLabels.RefreshIntel(); };
        cityPopup.OpenBank = city => bankPopup.Show(city);
        ui.AddChild(bankPopup);
        monasteryPopup = new MonasteryPopup();
        monasteryPopup.Init(player, time, religion);
        monasteryPopup.Announce = text => eventLog.Add(text);
        monasteryPopup.Changed = () => { cityPopup.Refresh(); cityLabels.RefreshIntel(); };
        cityPopup.OpenMonastery = city => monasteryPopup.Show(city);
        ui.AddChild(monasteryPopup);
        eventLog = new EventLog();
        eventLog.Init();
        ui.AddChild(eventLog);
        tweakPanel = new TweakPanel();
        tweakPanel.Init(topPx: 130);   // under the clock
        ui.AddChild(tweakPanel);

        GetTree().Root.SizeChanged += Resize;
        ApplyArgs(args);
    }

    // The boat starts on open water off a random large city's shore,
    // pointing out to sea (any city if no large one has a usable shore).
    private void PlaceBoatStart(List<City> cities, Random rng)
    {
        // A random large city first, then the rest as fallbacks.
        var large = cities.FindAll(c => c.Size == "large");
        var order = new List<City>();
        if (large.Count > 0) order.Add(large[rng.Next(large.Count)]);
        foreach (var c in cities) if (!order.Contains(c)) order.Add(c);
        foreach (var c in order)
        {
            if (CityPlacer.WaterStartNear(map, c, Config.BoatStartClearanceCells) is not (float x, float y, float heading)) continue;
            boat.X = x; boat.Y = y; boat.Heading = heading;
            startCity = c;
            GD.Print($"Boat starts off {c.Name} ({c.Size}, {c.Cells} cells, centre {c.X:0},{c.Y:0}) at {x:0},{y:0}");
            return;
        }
        throw new Exception("No city has a usable shore for the boat to start on");
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

    // --seed=N             fixed world RNG: same cities and fleet every run
    // The window fills the screen unless --screenshot or --size is given.
    // --screenshot[=path]  save the viewport after --frames (default 90) and quit
    // --zoom=Z             starting zoom (px per world unit)
    // --rig=name           starting camera rig
    // --vectors            start with the velocity/force overlay on (F toggles)
    // --mode=follow|edge|free  starting camera mode
    // --center=x,y         focus point (free mode)
    // --city[=Name]        open the market of the start city (or a named one)
    // --bank               open that city's bank window over its market
    // --vip[=daysAboard]   take a VIP offer aboard, picked up that many days ago
    // --deliver            then open the destination's market, landing them
    // --monastery[=Name]   open a monastery's window over its city's market
    // --monk[=daysAboard]  take a monk aboard, accepted that many days ago
    // --alms=daysAgo       pretend that monastery took alms that many days ago
    // --honour=N           set the player's honour (bank credit is 10x it)
    // --loan=N[@late]      borrow N from that city's bank at startup, its due
    //                      date already `late` days in the past
    // --size=WxH           window size
    // --hold=W,D           keys held down for the whole run (physical key names)
    // --press=Key@frame,... one-shot key presses (Escape closes the market)
    // --start=x,y[,deg]    put the boat here (world units, heading in degrees, 0 = east)
    // --sails=up|down      start under canvas (default) or at the oars
    // --stamina=0..1       start the crew part-spent (default 1 = fresh)
    // --set=Name=V,...     override [Tweak] values by field name (see core/Tweaks.cs)
    // --dump-tweaks        write changed tweaks to tweaks.dump on startup (test hook)
    // --fire=dx,dy@f,...   fire at the point dx,dy from the boat on those frames
    // --tweaks             start with the tweak panel open
    private void ApplyArgs(Dictionary<string, string> args)
    {
        float F(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        if (args.TryGetValue("set", out var sets))
            foreach (var a in sets.Split(','))
                if (!Tweaks.Apply(a)) GD.PushWarning($"--set: unknown tweak or bad value '{a}'");
        if (args.TryGetValue("start", out var start))
        {
            var parts = start.Split(',');
            if (parts.Length >= 2) { boat.X = F(parts[0]); boat.Y = F(parts[1]); }
            if (parts.Length >= 3) boat.Heading = Mathf.DegToRad(F(parts[2]));
            boat.SyncMesh();
            active.Enable(new CameraState(boat.X, boat.Y, active.State.Zoom));
        }
        if (args.ContainsKey("vectors")) vectors.Toggle();
        if (args.TryGetValue("fire", out var fire))
        {
            var at = fire.IndexOf('@');
            var xy = (at < 0 ? fire : fire[..at]).Split(',');
            fireOffset = new Vector2(F(xy[0]), F(xy[1]));
            if (at >= 0) foreach (var f in fire[(at + 1)..].Split(',')) fireFrames.Add(int.Parse(f));
            else fireFrames.Add(10);
        }
        if (args.ContainsKey("tweaks")) tweakPanel.Toggle();
        if (args.ContainsKey("dump-tweaks")) tweakPanel.Dump();
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
        // Banking test hooks: set honour (credit is a multiple of it), take
        // a loan out, and/or open the bank window over the market.
        if (args.TryGetValue("honour", out var honour)) player.Honour = int.Parse(honour);
        var bankCity = args.TryGetValue("city", out var named) && named != "true"
            ? GameData.Cities.Find(c => c.Name == named)
            : startCity != null && Loans.HasBank(startCity) ? startCity : GameData.Cities.Find(Loans.HasBank);
        if (args.TryGetValue("loan", out var loanArg) && bankCity != null)
        {
            var parts = loanArg.Split('@');
            int today = Days.Of(time.TotalMinutes);
            var loan = loans.Borrow(bankCity, int.Parse(parts[0]), player.Honour, today);
            if (loan == null) GD.PushWarning($"--loan: {bankCity.Name} would lend nothing (honour {player.Honour})");
            else
            {
                player.Money += loan.Principal;
                // `@late`: backdate the due date so the penalties can be seen
                // without sailing a month for them.
                if (parts.Length > 1)
                {
                    loan.DueDay = today - int.Parse(parts[1]);
                    // Anchor the loan bookkeeping a day back, so the first
                    // frame runs the midnight pass and the penalties land.
                    loans.Update(today - 1, lost => player.Honour -= lost);
                }
                cityLabels.RefreshIntel();
            }
        }
        if (args.ContainsKey("bank") && bankCity != null)
        {
            cityPopup.Show(bankCity);
            cityLabels.RefreshIntel();
            bankPopup.Show(bankCity);
        }
        // --vip: take the first VIP offer in the world aboard, optionally
        // picked up some days ago, so the decaying reward, the warnings and
        // the jump-ship penalty can be seen without sailing a fortnight.
        if (args.TryGetValue("vip", out var vipArg))
        {
            int daysAboard = vipArg == "true" ? 0 : int.Parse(vipArg);
            int today = Days.Of(time.TotalMinutes);
            foreach (var e in storyEvents.All)
            {
                if (e is not VipDeliveryStoryEvent offer) continue;
                player.VipPassengers.Add(new VipPassenger { Destination = offer.Destination, PickedUpDay = today - daysAboard });
                storyEvents.Accept(offer);
                GD.Print($"VIP aboard for {offer.Destination.Name}, picked up {daysAboard} day(s) ago");
                break;
            }
            // Anchor the daily gate a day back, so the pass runs on frame 1.
            if (daysAboard > 0) midnight.Turned(today - 1);
            // Story events are rolled unseeded, so the destination is not
            // knowable in advance: --deliver opens its market for us.
            if (args.ContainsKey("deliver") && player.VipPassengers.Count > 0)
            {
                cityPopup.Show(player.VipPassengers[0].Destination);
                cityLabels.RefreshIntel();
            }
        }
        if (args.TryGetValue("city", out var cityName))
        {
            // Names are dealt fresh every run, so bare --city opens the city
            // the boat starts off; a name still works if you have one.
            var city = cityName == "true" ? startCity : GameData.Cities.Find(c => c.Name == cityName);
            if (city != null) { cityPopup.Show(city); cityLabels.RefreshIntel(); }
            else GD.PushWarning($"--city: no city named '{cityName}' this run");
        }
        // Religion test hooks: take a monk aboard (optionally accepted some
        // days back, so the warnings and the walk-away can be seen), and/or
        // open a monastery's window.
        Monastery? house = null;
        if (args.TryGetValue("monastery", out var houseName) && houseName != "true")
        {
            var named2 = GameData.Cities.Find(c => c.Name == houseName);
            house = named2 == null ? null : religion.At(named2);
            if (house == null) GD.PushWarning($"--monastery: no monastery in '{houseName}' this run");
        }
        if (house == null && religion.Houses.Count > 0) house = religion.Houses[0];
        if (args.TryGetValue("monk", out var monkArg) && house != null)
        {
            int daysAboard = monkArg == "true" ? 0 : int.Parse(monkArg);
            int today = Days.Of(time.TotalMinutes);
            religion.PlaceErrand(house);
            var monk = religion.Accept(house, today);
            if (monk == null) GD.PushWarning($"--monk: {house.City.Name} has no monk to send");
            else
            {
                monk.DueDay -= daysAboard;
                GD.Print($"Monk of {monk.From.Name} aboard for {monk.Destination.Name}, {monk.DaysLeft(today)} days left");
                // Anchor both daily gates a day back, so the pass runs on the
                // first frame and the warning or the walk-away lands at once.
                if (daysAboard > 0) { midnight.Turned(today - 1); religion.Update(today - 1, _ => { }); }
                if (args.ContainsKey("deliver")) { cityPopup.Show(monk.Destination); cityLabels.RefreshIntel(); }
            }
        }
        if (args.TryGetValue("alms", out var almsArg) && house != null)
            house.LastGiftDay = Days.Of(time.TotalMinutes) - int.Parse(almsArg);
        if (args.ContainsKey("monastery") && house != null)
        {
            cityPopup.Show(house.City);
            cityLabels.RefreshIntel();
            monasteryPopup.Show(house.City);
        }
        if (args.TryGetValue("press", out var press))
            foreach (var spec in press.Split(','))
            {
                var parts = spec.Split('@');
                pressFrames.Add((Enum.Parse<Key>(parts[0], true), parts.Length > 1 ? int.Parse(parts[1]) : 10));
            }
        if (args.TryGetValue("sails", out var sails)) boat.SetSails(sails != "down");
        if (args.TryGetValue("stamina", out var stamina)) crewStamina.Set(F(stamina));
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
        if (followCamera != null) followCamera.Rig = rig;
        if (edgeCamera != null) edgeCamera.Rig = rig;
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
        edgeCamera.MinZoom = minZoom;
        var state = active.State;
        state.Zoom = Math.Max(state.Zoom, minZoom);
        CameraRig.Clamp(state, view, map.WorldW, map.WorldH);
    }

    // The world's prices moved: re-fix the market-size-weighted world average
    // every quote is shown against, then rebuild the open market and the
    // map's price intel. The rebuild matters — a market row anchors its deal
    // on the stock level it opened at (trading/MarketDeal.cs), so a row that
    // outlived a change to that level would be pricing against a stale world.
    private void RefreshMarkets()
    {
        worldPrices.Refresh(GameData.Cities, GameData.Products, Config.Market);
        cityPopup.Rebuild();
        cityLabels.RefreshIntel();
    }

    // Give up on the run with a banner that survives a wall of Godot output.
    // Quit() only lands at the end of the frame, so `fatal` stops _Process
    // touching the half-built world in the meantime.
    private void Die(string headline, List<string> detail)
    {
        GD.PrintErr(new string('=', 72));
        GD.PrintErr($"FATAL: {headline}.");
        foreach (var line in detail) GD.PrintErr($"    {line}");
        GD.PrintErr("Raise smallCities / mediumCities / largeCities in game-config.yml.");
        GD.PrintErr(new string('=', 72));
        fatal = true;
        GetTree().Quit(1);
    }

    public override void _Process(double delta)
    {
        if (fatal) return;
        float dt = Math.Min((float)delta, 0.1f);
        var view = View;
        foreach (var k in heldKeys) input.Keys.Add(k);
        // --press: one-shot key presses on given frames (--hold only holds a
        // key down, which never counts as a press).
        foreach (var (key, frame) in pressFrames)
            if (frame == frameCount) { input.Pressed.Add(key); input.Keys.Add(key); }

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
        // T shows the live-tuning panel.
        if (input.Pressed.Contains(Key.T)) tweakPanel.Toggle();
        // F shows the boat's velocity and force vectors.
        if (input.Pressed.Contains(Key.F)) vectors.Toggle();
        // P (debug) lists every product's price under every city label.
        if (input.Pressed.Contains(Key.P)) cityLabels.ToggleDebugPrices();
        // [ / ] in that mode: one product at a time.
        if (input.Pressed.Contains(Key.Bracketleft)) cityLabels.StepDebugProduct(-1);
        if (input.Pressed.Contains(Key.Bracketright)) cityLabels.StepDebugProduct(1);
        // Latched here: the active controller clears the frame deltas in Update().
        bool clicked = input.Clicked;
        bool escaped = input.Pressed.Contains(Key.Escape);
        // While the city menu is open the world is paused for input: no
        // sailing, no camera movement or zoom, no firing. Latched before the
        // click is handled so the click that closes the menu is inert too.
        bool menuOpen = cityPopup.IsOpen || bankPopup.IsOpen || monasteryPopup.IsOpen;

        // Hold either Shift to fast-forward: one real second buys more game
        // seconds, so the boat covers more ground AND the clock moves on by
        // the same factor. The rate ramps up over FastForwardRampUpSec of
        // holding and drains back over FastForwardRampDownSec on release so
        // the return to normal time isn't jarring.
        fastForwardSec = input.Down(Key.Shift) && !menuOpen
            ? Math.Min(fastForwardSec + dt, Config.FastForwardRampUpSec)
            : Math.Max(0, fastForwardSec - dt * Config.FastForwardRampUpSec / Config.FastForwardRampDownSec);
        float timeScale = GameTime.TimeScaleForFastForward(fastForwardSec);
        float gameDt = dt * timeScale;

        // The boat is drivable in every mode that follows it; in free mode
        // WASD belongs to the camera instead.
        // SPACE strikes or sets the sails: canvas catches the wind and runs
        // faster but answers the helm lazily; oars ignore the wind, shove
        // harder off the mark, top out slower and tire the crew.
        bool driving = active != freeCamera && !menuOpen;
        if (driving && input.Pressed.Contains(Key.Space)) boat.SetSails(!boat.SailsUp);
        if (driving)
        {
            boat.ThrustScale = Crew.SpeedMultiplier(player.CrewCount, Config.Crew);
            // Fast-forward speeds the voyage, not the helm: keep the turn rate
            // constant per real second so steering feels the same at any rate.
            boat.TurnRateScale = 1 / timeScale;
            // Sub-stepped so a big time scale doesn't cut corners on turns.
            float remaining = gameDt;
            while (remaining > 0)
            {
                float step = Math.Min(remaining, BoatConfig.MaxStepSeconds);
                boat.Update(step, input);
                remaining -= step;
            }
            // Being under way is what burns the day.
            if (boat.Velocity.LengthSquared() > 0.01f) time.AdvanceHours(Config.HoursPerSecondSailing * gameDt);
        }
        else boat.Rowing = false;

        // Sailing bookkeeping on game time, so fast-forward tires the crew
        // and swings the wind at the same rate it eats the calendar. The
        // knobs are live-tunable, so re-read them each frame.
        wind.BaseBearingDeg = BoatConfig.WindBearingDeg; wind.Force = BoatConfig.WindForce;
        wind.SwingDeg = BoatConfig.WindSwingDeg; wind.SwingHours = BoatConfig.WindSwingHours;
        crewStamina.RowHoursToEmpty = BoatConfig.RowHoursToEmpty;
        crewStamina.RestHoursToFull = BoatConfig.RestHoursToFull;
        float sailHours = Config.HoursPerSecondSailing * gameDt;
        wind.Advance(sailHours);
        crewStamina.Update(sailHours, boat.Rowing);

        if (menuOpen) input.ConsumeFrameDeltas();   // swallow wheel/drag so it doesn't replay on close
        else active.Update(dt, input, view);
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
        var groundView = new Rect2(minX, minY, maxX - minX, maxY - minY);
        chunks.Update(groundView, state.CenterX, state.CenterY);

        // City labels: hit-testing is ours, recomputed after the camera settled.
        var hoveredCity = cityLabels.Update(state, view, input.CursorPx, rig);
        Input.SetDefaultCursorShape(hoveredCity != null ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        if (clicked)
        {
            // With the bank or monastery window up, a click off it closes
            // only that — the market panel underneath stays open.
            if (bankPopup.IsOpen) bankPopup.Hide();
            else if (monasteryPopup.IsOpen) monasteryPopup.Hide();
            // Opening the popup discovers the city and its live events, so
            // the map labels may have new price intel to show.
            else if (hoveredCity != null) { cityPopup.Show(hoveredCity); cityLabels.RefreshIntel(); }
            else cityPopup.Hide();
        }
        // Combat runs on game time like the boat; a click is a shot at the
        // reticle only when it didn't land on a city label (hover-highlighted
        // names open the market instead) and the menu wasn't open. With the
        // menu open the reticle is hidden too (null cursor).
        bool canAim = input.HasCursor && !menuOpen;
        combat.Update(gameDt, rig, state, canAim ? input.CursorPx : null, clicked && hoveredCity == null && !menuOpen);
        // (After combat.Update, which clears PlayerFiredThisFrame — so a
        // scripted shot is seen by the combat camera below, like a click.)
        if (fireOffset is Vector2 fo && fireFrames.Contains(frameCount))
            combat.FireAtGround(boat.X + fo.X, boat.Y + fo.Y);
        // Enemy logic pauses with the menu, out of range, or past LOD 1.
        enemies.Update(menuOpen ? 0 : gameDt, boat, groundView);

        // Combat camera: fighting reads better from the edge cam. The first
        // shot fired with an enemy in cannon range flips follow → edge, and
        // drifting out of range (with a little slack, so the boundary
        // doesn't flap) flips it back. Free cam is never touched, and any
        // manual mode change cancels the auto-return.
        float combatRange = CombatConfig.MuzzleSpeed * CombatConfig.MuzzleSpeed / CombatConfig.Gravity;
        if (active == followCamera && combat.PlayerFiredThisFrame && enemies.AnyWithin(boat.X, boat.Y, combatRange))
        {
            SwitchMode("edge");
            combatCam = true;
        }
        else if (combatCam && active == edgeCamera && !enemies.AnyWithin(boat.X, boat.Y, combatRange * 1.2f))
            SwitchMode("follow");
        if (active != edgeCamera) combatCam = false;
        // Esc closes the window on top first, then the market panel under it.
        if (escaped)
        {
            if (bankPopup.IsOpen) bankPopup.Hide();
            else if (monasteryPopup.IsOpen) monasteryPopup.Hide();
            else cityPopup.Hide();
        }

        // Auto-docking: running the hull into a city opens its market, the
        // same as clicking its label (rules in trading/Docking.cs).
        var atQuay = CityPlacer.Touching(GameData.Cities, boat.X, boat.Y, Config.CityDockContactMargin);
        if (docking.Update(dt, atQuay, cityPopup.IsOpen) is City dock)
        {
            cityPopup.Show(dock);
            cityLabels.RefreshIntel();
        }

        cityPopup.Update(view);
        bankPopup.Update(view);
        monasteryPopup.Update(view);

        // Bank loans turn over at midnight like story events: warnings as the
        // due date nears, honour lost while one runs late (src/banking). VIP
        // passengers run on the same daily beat — they lose patience and
        // eventually jump ship (src/trading/Vips.cs).
        int today = Days.Of(time.TotalMinutes);
        var news = loans.Update(today, lost => player.Honour -= lost);
        if (midnight.Turned(today))
        {
            news.AddRange(Vips.Update(player.VipPassengers, today, Config.Vips, lost => player.Honour -= lost));
            // Every market drifts a tenth of the way back to normal: the rest
            // of the coast's traders, working while you sailed. Prices and the
            // sliders' anchors move with it, so an open market is rebuilt.
            Market.DecayAll(GameData.Cities, Config.Market);
            RefreshMarkets();
        }
        // Monasteries run on the same beat: monks out of time walk away, and
        // the houses' errands come and go (src/religion).
        news.AddRange(religion.Update(today, lost => player.Piety -= lost));
        if (news.Count > 0)
        {
            eventLog.Add(news);
            cityLabels.RefreshIntel();
            if (cityPopup.IsOpen) cityPopup.Refresh();
            if (bankPopup.IsOpen) bankPopup.Refresh();
            if (monasteryPopup.IsOpen) monasteryPopup.Refresh();
        }
        // Taking a passenger or an errand aboard, or landing one, changes the
        // drop-off tags under the map labels; both happen inside an open
        // panel, so watch the roster rather than every caller telling us.
        int roster = player.VipPassengers.Count * 1000003 + religion.Version;
        if (roster != lastRoster) { lastRoster = roster; cityLabels.RefreshIntel(); }

        // Crew upkeep runs on game time. An open market panel has to be told,
        // or it shows a stale hold and purse.
        int charged = rations.Update() + wages.Update();
        if (charged > 0 && cityPopup.IsOpen) cityPopup.Refresh();
        // Story events roll over at midnight; an open market menu has prices
        // and days-left tags baked into its rows, and the map labels show
        // days-left intel, so rebuild both.
        if (storyEvents.Update()) RefreshMarkets();
        clock.Update();
        // The event log stacks above the wind dial in the same corner.
        eventLog.Update(dt, view, windDial.Size.Y + 8);
        minimap.Update(state, view, rig);
        string guns = combat.PlayerGuns == null ? "" :
            $"guns {combat.PlayerGuns.LoadedCount}/{combat.PlayerGuns.Cannons.Count} loaded, shots {combat.Projectiles.Shots}, in air {combat.Projectiles.InFlight}";
        vectors.Update(rig);
        staminaBar.Update(dt, view, boat.SailsUp);
        windDial.Update(rig, boat.X, boat.Y, view);
        string sailing = $"{(boat.SailsUp ? "sails up" : "oars")} [space]  crew {crewStamina.Level * 100:F0}%\n" +
                         $"wind {wind.Force:F0} to the {wind.Towards()} ({wind.BearingDeg:F0}°), " +
                         $"drive {boat.SailDrive * 100:F0}%\n";   // the dial (bottom left) shows the wind
        hud.Update(state, view, rig, CameraRig.MinZoomFor(view, map.WorldW, map.WorldH), active.Name, timeScale, chunks.GetStats(), guns, sailing);
        tweakPanel.Update(view);

        frameCount++;
        if (screenshotPath != null && frameCount >= screenshotFrames)
        {
            var img = GetViewport().GetTexture().GetImage();
            var err = img.SavePng(screenshotPath);
            GD.Print($"screenshot {(err == Error.Ok ? "saved to" : "FAILED " + err + " for")} {screenshotPath} at frame {frameCount}; " +
                     $"boat at ({boat.X:F1}, {boat.Y:F1}) heading {Mathf.RadToDeg(boat.Heading):F0}° speed {boat.Speed:F1}; " +
                     $"sails {(boat.SailsUp ? "up" : "down")} stamina {crewStamina.Level:F2} " +
                     $"wind {wind.Force:F0}@{wind.BearingDeg:F0}°; " +
                     $"shots {combat.Projectiles.Shots} hits {combat.Projectiles.Hits}");
            screenshotPath = null;
            GetTree().Quit();
        }
    }
}
