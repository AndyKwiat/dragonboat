using Godot;
using System;

// The player boat: a brown box that drives around the world. World coords
// are y-down, so heading 0 points +x (right/east) and increasing heading
// turns clockwise on the map (towards +y / south).
public partial class Boat : Node3D
{
    public float X = Config.BoatStartX;
    public float Y = Config.BoatStartY;
    public float Heading = Config.BoatStartHeading;
    public float Speed;            // world units/sec, signed (negative = reversing)
    public float MaxSpeedScale = 1; // multiplier on top speed, driven by crew size

    public override void _Ready()
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Config.BoatLength, Config.BoatHeight, Config.BoatWidth) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Config.BoatColor,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        mesh.Position = new Vector3(0, Config.BoatHeight / 2, 0);  // sitting on the water at 0
        AddChild(mesh);
        SyncMesh();
    }

    public void Update(float dt, InputState input)
    {
        bool forward = input.Down(Key.W, Key.Up);
        bool back = input.Down(Key.S, Key.Down);
        bool left = input.Down(Key.A, Key.Left);
        bool right = input.Down(Key.D, Key.Right);

        // Accelerate / brake, then apply friction when no throttle input.
        if (forward) Speed += Config.BoatAccel * dt;
        else if (back) Speed -= Config.BoatAccel * dt;
        else
        {
            float drop = Config.BoatFriction * dt;
            Speed = Math.Abs(Speed) <= drop ? 0 : Speed - Math.Sign(Speed) * drop;
        }
        // Crew only helps you go forward; backing up is backing up.
        Speed = Math.Clamp(Speed, -Config.BoatMaxReverseSpeed, Config.BoatMaxSpeed * MaxSpeedScale);

        // Steering authority scales with speed: a dead boat doesn't spin in place.
        int turn = (right ? 1 : 0) - (left ? 1 : 0);
        if (turn != 0 && Speed != 0)
        {
            float authority = Math.Min(Math.Abs(Speed) / Config.BoatTurnFullSpeed, 1f);
            Heading += turn * Config.BoatTurnRate * authority * Math.Sign(Speed) * dt;
        }

        X += Mathf.Cos(Heading) * Speed * dt;
        Y += Mathf.Sin(Heading) * Speed * dt;
        SyncMesh();
    }

    public void SyncMesh()
    {
        Position = CameraRig.WorldToGodot(X, Y, 0);
        // A rotation about +Y turns +X towards -Z; world headings turn +X
        // towards +Z (south), hence the sign.
        Rotation = new Vector3(0, -Heading, 0);
    }
}
