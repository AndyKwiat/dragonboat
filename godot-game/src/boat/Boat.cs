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
    public float Vx, Vy;            // velocity, world units/sec
    public float ThrustScale = 1;   // multiplier on MaxForce, driven by crew size
    public bool Turning;            // a steering key was held this frame
    public float TurnRateScale = 1; // multiplier on turn rate; Main sets 1/timeScale so
                                    // steering feels the same in fast-forward
    public float Roll;              // radians about the forward axis (+ = starboard down)
    public int HitsTaken;
    // Last step's force breakdown (world units/s²), for the vector overlay.
    public Vector2 ThrustForce, DragForce, CurrentForce, WindForce;
    public Vector2 Velocity => new(Vx, Vy);
    public Vector2 Forward => new(Mathf.Cos(Heading), Mathf.Sin(Heading));
    // Speed through the water along the keel, signed (negative = making sternway).
    public float Speed => Vx * Mathf.Cos(Heading) + Vy * Mathf.Sin(Heading);
    public float GroundSpeed => Speed;
    public float Length => BoatConfig.Length;

    private Terrain terrain = null!;
    public Currents? Currents; // optional: rapids that scale top speed (world/Currents.cs)
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

        float cosH = Mathf.Cos(Heading), sinH = Mathf.Sin(Heading);
        // Velocity in the hull frame: along the keel and abeam (+ = to starboard).
        float vFwd = Vx * cosH + Vy * sinH;
        float vLat = -Vx * sinH + Vy * cosH;

        // Rudder: authority scales with water speed over the hull, so a boat
        // merely drifting on a current can't steer. Turning also drags.
        int turn = (right ? 1 : 0) - (left ? 1 : 0);
        Turning = turn != 0;
        float authority = Math.Min(Math.Abs(vFwd) / BoatConfig.TurnFullSpeed, 1f);
        if (turn != 0 && vFwd != 0)
            Heading += turn * BoatConfig.TurnRate * TurnRateScale * authority * Math.Sign(vFwd) * dt;

        // Forces. Thrust is along the heading; drag is split by axis so the
        // keel resists sideways motion far more than forward motion.
        float thrust = forward ? BoatConfig.MaxForce * ThrustScale : back ? -BoatConfig.BrakeForce : 0;
        ThrustForce = new Vector2(cosH, sinH) * thrust;
        float k1 = BoatConfig.DragLinear + (turn != 0 ? BoatConfig.RudderDrag * authority : 0);
        float k2 = BoatConfig.DragQuad;
        float dFwd = -(k1 + k2 * Math.Abs(vFwd)) * vFwd;
        float dLat = -(k1 + k2 * Math.Abs(vLat)) * vLat * BoatConfig.LateralDragScale;
        DragForce = new Vector2(dFwd * cosH - dLat * sinH, dFwd * sinH + dLat * cosH);
        CurrentForce = Currents?.ForceAt(X, Y) ?? Vector2.Zero;
        WindForce = BoatConfig.WindVector();

        var f = ThrustForce + DragForce + CurrentForce + WindForce;
        // Semi-implicit: drag can't overshoot through zero within one step
        // as long as the step is small (BoatConfig.MaxStepSeconds).
        Vx += f.X * dt;
        Vy += f.Y * dt;
        X += Vx * dt;
        Y += Vy * dt;
        CollideWithLand();

        // Heel away from the sideways push, eased; and a slow bob.
        float wantRoll = Mathf.Clamp(vLat / BoatConfig.LeanFullSpeed, -1, 1) * BoatConfig.LeanAngle;
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
    // non-water cell it overlaps and the boat moves with it. The velocity
    // into the shore is removed (a little bounces back), the velocity along
    // it survives, so the boat slides along a coast instead of stopping dead.
    private void CollideWithLand()
    {
        float r = BoatConfig.Width / 2 * BoatConfig.CollisionRadiusScale;
        float cosH = Mathf.Cos(Heading), sinH = Mathf.Sin(Heading);
        foreach (float o in new[] { -BoatConfig.Length / 3, 0, BoatConfig.Length / 3 })
        {
            float cx = X + cosH * o, cy = Y + sinH * o;
            var push = terrain.ResolveCircle(ref cx, ref cy, r, out var normal);
            if (push == Vector2.Zero) continue;
            X += push.X; Y += push.Y;
            float into = Vx * normal.X + Vy * normal.Y;   // negative = moving into the land
            if (into < 0)
            {
                Vx -= normal.X * into * (1 + BoatConfig.CollisionBounce);
                Vy -= normal.Y * into * (1 + BoatConfig.CollisionBounce);
            }
        }
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
