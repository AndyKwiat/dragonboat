using System;

// What a boat is doing this instant. The bottom of two levels: an AiOrder
// (src/ai/AiOrder.cs) is the standing assignment and decides which of these
// the boat should be in; the state itself only names the behaviour, and its
// data lives in AiMemory.
//
// It is a plain enum because that is all it has to be: the transitions live
// in the orders and tactics, not in a switch over this, so there is nowhere
// to hang per-state data and no reason to make it a class. It is what the
// run's report line prints and what docs/ai.md documents.
//
// The game's own hostile fleet (src/enemies/EnemyShips.cs) is NOT an AI boat
// and has no state: those hulls are moored and deliberately left alone.
public enum AiState
{
    // Lies to: no helm orders, no thrust. Drifts only if the water moves her.
    Idle,

    // Making a passage along a route from the nav mesh (docs/nav.md), whether
    // that is a voyage to somewhere new or the way back to her station.
    Sailing,

    // Lying at her station, guarding it, waiting for something to happen.
    Holding,

    // Closing on a target that is too far off to shoot at.
    Pursuing,

    // In the fight: stopped, turning to keep the enemy on her beam, guns
    // free. There is no separate "firing" state — whether a shot actually
    // goes is decided by the battery every frame, not by the state machine.
    Engaging,
}

public static class AiStates
{
    public const AiState Default = AiState.Idle;

    public static string Name(AiState s) => s.ToString().ToLowerInvariant();
}
