using Godot;
using System;
using System.Collections.Generic;

// Cannonballs in flight. Ballistic under CombatConfig.Gravity, tested each
// step against every registered target (except the vessel that fired) and
// against the ground: water → splash ring, raised land → dirt puff at the
// column top, so every shot leaves a mark where it landed.
public partial class Projectiles : Node3D
{
    public sealed class Ball
    {
        public MeshInstance3D Mesh = null!;
        public Vector3 Velocity;
        public Node3D? Owner;
    }

    public int Shots, Hits;
    public int InFlight => balls.Count;

    private readonly List<Ball> balls = new();
    private Terrain terrain = null!;
    private Effects effects = null!;
    private StandardMaterial3D ballMaterial = null!;

    public void Init(Terrain terrain, Effects effects)
    {
        this.terrain = terrain;
        this.effects = effects;
        ballMaterial = new StandardMaterial3D { AlbedoColor = CombatConfig.BallColor };
    }

    // Muzzle velocity for a fixed launch angle that lands exactly on target:
    // v² = g d² / (2 cos²a (d tan a − h)). When the target is too high for
    // the angle the denominator goes to zero and the shot just falls short.
    public static Vector3 SolveLaunch(Vector3 start, Vector3 target)
    {
        var flat = new Vector3(target.X - start.X, 0, target.Z - start.Z);
        float d = Math.Max(flat.Length(), 0.01f);
        float h = target.Y - start.Y;
        float a = Mathf.DegToRad(CombatConfig.ArcAngleDeg);
        float denom = 2 * MathF.Cos(a) * MathF.Cos(a) * (d * MathF.Tan(a) - h);
        float v = MathF.Sqrt(CombatConfig.Gravity * d * d / Math.Max(denom, 1e-6f));
        flat = flat.Normalized();
        return new Vector3(flat.X * MathF.Cos(a) * v, MathF.Sin(a) * v, flat.Z * MathF.Cos(a) * v);
    }

    public void Fire(Cannon cannon, Vector3 target)
    {
        var start = cannon.GlobalPosition;
        var owner = cannon.GetParent<CannonBattery>().Vessel;
        var mi = new MeshInstance3D
        {
            Mesh = Effects.SphereMesh(),
            MaterialOverride = ballMaterial,
            Position = start,
            Scale = Vector3.One * CombatConfig.BallRadius,
        };
        AddChild(mi);
        balls.Add(new Ball { Mesh = mi, Velocity = SolveLaunch(start, target), Owner = owner });
        effects.SpawnBurst(start, CombatConfig.MuzzleFlashColor, CombatConfig.MuzzleFlashLife, CombatConfig.MuzzleFlashSize);
        cannon.Reload = CombatConfig.ReloadTime;
        Shots++;
    }

    public void Update(float dt, IReadOnlyList<ICombatTarget> targets)
    {
        for (int i = balls.Count - 1; i >= 0; i--)
        {
            var b = balls[i];
            b.Velocity.Y -= CombatConfig.Gravity * dt;
            var p = b.Mesh.Position + b.Velocity * dt;
            b.Mesh.Position = p;

            bool done = false;
            foreach (var t in targets)
            {
                if (t.TargetNode == b.Owner || !t.ContainsPoint(p)) continue;
                t.TakeHit(p);
                effects.SpawnBurst(p, CombatConfig.HitFlashColor, CombatConfig.HitFlashLife, CombatConfig.HitFlashSize);
                Hits++;
                done = true;
                break;
            }
            if (!done)
            {
                float ground = terrain.HeightAt(p.X, p.Z);
                if (p.Y <= ground)
                {
                    var at = new Vector3(p.X, ground, p.Z);
                    if (ground <= 0 && terrain.IsWater(p.X, p.Z))
                        effects.SpawnRing(at, CombatConfig.SplashColor, CombatConfig.SplashLife, CombatConfig.SplashSize);
                    else
                        effects.SpawnRing(at, CombatConfig.LandPuffColor, CombatConfig.LandPuffLife, CombatConfig.LandPuffSize);
                    done = true;
                }
            }
            if (done)
            {
                b.Mesh.QueueFree();
                balls.RemoveAt(i);
            }
        }
    }
}
