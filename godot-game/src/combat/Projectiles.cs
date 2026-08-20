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
        public BlobShadow Shadow = null!;
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

    // Every ball leaves at MuzzleSpeed; the LOW launch angle that lands on
    // the target is tan θ = (v² − √(v⁴ − g(g d² + 2 h v²))) / (g d), so a
    // farther target gets a higher angle and a longer flight. Out of range
    // the discriminant goes negative and we fire at the range-maximising
    // angle instead (tan θ = v²/(g d)) — the ball falls short; WithinRange
    // lets the aim UI warn first. The angle is floored at MinAngleDeg, so a
    // very close target is overshot: the ball sails over it.
    private static float Discriminant(float d, float h, float v, float g) =>
        v * v * v * v - g * (g * d * d + 2 * h * v * v);

    public static bool WithinRange(Vector3 start, Vector3 target)
    {
        var flat = new Vector3(target.X - start.X, 0, target.Z - start.Z);
        return Discriminant(flat.Length(), target.Y - start.Y, CombatConfig.MuzzleSpeed, CombatConfig.Gravity) >= 0;
    }

    public static Vector3 SolveLaunch(Vector3 start, Vector3 target)
    {
        var flat = new Vector3(target.X - start.X, 0, target.Z - start.Z);
        float d = Math.Max(flat.Length(), 0.01f);
        float h = target.Y - start.Y;
        float v = CombatConfig.MuzzleSpeed, g = CombatConfig.Gravity;
        float disc = Discriminant(d, h, v, g);
        float tan = (v * v - MathF.Sqrt(Math.Max(disc, 0))) / (g * d);
        float angle = Math.Max(MathF.Atan(tan), Mathf.DegToRad(CombatConfig.MinAngleDeg));
        flat = flat.Normalized();
        return new Vector3(flat.X * MathF.Cos(angle) * v, MathF.Sin(angle) * v, flat.Z * MathF.Cos(angle) * v);
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
        var shadow = new BlobShadow(CombatConfig.BallShadowStrength);
        AddChild(shadow);
        balls.Add(new Ball { Mesh = mi, Shadow = shadow, Velocity = SolveLaunch(start, target), Owner = owner });
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

            // Blob shadow on the ground straight below, smaller and fainter
            // the higher the ball is.
            float groundY = terrain.HeightAt(p.X, p.Z);
            float heightT = BlobShadow.HeightT(p.Y - groundY, CombatConfig.ShadowShrinkHeight);
            b.Shadow.Position = new Vector3(p.X, groundY + 0.04f, p.Z);
            b.Shadow.Scale = Vector3.One * (CombatConfig.BallRadius * CombatConfig.BallShadowScale
                                            * Mathf.Lerp(1, CombatConfig.BallShadowMin, heightT));
            b.Shadow.SetStrength(CombatConfig.BallShadowStrength * (1 - heightT));

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
                b.Shadow.QueueFree();
                balls.RemoveAt(i);
            }
        }
    }
}
