// Another vessel, as the AI sees it: where it is, where it is going, and
// whose side it is on. Deliberately not a Boat — the decision code is
// Godot-free and this is all it is allowed to know.
public readonly record struct AiContact(
    int Id, int Team, float X, float Y, float Vx, float Vy, float Heading);
