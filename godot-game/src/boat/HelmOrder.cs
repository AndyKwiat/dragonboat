// What a helm is being told to do this frame: ahead, astern, and rudder.
// The player's orders come from the keyboard (Boat.Update reads InputState);
// an AI boat's come from its state machine (src/ai/AiBoat.cs). Boat physics
// only ever sees this struct, so nothing in the boat code knows or cares
// which of the two is steering. No Godot types.
public readonly struct HelmOrder
{
    public readonly bool Ahead, Astern, Port, Starboard;

    public HelmOrder(bool ahead, bool astern, bool port, bool starboard)
    {
        Ahead = ahead; Astern = astern; Port = port; Starboard = starboard;
    }

    // Hands off the tiller: no way on, no rudder.
    public static HelmOrder None => default;
}
