using System;

// A boat's standing assignment — what it is FOR. Set at spawn from the
// scenario and effectively never changed; a commander handing out new orders
// later is just an assignment, because an order is data.
//
// The split with AiTactic is by what each one owns, not by who runs first:
//
//   The ORDER owns whether there is a fight at all — which contacts count,
//   how long to keep one, how far it may chase, and what it does when there
//   is nobody to fight. It never says HOW to fight.
//
//   The TACTIC owns the boat while a target exists. It never says "go home".
//
// That is why a rammer that has overshot and a gunner that wants to open the
// range need no new states and never touch the order: both are the tactic
// returning a goal point somewhere else.
public interface IAiOrder
{
    AiOrderKind Kind { get; }

    // Is this contact worth starting a fight over? Called only for contacts
    // that are hostile and not already the current target.
    bool Acquire(in AiSituation s, in AiContact c, AiMemory m);

    // Is the fight still on? Deliberately separate from Acquire so the two
    // can disagree — that gap is the hysteresis.
    bool Keep(in AiSituation s, in AiContact c, AiMemory m);

    // What to do with no target. Sets the state and returns where to go.
    AiGoal Idle(in AiSituation s, AiMemory m);

    // Null for an order that does not fight; then a target is never sought
    // and the boat is never armed.
    IAiTactic? Tactic { get; }
}

// How a boat fights, once its order has found it something to fight. The
// pluggable half: a boat that rams and a boat that stands off and shoots are
// two of these sharing one order.
public interface IAiTactic
{
    // Owns the state while there is a target. Returns the point to make for.
    AiGoal Fight(in AiSituation s, AiMemory m);
}
