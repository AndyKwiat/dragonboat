using Godot;
using System.Collections.Generic;

// The combat layer: targets, projectiles, effects and the player's aim.
// Main creates one, arms vessels with `Arm`, registers hittable things with
// `AddTarget`, and calls Update once per frame with the (game-time) dt.
//
// Player firing: the cursor is cast onto the ground (water plane or column
// top), a reticle marks the spot, and a click fires the loaded cannon that
// bears most squarely on it (CannonBattery.BestCannon). Enemy vessels will
// drive their own batteries the same way — `Fire(battery, target)` is all
// they need.
public partial class CombatSystem : Node3D
{
    public readonly Effects Effects = new();
    public readonly Projectiles Projectiles = new();
    public CannonBattery? PlayerGuns;

    public Vector3 AimPoint { get; private set; }
    public bool AimValid { get; private set; }
    public bool AimReady { get; private set; }   // a loaded cannon can bear on AimPoint

    private readonly List<ICombatTarget> targets = new();
    private Terrain terrain = null!;
    private MeshInstance3D reticle = null!;
    private StandardMaterial3D reticleMaterial = null!;

    public void Init(Terrain terrain)
    {
        this.terrain = terrain;
        Name = "Combat";
        Projectiles.Init(terrain, Effects);
        AddChild(Effects);
        AddChild(Projectiles);
        reticleMaterial = Effects.UnshadedMaterial(CombatConfig.ReticleReadyColor, transparent: false);
        reticle = new MeshInstance3D { Mesh = Effects.RingMesh(), MaterialOverride = reticleMaterial, Visible = false };
        AddChild(reticle);
    }

    // Mount a battery on a vessel. Returns it so callers (AI) can keep a handle.
    public CannonBattery Arm(Node3D vessel, IEnumerable<CombatConfig.Mount> mounts, float vesselLength)
    {
        var battery = new CannonBattery(mounts, vesselLength);
        vessel.AddChild(battery);
        return battery;
    }

    public void AddTarget(ICombatTarget t) => targets.Add(t);
    public void RemoveTarget(ICombatTarget t) => targets.Remove(t);

    public bool Fire(CannonBattery battery, Vector3 target)
    {
        var c = battery.BestCannon(target);
        if (c == null) return false;
        Projectiles.Fire(c, target);
        return true;
    }

    // Fire the player's best cannon at a world (x, y) ground point. Used by
    // the --fire test flag; the mouse path goes through Update.
    public bool FireAtGround(float x, float y) =>
        PlayerGuns != null && Fire(PlayerGuns, new Vector3(x, terrain.HeightAt(x, y), y));

    public void Update(float dt, CameraRig rig, CameraState state, Vector2? cursorPx, bool clicked)
    {
        PlayerGuns?.Tick(dt);
        Projectiles.Update(dt, targets);
        Effects.Update(dt);

        // Aim under the cursor.
        var cam = rig.Camera;
        var hit = Vector3.Zero;
        AimValid = cursorPx is Vector2 c &&
            terrain.RaycastGround(cam.ProjectRayOrigin(c), cam.ProjectRayNormal(c), Config.MaxViewDistance, out hit);
        if (AimValid)
        {
            AimPoint = hit;
            if (CombatConfig.MaxRange > 0 && PlayerGuns != null)
            {
                var d = hit - PlayerGuns.Vessel.GlobalPosition; d.Y = 0;
                if (d.Length() > CombatConfig.MaxRange) AimValid = false;
            }
        }
        AimReady = AimValid && PlayerGuns != null && PlayerGuns.BestCannon(AimPoint) != null;

        reticle.Visible = AimValid;
        if (AimValid)
        {
            reticle.Position = AimPoint + new Vector3(0, 0.05f, 0);
            reticle.Scale = Vector3.One * (CombatConfig.ReticlePx / state.Zoom);
            reticleMaterial.AlbedoColor = AimReady ? CombatConfig.ReticleReadyColor : CombatConfig.ReticleBlockedColor;
        }

        if (clicked && AimReady && PlayerGuns != null) Fire(PlayerGuns, AimPoint);
    }
}
