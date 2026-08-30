using Godot;
using System.Collections.Generic;

// The AI fleet: every self-sailing non-player boat in the run, ticked once a
// frame. Only a test scenario ever puts boats in it (src/scenarios), so in a
// normal game this holds nothing and Update walks an empty list — the game
// sails exactly as it did before the AI existed.
public partial class AiBoats : Node3D
{
    private readonly List<AiBoat> boats = new();
    public IReadOnlyList<AiBoat> Boats => boats;
    public int Count => boats.Count;

    private Terrain terrain = null!;

    public void Init(Terrain terrain)
    {
        Name = "AiBoats";
        this.terrain = terrain;
    }

    // Put a boat on the water. Sails are struck: an AI boat under canvas
    // would be shoved about by the wind before it has any say in where it
    // goes, which is not the baseline the pathing work wants.
    public AiBoat Spawn(float x, float y, float heading, AiState state)
    {
        var boat = new AiBoat { X = x, Y = y, Heading = heading, State = state, Hull = AiBoat.BlueHull, SailsUp = false };
        boat.Init(terrain);
        AddChild(boat);
        boats.Add(boat);
        return boat;
    }

    // dt is game time, and 0 while the world is paused (the city menu).
    public void Update(float dt, Boat player)
    {
        if (dt <= 0 || boats.Count == 0) return;
        foreach (var b in boats) b.Think(dt, player);
    }

    // One line per boat for a scripted run's final report, so a pathing run
    // can be asserted on without looking at pixels.
    public string Report()
    {
        if (boats.Count == 0) return "";
        var parts = new List<string>();
        foreach (var b in boats)
            parts.Add($"{b.State.ToString().ToLowerInvariant()}@({b.X:F1}, {b.Y:F1}) {Mathf.RadToDeg(b.Heading):F0}° {b.Speed:F1}");
        return $"ai boats {string.Join("; ", parts)}";
    }
}
