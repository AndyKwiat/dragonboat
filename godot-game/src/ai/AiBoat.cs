using Godot;
using System.Collections.Generic;

// A non-player boat that sails and fights itself.
//
// It IS a Boat: the same hull, the same arcade physics, the same land
// collision and the same hit box as the player's — so whatever the AI learns
// to do, it does under the rules the player sails by. What it adds is an
// order, a memory, and a battery.
//
// This class does no deciding. It gathers what the boat can see into an
// AiSituation, hands that to the order (src/ai/AiOrder.cs), and carries out
// the answer. Every rule worth arguing about lives in Godot-free code that
// tests/ compiles — this file is the part that has to touch the engine.
//
// It is NOT the hostile fleet: those hulls are EnemyShip (src/enemies), they
// are moored, and nothing here touches them. AI boats exist only inside a
// test scenario, so a normal launch has none.
public partial class AiBoat : Boat
{
    // Blue, so an AI boat is never mistaken for the player's boat (dark
    // brown) or for the hostile fleet (red).
    public static readonly Color BlueHull = new Color("#2f6fb0");

    public IAiOrder Order = new IdleOrder();
    public readonly AiMemory Memory = new();
    // Only a boat whose order can fight is given guns (AiBoats.Spawn), so an
    // idle hull on a gunnery bench stays exactly as inert as it looks.
    public CannonBattery? Guns;

    public Vector2? Destination;
    private CombatSystem? combat;
    private NavMesh? nav;
    // The last thing decided, kept for the report line and the overlay.
    public AiContact? Aimed { get; private set; }
    public float Range { get; private set; }
    public float BearingDeg { get; private set; }
    public bool HadLineOfSight { get; private set; }

    public AiState State => Memory.State;
    // The water this boat has said it is making for, so its consorts can
    // leave it alone (src/ai/Traffic.cs).
    public Berth Claim { get; private set; }
    public NavRoute? Route => Memory.Route;
    public int Leg => Memory.Leg;
    public bool Arrived => Memory.Route != null && Memory.Leg >= Memory.Route.Waypoints.Count;
    public int Shots => Guns?.Shots ?? 0;

    // The point this boat is steering for, for the route overlay.
    public Vector2? Target =>
        Memory.Route != null && Memory.Leg < Memory.Route.Waypoints.Count
            ? new Vector2(Memory.Route.Waypoints[Memory.Leg].X, Memory.Route.Waypoints[Memory.Leg].Y)
            : null;

    public void Arm(CombatSystem combat, NavMesh nav)
    {
        this.combat = combat;
        this.nav = nav;
    }

    // How this boat looks to everyone else's AI.
    public AiContact AsContact() => new(GetInstanceId().GetHashCode(), Team, X, Y, Vx, Vy, Heading);

    // One frame: reload, look, decide, sail, shoot — in that order, and each
    // for a reason.
    public void Think(float dt, IReadOnlyList<AiContact> contacts, IReadOnlyList<Berth> taken, float consortRange)
    {
        // Reload FIRST and with the whole frame's dt. Nothing else ticks a
        // non-player battery — CombatSystem.Update only ticks the player's —
        // and ticking inside the sub-step loop below would reload several
        // times over. Miss this line and the boat fires one volley, ever.
        Guns?.Tick(dt);
        Memory.Advance(dt);

        // Decide ONCE, before moving. The sub-step count varies with the
        // frame time, so deciding inside the loop would make the boat's
        // behaviour depend on the frame rate.
        var bare = Snapshot(dt, null, consortRange);
        var target = Targeting.Pick(Team, X, Y, contacts, Order, Memory, bare);
        var situation = Snapshot(dt, target, consortRange);
        var goal = AiPilot.Decide(situation, Memory, Order);
        // Somewhere to BE, rather than a bearing to hold? Then check the
        // water is not already spoken for. A boat that is stopped and
        // turning is only pointing itself, and must not be nudged off the
        // bearing it has just worked for.
        if (goal.KeepWay)
        {
            var pivot = target is AiContact p ? ((float, float)?)(p.X, p.Y) : null;
            var (rx, ry) = Traffic.MakeRoom(goal.X, goal.Y, AiConfig.AiSpacing, taken, pivot);
            goal = goal with { X = rx, Y = ry };
        }
        // Claim only water she is actually going to. A boat that is stopped
        // and turning has a goal that is a bearing hint sixty units off her
        // bow, not a destination — claiming THAT would send her consorts
        // round a phantom and leave them free to park alongside her.
        Claim = goal.KeepWay ? new Berth(goal.X, goal.Y) : new Berth(X, Y);
        goal = ThroughWater(goal, situation);
        var helm = AiSteering.For(goal, situation);

        float remaining = dt;
        while (remaining > 0)
        {
            float step = Mathf.Min(remaining, BoatConfig.MaxStepSeconds);
            Update(step, helm);
            remaining -= step;
        }

        // Shoot LAST, after the hull has finished moving. BestCannon measures
        // off each gun's GlobalPosition, which is only right once Boat.Update
        // has run SyncMesh — firing before the move would aim from where the
        // boat used to be, and could loose a gun that no longer bears.
        if (goal.OpenFire && target is AiContact enemy) OpenFire(enemy);
    }

