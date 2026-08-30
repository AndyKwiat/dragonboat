using Godot;

// A non-player boat that sails itself.
//
// It IS a Boat: the same hull, the same arcade physics, the same land
// collision and the same hit box as the player's — so whatever the AI learns
// to do, it does under the rules the player sails by. All it adds is a state
// (src/ai/AiState.cs) and, once a frame, the helm orders that state wants;
// the physics step is the player's, unchanged.
//
// It is NOT the hostile fleet: those hulls are EnemyShip (src/enemies), they
// are moored, they never move, and this class does not touch them. AI boats
// exist only inside a test scenario (src/scenarios/Scenario.cs), so a normal
// launch has none of them.
public partial class AiBoat : Boat
{
    // Blue, so an AI boat is never mistaken for the player's boat (dark
    // brown) or for the hostile fleet (red).
    public static readonly Color BlueHull = new Color("#2f6fb0");

    public AiState State = AiStates.Default;

    // One frame of AI: decide, then sail. `player` is the player's boat —
    // most states will want to know where it is.
    public void Think(float dt, Boat player)
    {
        var helm = State switch
        {
            // Lies to. Sails struck (so the wind cannot take her) and not an
            // order given: she goes nowhere unless the water moves her.
            AiState.Idle => HelmOrder.None,
            _ => HelmOrder.None,
        };
        // Sub-stepped exactly like the player's boat, so a big time scale
        // can't cut corners on a turn (Main sub-steps the player the same way).
        float remaining = dt;
        while (remaining > 0)
        {
            float step = Mathf.Min(remaining, BoatConfig.MaxStepSeconds);
            Update(step, helm);
            remaining -= step;
        }
    }
}
