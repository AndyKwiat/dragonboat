using System;

// What a non-player boat under AI control is currently trying to do.
//
// This is the dial the pathing work turns: every AI boat is in exactly one
// state, the state alone decides the helm orders it gives itself each frame
// (src/ai/AiBoat.cs), and new behaviour arrives as a new state rather than
// as a flag threaded through the old one. Scenarios name the state a boat
// starts in (`boats: p1:idle`).
//
// The game's own hostile fleet (src/enemies/EnemyShips.cs) is NOT an AI boat
// and has no state: those hulls are moored, never move, and are deliberately
// left exactly as they were — see docs/ai.md.
public enum AiState
{
    // Lies to: sails struck, no helm orders, no thrust. Drifts only if the
    // water moves her. The state a scenario boat starts in unless it says
    // otherwise, and the baseline the pathing states are measured against.
    Idle,
}

public static class AiStates
{
    public const AiState Default = AiState.Idle;

    // Parse a scenario's state name ("idle"); null if it names no state.
    public static AiState? Parse(string name) =>
        Enum.TryParse<AiState>(name.Trim(), ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : null;

    public static string Names => string.Join(", ", Enum.GetNames<AiState>()).ToLowerInvariant();
}
