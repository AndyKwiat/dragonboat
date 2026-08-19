using Godot;
using System;

// One gun: a small box on a vessel, facing along its own local +X. It knows
// its bearing, whether it is loaded, and how far off its line a target is.
// The battery (CannonBattery.cs) owns the list and picks which one fires.
public partial class Cannon : MeshInstance3D
{
    public float Reload;          // seconds until loaded again; 0 = ready
    public float BearingDeg;      // relative to the vessel's bow, +90 = starboard
    public bool Loaded => Reload <= 0;

    private StandardMaterial3D material = null!;
    private bool showingReload;

    public Cannon(CombatConfig.Mount mount, float scale)
    {
        BearingDeg = mount.BearingDeg;
        Mesh = new BoxMesh { Size = CombatConfig.CannonSize * scale };
        material = new StandardMaterial3D { AlbedoColor = CombatConfig.CannonColor };
        MaterialOverride = material;
        // Vessel frame: forward +X, up +Y, starboard +Z. A rotation about +Y
        // turns +X towards -Z, so a starboard bearing is a negative yaw.
        Position = new Vector3(mount.Forward, mount.Height, mount.Right) * scale;
        Rotation = new Vector3(0, -Mathf.DegToRad(mount.BearingDeg), 0);
    }

    // Where the gun points on the ground plane (unit vector, Godot XZ).
    public Vector3 FlatFacing()
    {
        var f = GlobalTransform.Basis.X;
        f.Y = 0;
        return f.LengthSquared() > 1e-8f ? f.Normalized() : Vector3.Right;
    }

    // Angle (radians) between the gun's facing and the direction to target, flat.
    public float OffAngle(Vector3 target)
    {
        var to = target - GlobalPosition;
        to.Y = 0;
        if (to.LengthSquared() < 1e-8f) return 0;
        return MathF.Acos(Math.Clamp(FlatFacing().Dot(to.Normalized()), -1, 1));
    }

    public bool CanBear(Vector3 target) => OffAngle(target) <= Mathf.DegToRad(CombatConfig.FiringArcDeg) / 2;

    public void Tick(float dt)
    {
        Reload = Math.Max(0, Reload - dt);
        bool reloading = Reload > 0;
        if (reloading != showingReload)
        {
            showingReload = reloading;
            material.AlbedoColor = reloading ? CombatConfig.CannonReloadingColor : CombatConfig.CannonColor;
        }
    }
}
