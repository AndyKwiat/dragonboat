using Godot;
using System;

// The player boat: arcade physics on the water, a lean into turns and a bob,
// kept off the land by the terrain, and hittable by cannonballs. World
// coords are y-down, so heading 0 points +x (east) and increasing heading
// turns clockwise on the map (towards +y / south).
//
// Vessel frame (this node's local space): forward +X, up +Y, starboard +Z.
// Guns (combat/CannonBattery) are mounted as children and ride along.
public partial class Boat : Node3D, ICombatTarget
{
    public float X = BoatConfig.StartX;
    public float Y = BoatConfig.StartY;
    public float Heading = BoatConfig.StartHeading;
    public float Speed;            // world units/sec, signed (negative = reversing)
    public float MaxSpeedScale = 1; // multiplier on top speed, driven by crew size
    public bool Turning;            // a steering key was held this frame
    public float TurnRateScale = 1; // multiplier on turn rate; Main sets 1/timeScale so
                                    // steering feels the same in fast-forward
    public float Roll;              // radians about the forward axis (+ = starboard down)
    public float GroundSpeed;       // actual movement along the heading this step (≤ Speed when pushed by land)
    public int HitsTaken;
    public float Length => BoatConfig.Length;

    private Terrain terrain = null!;
    private BoatModel model = null!;
    private BlobShadow shadow = null!;
    private float bobPhase;
    private float hitFlash;

    public Node3D TargetNode => this;

    public void Init(Terrain terrain)
    {
        this.terrain = terrain;
    }

    public override void _Ready()
    {
        model = new BoatModel(BoatConfig.Length, BoatConfig.HullColor);
        AddChild(model);
        // The shadow is a sibling, not a child: it must stay flat on the
        // water while the hull rolls and bobs above it.
        shadow = new BlobShadow(BoatConfig.ShadowStrength);
        GetParent().CallDeferred(Node.MethodName.AddChild, shadow);
        SyncMesh();
    }

    public void Update(float dt, InputState input)
    {
        bool forward = input.Down(Key.W, Key.Up);
        bool back = input.Down(Key.S, Key.Down);
        bool left = input.Down(Key.A, Key.Left);
        bool right = input.Down(Key.D, Key.Right);

        // Accelerate / brake, then apply friction when no throttle input.
        if (forward) Speed += BoatConfig.Accel * dt;
        else if (back) Speed -= BoatConfig.Accel * dt;
        else
        {
            float drop = BoatConfig.Friction * dt;
            Speed = Math.Abs(Speed) <= drop ? 0 : Speed - Math.Sign(Speed) * drop;
        }
        // Crew only helps you go forward; backing up is backing up.
        Speed = Math.Clamp(Speed, -BoatConfig.MaxReverseSpeed, BoatConfig.MaxSpeed * MaxSpeedScale);

        // Steering authority scales with speed: a dead boat doesn't spin in place.
        int turn = (right ? 1 : 0) - (left ? 1 : 0);
        Turning = turn != 0;
        if (turn != 0 && Speed != 0)
        {
            float authority = Math.Min(Math.Abs(Speed) / BoatConfig.TurnFullSpeed, 1f);
            Heading += turn * BoatConfig.TurnRate * TurnRateScale * authority * Math.Sign(Speed) * dt;
        }

        float x0 = X, y0 = Y;
        X += Mathf.Cos(Heading) * Speed * dt;
        Y += Mathf.Sin(Heading) * Speed * dt;
        CollideWithLand(dt);
        GroundSpeed = dt > 0 ? ((X - x0) * Mathf.Cos(Heading) + (Y - y0) * Mathf.Sin(Heading)) / dt : 0;

        // Heel outward from the turn by LeanAngle at full speed (the
        // prototype's roll), eased; and a slow bob.
        float speedFrac = Math.Min(1, Math.Abs(Speed) / BoatConfig.MaxSpeed);
        float wantRoll = -turn * BoatConfig.LeanAngle * speedFrac;
        float k = BoatConfig.LeanSmoothSec <= 0 ? 1 : 1 - Mathf.Exp(-dt / BoatConfig.LeanSmoothSec);
        Roll += (wantRoll - Roll) * k;
        bobPhase += BoatConfig.BobRadPerSec * dt;

        if (hitFlash > 0)
        {
            hitFlash = Math.Max(0, hitFlash - dt);
            model.SetHitFlash(CombatConfig.HullFlashSec > 0 ? hitFlash / CombatConfig.HullFlashSec : 0, CombatConfig.HullHitColor);
        }
        SyncMesh();
    }

    // The hull is three circles along its length; each is pushed out of any
    // non-water cell it overlaps and the boat moves with it, so it slides
    // along a shore instead of stopping dead. While in contact, speed decays
    // at CollisionDrag scaled by how square-on the hit is.
    private void CollideWithLand(float dt)
    {
        float r = BoatConfig.Width / 2 * BoatConfig.CollisionRadiusScale;
        float cosH = Mathf.Cos(Heading), sinH = Mathf.Sin(Heading);
        float worstInto = 0;
        foreach (float o in new[] { -BoatConfig.Length / 3, 0, BoatConfig.Length / 3 })
        {
            float cx = X + cosH * o, cy = Y + sinH * o;
            var push = terrain.ResolveCircle(ref cx, ref cy, r, out var normal);
            if (push == Vector2.Zero) continue;
            X += push.X; Y += push.Y;
            // How squarely we were moving into the obstacle, 0..1.
            float into = -(cosH * normal.X + sinH * normal.Y) * Math.Sign(Speed);
            worstInto = Math.Max(worstInto, into);
        }
        if (worstInto > 0) Speed *= Math.Max(0, 1 - worstInto * BoatConfig.CollisionDrag * dt);
    }

    public void SyncMesh()
    {
        float bob = Mathf.Sin(bobPhase) * BoatConfig.BobAmplitude;
        Position = CameraRig.WorldToGodot(X, Y, bob);
        if (shadow != null && shadow.IsInsideTree())
        {
            // A hull-shaped ellipse on the water; raised land cells at the
            // shoreline simply occlude whatever overhangs them.
            shadow.Position = new Vector3(X, 0.03f, Y);
            shadow.Rotation = new Vector3(0, -Heading, 0);
            shadow.Scale = new Vector3(BoatConfig.Length / 2, 1, BoatConfig.Width / 2) * BoatConfig.ShadowScale;
            shadow.SetStrength(BoatConfig.ShadowStrength);
        }
        // Euler order YXZ: yaw about +Y first (a rotation about +Y turns +X
        // towards -Z; world headings turn +X towards +Z, hence the sign),
        // then roll about the boat's own forward axis.
        Rotation = new Vector3(Roll, -Heading, 0);
    }

    // ICombatTarget: the hull box plus a margin, in the vessel frame.
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
    }
}
