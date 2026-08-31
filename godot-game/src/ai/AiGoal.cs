// What a boat is trying to do this frame, in the only three terms the rest
// of the AI needs: a point to make for, whether to keep way on while making
// for it, and whether the guns are free.
//
// Every state reduces to this — holding station is "make for the station,
// stopped"; presenting a broadside is "make for a point abeam of the enemy,
// stopped". That is what keeps AiSteering to a dozen lines and every rule
// test to a question about geometry.
public readonly record struct AiGoal(float X, float Y, bool KeepWay, bool OpenFire)
{
    public static AiGoal Stay(float x, float y) => new(x, y, false, false);
}
