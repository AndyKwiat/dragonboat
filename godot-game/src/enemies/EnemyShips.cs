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
// UpdateRangeFactor × the cannons' physical range of the player boat, and
// not at all while the terrain is drawn past the first LOD (zoomed far
// out) or the world is paused (dt = 0, e.g. the city menu).
public partial class EnemyShip : Node3D, ICombatTarget
{
    public float X, Y, Heading;
    public int HitsTaken;
    public CannonBattery Guns = null!;
    public bool Sunk { get; private set; }

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
        // Flat on the water like the boat's; stationary, so placed once.
        shadow = new BlobShadow(BoatConfig.ShadowStrength);
        GetParent().AddChild(shadow);
        shadow.Position = new Vector3(X, 0.03f, Y);
        shadow.Rotation = new Vector3(0, -Heading, 0);
        shadow.Scale = new Vector3(BoatConfig.Length / 2, 1, BoatConfig.Width / 2) * BoatConfig.ShadowScale;
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
        // Sunk: a splash where she went down, then gone.
        Sunk = true;
        combat.RemoveTarget(this);
        combat.Effects.SpawnRing(new Vector3(X, 0.05f, Y), CombatConfig.SplashColor,
            CombatConfig.SplashLife * 2, BoatConfig.Length);
        shadow.QueueFree();
        QueueFree();
    }
}

// Spawns the fleet on random water and gates the per-frame logic.
public partial class EnemyShips : Node3D
{
    public const int Count = 50;
    public const int HitsToSink = 3;
    // A ship's logic runs only within this factor of the cannons' physical
    // range (MuzzleSpeed²/Gravity) of the player boat.
    public const float UpdateRangeFactor = 1.5f;
    // Nobody spawns closer to the player's start than this.
    public const float MinSpawnDistance = 400f;
    // Ships lurk near shore: a spawn cell must have land within this many
    // cells, which keeps them in rivers, canals and coastal water rather
    // than the open ocean.
    public const int MaxLandDistanceCells = 1;
    public static readonly Color HullColor = new Color("#8a2626");

    private readonly List<EnemyShip> ships = new();

    public void Init(DMap map, Terrain terrain, CombatSystem combat, Boat player, Random rng)
    {
        Name = "EnemyShips";
        int tries = 0;
        while (ships.Count < Count && tries++ < Count * 200)
        {
            float x = (float)rng.NextDouble() * map.WorldW;
            float y = (float)rng.NextDouble() * map.WorldH;
            if (!terrain.IsWater(x, y) || !NearLand(map, x, y)) continue;
            float dx = x - player.X, dy = y - player.Y;
            if (dx * dx + dy * dy < MinSpawnDistance * MinSpawnDistance) continue;
            var ship = new EnemyShip();
            AddChild(ship);
            ship.Init(x, y, (float)(rng.NextDouble() * Math.Tau), combat);
            ships.Add(ship);
        }
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
    private static bool NearLand(DMap map, float x, float y)
    {
        int cx = (int)(x / Config.TileScale), cy = (int)(y / Config.TileScale);
        int r = MaxLandDistanceCells;
        for (int j = -r; j <= r; j++)
            for (int i = -r; i <= r; i++)
                if (!Terrain.IsWaterIndex(map.TerrainAt(cx + i, cy + j))) return true;
        return false;
    }

    // dt is game time and 0 while the world is paused; lodStep is the
    // terrain streamer's current LOD (1 = full detail).
    public void Update(float dt, int lodStep, Boat player)
    {
        if (dt <= 0 || lodStep > 1) return;
        float range = CombatConfig.MuzzleSpeed * CombatConfig.MuzzleSpeed / CombatConfig.Gravity;
        float gate = range * UpdateRangeFactor;
        var target = new Vector3(player.X, 0, player.Y);
        ships.RemoveAll(s => s.Sunk);
        foreach (var s in ships)
        {
            float dx = s.X - player.X, dy = s.Y - player.Y;
            if (dx * dx + dy * dy > gate * gate) continue;
            s.Tick(dt, target);
        }
    }
}
