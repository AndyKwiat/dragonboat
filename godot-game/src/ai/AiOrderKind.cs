using System;

// The orders a scenario can name. `boats: ai0:protect` in a scenario file
// (docs/scenarios.md) resolves through here.
public enum AiOrderKind
{
    // Do nothing at all, for ever. The default.
    Idle,
    // Sail to a destination, then lie to.
    Voyage,
    // Guard a station: hold it, fight what comes near it, go back to it.
    Protect,
}

public static class AiOrders
{
    public const AiOrderKind Default = AiOrderKind.Idle;

    // Parse a scenario's order name ("protect"); null if it names none.
    // Note this replaced a parser over AiState: states are not orders, and a
    // scenario that said `p1:engaging` should be an error, not a boat that
    // silently sits in a fighting state with nothing to fight.
    public static AiOrderKind? Parse(string name) =>
        Enum.TryParse<AiOrderKind>(name.Trim(), ignoreCase: true, out var k) && Enum.IsDefined(k) ? k : null;

    public static string Names => string.Join(", ", Enum.GetNames<AiOrderKind>()).ToLowerInvariant();

    public static bool NeedsDestination(AiOrderKind kind) => kind == AiOrderKind.Voyage;
}