    private AiSituation Snapshot(float dt, AiContact? target, float consortRange)
    {
        Aimed = target;
        if (target is not AiContact t)
        {
            Range = 0; BearingDeg = 0; HadLineOfSight = false;
            return new AiSituation(dt, Team, X, Y, Heading, Speed, null, 0, 0, false, LoadedGuns(), consortRange);
        }
        float dx = t.X - X, dy = t.Y - Y;
        Range = Mathf.Sqrt(dx * dx + dy * dy);
        float bearing = Helmsman.Bearing(X, Y, Heading, t.X, t.Y);
        BearingDeg = Mathf.RadToDeg(bearing);
        // Line of sight is the expensive part, so it is re-tested a few times
        // a second rather than every frame; land does not move.
        if (Memory.SecondsSinceLos >= AiConfig.AiLosRefreshSec)
        {
            HadLineOfSight = nav == null || NavPath.Visible(nav, X, Y, t.X, t.Y);
            Memory.SecondsSinceLos = 0;
        }
        return new AiSituation(dt, Team, X, Y, Heading, Speed, t, Range, bearing, HadLineOfSight, LoadedGuns(), consortRange);
    }

    private int LoadedGuns()
    {
        if (Guns == null) return 0;
        int n = 0;
        foreach (var c in Guns.Cannons) if (c.Loaded) n++;
        return n;
    }

    // A goal on the far side of a headland is not a goal you can steer
    // straight at. When the water is not clear, path to it and make for the
    // next waypoint instead — the same nav mesh a voyage uses (docs/nav.md),
    // re-planned now and then because the target keeps moving.
    private AiGoal ThroughWater(AiGoal goal, in AiSituation s)
    {
        if (nav == null || Order.Tactic == null || !goal.KeepWay || s.LineOfSight) return goal;
        if (Memory.SecondsSinceRepath >= AiConfig.AiRepathSec)
        {
            Memory.Route = NavPath.Find(nav, X, Y, goal.X, goal.Y);
            Memory.Leg = 0;
            Memory.SecondsSinceRepath = 0;
        }
        if (Memory.Route == null) return goal;
        while (Memory.Leg < Memory.Route.Waypoints.Count
               && Helmsman.Reached(X, Y, Memory.Route.Waypoints[Memory.Leg].X, Memory.Route.Waypoints[Memory.Leg].Y))
            Memory.Leg++;
        if (Memory.Leg >= Memory.Route.Waypoints.Count) return goal;
        var wp = Memory.Route.Waypoints[Memory.Leg];
        return goal with { X = wp.X, Y = wp.Y };
    }

    // The battery decides the shot, not the state machine: the best loaded
    // cannon that bears, if it can physically reach. That is why the tactic's
    // bearing windows can be loose — they pick a word for the report, never
    // whether a round goes.
    private void OpenFire(in AiContact enemy)
    {
        if (Guns == null || combat == null) return;
        if (!Gunnery.Solve(X, Y, enemy.X, enemy.Y, enemy.Vx, enemy.Vy,
                CombatConfig.MuzzleSpeed, CombatConfig.Gravity, AiConfig.AiLeadFraction,
                out float aimX, out float aimY)) return;
        var at = new Vector3(aimX, 0, aimY);
        var cannon = Guns.BestCannon(at);
        if (cannon != null && Projectiles.WithinRange(cannon.GlobalPosition, at))
            combat.Projectiles.Fire(cannon, at);
    }

    // How far is left to sail, along the route rather than as the crow flies.
    public float DistanceToGo()
    {
        if (Memory.Route == null || Memory.Leg >= Memory.Route.Waypoints.Count) return 0;
        float total = new Vector2(Memory.Route.Waypoints[Memory.Leg].X - X, Memory.Route.Waypoints[Memory.Leg].Y - Y).Length();
        for (int i = Memory.Leg + 1; i < Memory.Route.Waypoints.Count; i++)
        {
            var a = Memory.Route.Waypoints[i - 1];
            var b = Memory.Route.Waypoints[i];
            total += new Vector2(b.X - a.X, b.Y - a.Y).Length();
        }
        return total;
    }
}
