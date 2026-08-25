using Godot;
using System;
using System.Collections.Generic;

// Enemy ships: stationary hostile boats scattered on random water cells at
// launch. Hull, hit box and guns are exactly the player's (BoatConfig
// dimensions, CombatConfig.PlayerMounts, the shared CombatConfig knobs — a
// cannon tweak on the T panel retunes every ship in the world, player and
// enemy alike). They never move or turn; the whole AI is "click on the
// player's boat as fast as the reloads allow": each update the best loaded
// cannon that bears and has the range fires, exactly like a player click on
// the boat's position. Cannons that can't bear simply stay silent.
//
// Logic gating: a ship only updates (reloads, fires) while it sits within
// UpdateRangeFactor × the cannons' physical range of the player boat, or
// while the world is paused (dt = 0, e.g. the city menu). That range check
// is the whole budget — everything else is one distance test per ship.
//
// Sinking one pays KillHonour and the wreck respawns fresh somewhere else
// off screen — the fleet never shrinks below Count.
public partial class EnemyShip : Node3D, ICombatTarget
{
    public float X, Y, Heading;
    public int HitsTaken;
    public CannonBattery Guns = null!;
    public bool Sunk { get; private set; }
    // Set by the manager: called the moment the ship goes down (honour).
    public Action OnSunk = () => { };

    private CombatSystem combat = null!;
    private BoatModel model = null!;
    private BlobShadow shadow = null!;
    private float hitFlash;

    public Node3D TargetNode => this;

    public void Init(float x, float y, float heading, CombatSystem combat)
    {
        this.combat = combat;
        X = x; Y = y; Heading = heading;
        Position = CameraRig.WorldToGodot(X, Y, 0);
        Rotation = new Vector3(0, -Heading, 0);
        model = new BoatModel(BoatConfig.Length, EnemyShips.HullColor);
        AddChild(model);
        Guns = combat.Arm(this, CombatConfig.PlayerMounts, BoatConfig.Length);
        combat.AddTarget(this);
    }

    public override void _Ready()
    {
        // Flat on the water like the boat's; stationary, so placed per spawn.
        shadow = new BlobShadow(BoatConfig.ShadowStrength);
        GetParent().AddChild(shadow);
        PlaceShadow();
    }

    private void PlaceShadow()
    {
        shadow.Position = new Vector3(X, 0.03f, Y);
        shadow.Rotation = new Vector3(0, -Heading, 0);
        shadow.Scale = new Vector3(BoatConfig.Length / 2, 1, BoatConfig.Width / 2) * BoatConfig.ShadowScale;
    }

    // Back from the deep: fresh hull at a new spot (the manager picks it).
    public void Respawn(float x, float y, float heading)
    {
        X = x; Y = y; Heading = heading;
        HitsTaken = 0;
        Sunk = false;
        Position = CameraRig.WorldToGodot(X, Y, 0);
        Rotation = new Vector3(0, -Heading, 0);
        PlaceShadow();
        Visible = true;
        shadow.Visible = true;
        combat.AddTarget(this);
    }

    public void Tick(float dt, Vector3 target)
    {
        Guns.Tick(dt);
        if (hitFlash > 0)
        {
            hitFlash = Math.Max(0, hitFlash - dt);
            model.SetHitFlash(CombatConfig.HullFlashSec > 0 ? hitFlash / CombatConfig.HullFlashSec : 0, CombatConfig.HullHitColor);
        }
        // The player click, automated: best loaded cannon that bears on the
        // boat and can physically reach it. No cannon? Wait reloaded/lucky.
        var c = Guns.BestCannon(target);
        if (c != null && Projectiles.WithinRange(c.GlobalPosition, target))
            combat.Projectiles.Fire(c, target);
    }

    // ICombatTarget: the same hull box + margin as the player boat.
    public bool ContainsPoint(Vector3 p)
    {
        var l = ToLocal(p);
        float m = CombatConfig.HitMargin;
        return Math.Abs(l.X) <= BoatConfig.Length / 2 + m
            && l.Y >= -m && l.Y <= BoatConfig.Height + m
            && Math.Abs(l.Z) <= BoatConfig.Width / 2 + m;
    }

    public void TakeHit(Vector3 p)
    {
        HitsTaken++;
        hitFlash = CombatConfig.HullFlashSec;
        model.SetHitFlash(1, CombatConfig.HullHitColor);
        if (HitsTaken < EnemyShips.HitsToSink) return;
        // Sunk: a splash where she went down, then hidden until the manager
        // respawns her somewhere else off screen.
        Sunk = true;
        combat.RemoveTarget(this);
        combat.Effects.SpawnRing(new Vector3(X, 0.05f, Y), CombatConfig.SplashColor,
            CombatConfig.SplashLife * 2, BoatConfig.Length);
        Visible = false;
        shadow.Visible = false;
        OnSunk();
    }
}

