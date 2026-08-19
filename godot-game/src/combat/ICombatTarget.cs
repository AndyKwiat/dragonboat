using Godot;

// Anything a cannonball can hit. The player boat implements it today; enemy
// boats (and forts, wrecks…) implement it tomorrow and register with
// CombatSystem.AddTarget. Positions are Godot space (X = world x, Z = world
// y, Y = height).
public interface ICombatTarget
{
    // The scene node — projectiles use it to skip the vessel that fired them.
    Node3D TargetNode { get; }
    // Is this world point inside the hit volume (hull + CombatConfig.HitMargin)?
    bool ContainsPoint(Vector3 p);
    // A ball landed at p. Visual/audio reaction, damage, scoring all start here.
    void TakeHit(Vector3 p);
}
