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

    // True from the moment the player's battery fires until the next
    // Update — Main's combat-camera logic keys off it.
    public bool PlayerFiredThisFrame { get; private set; }

    public Vector3 AimPoint { get; private set; }
    public bool AimValid { get; private set; }
    public bool AimReady { get; private set; }   // a loaded cannon can bear on AimPoint

    private readonly List<ICombatTarget> targets = new();
    private Terrain terrain = null!;
    private MeshInstance3D reticle = null!;
    private StandardMaterial3D reticleMaterial = null!;

    // `rng` seeds the shot scatter: pass one derived from the world seed and
    // a --seed run repeats down to which way each ball strayed.
    public void Init(Terrain terrain, System.Random? rng = null)
    {
        this.terrain = terrain;
        Name = "Combat";
        Projectiles.Init(terrain, Effects, rng);
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

    // One gun: the loaded one that bears most squarely. This is how an AI
    // boat shoots — she fires as she comes to bear, not in salvoes.
    public bool Fire(CannonBattery battery, Vector3 target)
    {
        var c = battery.BestCannon(target);
        if (c == null) return false;
        Projectiles.Fire(c, target);
        if (battery == PlayerGuns) PlayerFiredThisFrame = true;
        return true;
    }

    // A broadside: every loaded gun that bears on the point and can reach it
    // fires together, so a hull lying beam-on to the target empties that side
    // at once — one on the small boat, three on the medium, six on the large.
    // Each ball scatters on its own (Projectiles), so the salvo straddles.
    // Returns how many went off.
    public int FireBroadside(CannonBattery battery, Vector3 target)
    {
        int fired = 0;
        foreach (var c in battery.CannonsBearing(target))
        {
            if (!Projectiles.WithinRange(c.GlobalPosition, target)) continue;
            Projectiles.Fire(c, target);
            fired++;
        }
        if (fired > 0 && battery == PlayerGuns) PlayerFiredThisFrame = true;
        return fired;
    }

    // Fire the player's broadside at a world (x, y) ground point. Used by the
    // --fire test flag; the mouse path goes through Update.
    public bool FireAtGround(float x, float y) =>
        PlayerGuns != null && FireBroadside(PlayerGuns, new Vector3(x, terrain.HeightAt(x, y), y)) > 0;

    public void Update(float dt, CameraRig rig, CameraState state, Vector2? cursorPx, bool clicked)
    {
        PlayerFiredThisFrame = false;
        PlayerGuns?.Tick(dt);
        Projectiles.Update(dt, targets);
        Effects.Update(dt);

        // Aim under the cursor.
        var cam = rig.Camera;
        var hit = Vector3.Zero;
        AimValid = cursorPx is Vector2 c &&
            terrain.RaycastGround(cam.ProjectRayOrigin(c), cam.ProjectRayNormal(c), Config.MaxViewDistance, out hit);
        if (AimValid) AimPoint = hit;
        // Ready = a loaded cannon bears on the point AND the point is inside
        // the fixed muzzle speed's physical reach from that cannon.
        var aimCannon = AimValid && PlayerGuns != null ? PlayerGuns.BestCannon(AimPoint) : null;
        AimReady = aimCannon != null && Projectiles.WithinRange(aimCannon.GlobalPosition, AimPoint);

        reticle.Visible = AimValid;
        if (AimValid)
        {
            reticle.Position = AimPoint + new Vector3(0, 0.05f, 0);
            reticle.Scale = Vector3.One * (CombatConfig.ReticlePx / state.Zoom);
            reticleMaterial.AlbedoColor = AimReady ? CombatConfig.ReticleReadyColor : CombatConfig.ReticleBlockedColor;
        }

        if (clicked && AimReady && PlayerGuns != null) FireBroadside(PlayerGuns, AimPoint);
    }
}