// Spawns the fleet on random water and gates the per-frame logic.
public partial class EnemyShips : Node3D
{
    public const int Count = 20;
    public const int HitsToSink = 3;
    public const int KillHonour = 50;   // paid the moment a ship goes down
    // A ship's logic runs only within this factor of the cannons' physical
    // range (MuzzleSpeed²/Gravity) of the player boat.
    public const float UpdateRangeFactor = 1.5f;
    // Nobody spawns closer to the player's start than this.
    public const float MinSpawnDistance = 400f;
    // Ships keep out of the open ocean (the big water in the south) but are
    // free to sit well off the beach: a spawn cell must have land somewhere
    // within this many cells, which covers rivers and coastal water and
    // excludes the deep sea. Sampled every LandSampleStep cells — the land
    // masses are far bigger than the step.
    public const int MaxLandDistanceCells = 250;
    public const int LandSampleStep = 8;
    public static readonly Color HullColor = new Color("#8a2626");

    private readonly List<EnemyShip> ships = new();

    private TerrainMap map = null!;
    private Terrain terrain = null!;
    private Random rng = null!;
    private Boat player = null!;

    public void Init(TerrainMap map, Terrain terrain, CombatSystem combat, Boat player, Random rng, Action onKill)
    {
        Name = "EnemyShips";
        this.map = map; this.terrain = terrain; this.rng = rng; this.player = player;
        while (ships.Count < Count)
        {
            if (FindSpawn(new Rect2()) is not (float x, float y)) break;
            var ship = new EnemyShip { OnSunk = onKill };
            AddChild(ship);
            ship.Init(x, y, (float)(rng.NextDouble() * Math.Tau), combat);
            ships.Add(ship);
        }
    }

    // A valid spawn point: water with land nearby, clear of the player's
    // start-distance bubble, and outside `avoid` (the current view) — the
    // view constraint is dropped if it can't be satisfied (whole map on
    // screen). Null only if the map has no valid water at all.
    private (float, float)? FindSpawn(Rect2 avoid)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int tries = 0; tries < 400; tries++)
            {
                float x = (float)rng.NextDouble() * map.WorldW;
                float y = (float)rng.NextDouble() * map.WorldH;
                if (!terrain.IsWater(x, y) || !NearLand(map, x, y)) continue;
                float dx = x - player.X, dy = y - player.Y;
                if (dx * dx + dy * dy < MinSpawnDistance * MinSpawnDistance) continue;
                if (pass == 0 && avoid.HasPoint(new Vector2(x, y))) continue;
                return (x, y);
            }
        }
        return null;
    }

    // Any live ship within dist of (x, y)? Drives the combat camera.
    public bool AnyWithin(float x, float y, float dist)
    {
        foreach (var s in ships)
        {
            if (s.Sunk) continue;
            float dx = s.X - x, dy = s.Y - y;
            if (dx * dx + dy * dy <= dist * dist) return true;
        }
        return false;
    }

    // Any land cell within MaxLandDistanceCells of the cell under (x, y)?
    private static bool NearLand(TerrainMap map, float x, float y)
    {
        int cx = (int)x, cy = (int)y;
        int r = MaxLandDistanceCells;
        for (int j = -r; j <= r; j += LandSampleStep)
            for (int i = -r; i <= r; i += LandSampleStep)
                if (!Terrain.IsWaterIndex(map.TerrainAt(cx + i, cy + j))) return true;
        return false;
    }

    // dt is game time and 0 while the world is paused; view is the ground
    // rect the camera can see (respawns land outside it).
    public void Update(float dt, Boat player, Rect2 view)
    {
        // Wrecks respawn even while the fight logic is gated — off screen,
        // so the player never sees the ship pop in.
        foreach (var s in ships)
            if (s.Sunk && FindSpawn(view) is (float x, float y))
                s.Respawn(x, y, (float)(rng.NextDouble() * Math.Tau));

        if (dt <= 0) return;
        float range = CombatConfig.MuzzleSpeed * CombatConfig.MuzzleSpeed / CombatConfig.Gravity;
        float gate = range * UpdateRangeFactor;
        var target = new Vector3(player.X, 0, player.Y);
        foreach (var s in ships)
        {
            float dx = s.X - player.X, dy = s.Y - player.Y;
            if (dx * dx + dy * dy > gate * gate) continue;
            s.Tick(dt, target);
        }
    }
}
