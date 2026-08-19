using System;

// Crew maths: the head count drives rations, wages, top speed and hold space.
public sealed class CrewLimits
{
    public int Starting, Min, Max;
    public float SpeedAtMin, SpeedAtMax;
    public int SpeedFullCrew;   // speed tops out here; more hands only carry cargo
    public int CargoPerCrew;
}

public static class Crew
{
    public static int Clamp(int count, CrewLimits l) => Math.Clamp(count, l.Min, l.Max);

    public static int CargoCapacity(int crew, CrewLimits l) => Clamp(crew, l) * l.CargoPerCrew;

    // False at the limits (nothing would change) and false when putting hands
    // ashore would leave less hold space than the cargo already aboard.
    public static bool ChangeAllowed(int crew, int delta, int cargoUnits, CrewLimits l)
    {
        int next = Clamp(crew + delta, l);
        return next != Clamp(crew, l) && CargoCapacity(next, l) >= cargoUnits;
    }

    public static float SpeedMultiplier(int crew, CrewLimits l)
    {
        int span = Math.Max(l.SpeedFullCrew, l.Min) - l.Min;
        int over = Clamp(crew, l) - l.Min;
        float t = span <= 0 ? 1 : Math.Min((float)over / span, 1f);
        return l.SpeedAtMin + (l.SpeedAtMax - l.SpeedAtMin) * t;
    }
}
