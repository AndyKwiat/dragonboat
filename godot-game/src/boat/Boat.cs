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
    public float X, Y;   // set by Main before Init (off a large city's shore)
    public float Heading = BoatConfig.StartHeading;
    public float Vx, Vy;            // velocity, world units/sec
    public float ThrustScale = 1;   // multiplier on MaxForce, driven by crew size
    public bool Turning;            // a steering key was held this frame
    public float TurnRateScale = 1; // multiplier on turn rate; Main sets 1/timeScale so
                                    // steering feels the same in fast-forward
    public bool SailsUp = true;     // SPACE toggles: canvas (wind, fast, sluggish helm)
                                    // vs oars (no wind, punchy, slower, tires the crew)
    public bool Rowing;             // sails down AND a helm order given this frame
    public float SailDrive = 1;     // 0..1 of the rig's pull the wind is giving
    public Wind? Wind;              // the world's wind; only felt with the sails up
    public float Roll;              // radians about the forward axis (+ = starboard down)
    public int HitsTaken;
    // Last step's force breakdown (world units/s²), for the vector overlay.
    public Vector2 ThrustForce, DragForce, CurrentForce, WindForce;
    public Vector2 Velocity => new(Vx, Vy);
    public Vector2 Forward => new(Mathf.Cos(Heading), Mathf.Sin(Heading));
    // Speed through the water along the keel, signed (negative = making sternway).
    public float Speed => Vx * Mathf.Cos(Heading) + Vy * Mathf.Sin(Heading);
    public float GroundSpeed => Speed;

    // Hull size, per boat rather than per game: the numbers default to the
    // one vessel the game has today, but they are fields so a launch and a
    // merchantman can differ later. Everything that measures this hull —
    // the model, the shadow, the hit box, the collision shell — reads these.
    public float HullLength = BoatConfig.Length;
    public float HullWidth = BoatConfig.Width;
    public float Length => HullLength;
    // What it takes to shove her aside. Deck area is a good enough stand-in
    // for displacement, and keeps a bigger hull harder to push.
    public float Mass => HullLength * HullWidth;

    // The collision shell: three circles along the keel (src/boat/Hulls.cs),
    // the same shape the shore is resolved against.
    public Hull Shell => new(X, Y, Heading, HullLength,
        HullWidth / 2 * BoatConfig.CollisionRadiusScale, Mass);

    private Terrain terrain = null!;
    public Currents? Currents; // optional: rapids that scale top speed (world/Currents.cs)
    private BoatModel model = null!;
    private BlobShadow shadow = null!;
    private float bobPhase;
    private float hitFlash;

    public Node3D TargetNode => this;

    // Hull colour, so an AI boat reads as somebody else's (src/ai/AiBoat.cs).
    // Set before the node enters the tree; the model is built in _Ready.
    public Color Hull = BoatConfig.HullColor;
    // Whose side this hull is on. Boats on different teams are enemies and
    // nothing else decides hostility (src/ai/Targeting.cs). The player is 0,
    // so a vessel nobody has assigned is friendly by default.
    public int Team;

    public void Init(Terrain terrain)
    {
        this.terrain = terrain;
    }

    public override void _Ready()
    {
        model = new BoatModel(HullLength, Hull);
        AddChild(model);
        model.SetSails(SailsUp);
        // The shadow is a sibling, not a child: it must stay flat on the
        // water while the hull rolls and bobs above it.
        shadow = new BlobShadow(BoatConfig.ShadowStrength);
        GetParent().CallDeferred(Node.MethodName.AddChild, shadow);
        SyncMesh();
    }

    /// Sails up or down. The model raises/strikes the canvas to match.
    public void SetSails(bool up)
    {
        SailsUp = up;
        model?.SetSails(up);
    }

    // The player's helm: WASD / arrows off the keyboard.
    public void Update(float dt, InputState input) =>
        Update(dt, new HelmOrder(input.Down(Key.W, Key.Up), input.Down(Key.S, Key.Down),
                                 input.Down(Key.A, Key.Left), input.Down(Key.D, Key.Right)));

    // One physics step against a set of helm orders, whoever gave them — the
    // keyboard above, or an AI boat's state machine (src/ai/AiBoat.cs).
    public void Update(float dt, HelmOrder helm)
    {
        bool forward = helm.Ahead;
        bool back = helm.Astern;
        bool left = helm.Port;
        bool right = helm.Starboard;

        float cosH = Mathf.Cos(Heading), sinH = Mathf.Sin(Heading);
        // Velocity in the hull frame: along the keel and abeam (+ = to starboard).
        float vFwd = Vx * cosH + Vy * sinH;
        float vLat = -Vx * sinH + Vy * cosH;

        // Rudder: authority scales with water speed over the hull, so a boat
        // merely drifting on a current can't steer. Turning also drags. A
        // boat under canvas answers the helm worse than one under oars.
        int turn = (right ? 1 : 0) - (left ? 1 : 0);
        Turning = turn != 0;
        Rowing = !SailsUp && (forward || back || turn != 0);
        float authority = Math.Min(Math.Abs(vFwd) / BoatConfig.TurnFullSpeed, 1f);
        // Oars don't need water over the rudder: the crew backs one side and
        // pulls the other, so a boat under oars can spin where it lies. It
        // keeps at least OarStandingTurn of full authority however slowly it
        // is going, and turns bow-first even dead in the water.
        int turnSign = vFwd < 0 ? -1 : 1;
        if (!SailsUp) authority = Math.Max(authority, BoatConfig.OarStandingTurn);
        else if (vFwd == 0) authority = 0;
        if (turn != 0 && authority > 0)
            Heading += turn * BoatConfig.TurnRate * BoatConfig.TurnScaleFor(SailsUp)
                     * TurnRateScale * authority * turnSign * dt;

        // Forces. Thrust is along the heading; drag is split by axis so the
        // keel resists sideways motion far more than forward motion. The sail
        // state scales both: canvas pushes gently against little drag (fast,
        // lazy), oars shove hard against a lot of it (nippy, slower).
        float thrustScale = BoatConfig.ThrustScaleFor(SailsUp);
        float dragScale = BoatConfig.DragScaleFor(SailsUp);
        // Under canvas the sails are always drawing: the boat makes way
        // whether or not W is held (holding it changes nothing), and S is how
        // you take the way off. At the oars nothing moves without an order.
        bool driving = SailsUp ? !back : forward;
        // What the canvas pulls is the wind's to give: becalmed, the boat
        // goes nowhere until the oars come out. Oars pull their own weight.
        SailDrive = SailsUp && Wind != null
            ? Wind.Drive(BoatConfig.SailFullWind, BoatConfig.SailWindKneeFrac, BoatConfig.SailWindKneeDrive)
            : 1;
        float thrust = driving ? BoatConfig.MaxForce * ThrustScale * thrustScale * SailDrive
                     : back ? -BoatConfig.BrakeForce * thrustScale : 0;
        ThrustForce = new Vector2(cosH, sinH) * thrust;
        float k1 = BoatConfig.DragLinear * dragScale + (turn != 0 ? BoatConfig.RudderDrag * authority : 0);
        float k2 = BoatConfig.DragQuad * dragScale;
        float dFwd = -(k1 + k2 * Math.Abs(vFwd)) * vFwd;
        float dLat = -(k1 + k2 * Math.Abs(vLat)) * vLat * BoatConfig.LateralDragScale;
        DragForce = new Vector2(dFwd * cosH - dLat * sinH, dFwd * sinH + dLat * cosH);
        CurrentForce = Currents?.ForceAt(X, Y) ?? Vector2.Zero;
        // Only canvas catches the wind; with the sails struck the boat feels
        // nothing but the oars, the water and the current.
        WindForce = Vector2.Zero;
        if (SailsUp && Wind != null)
        {
            var (wx, wy) = Wind.ForceOn(Heading, BoatConfig.WindBeatingScale);
            WindForce = new Vector2(wx, wy);
        }

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
        float r = HullWidth / 2 * BoatConfig.CollisionRadiusScale;
        float cosH = Mathf.Cos(Heading), sinH = Mathf.Sin(Heading);
        foreach (float o in new[] { -HullLength / 3, 0, HullLength / 3 })
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
            shadow.Scale = new Vector3(HullLength / 2, 1, HullWidth / 2) * BoatConfig.ShadowScale;
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
        return Math.Abs(l.X) <= HullLength / 2 + m
            && l.Y >= -m && l.Y <= BoatConfig.Height + m
            && Math.Abs(l.Z) <= HullWidth / 2 + m;
    }

    public void TakeHit(Vector3 p)
    {
        HitsTaken++;
        hitFlash = CombatConfig.HullFlashSec;
    }

    // Flash the hull without counting it as a hit — what a collision uses, so
    // scraping alongside another boat reads on screen without pretending a
    // cannonball landed.
    public void Jolt() => hitFlash = CombatConfig.HullFlashSec;
}
