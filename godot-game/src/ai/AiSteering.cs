using System;

// A goal point becomes helm orders. The whole of it — because every state
// reduces to "make for this point, with or without way on", the steering has
// nothing left to decide and Helmsman does the actual work.
public static class AiSteering
{
    public static HelmOrder For(in AiGoal goal, in AiSituation s)
    {
        if (Helmsman.Reached(s.X, s.Y, goal.X, goal.Y)) return Brake(s, HelmOrder.None);
        var helm = Helmsman.SteerTo(s.X, s.Y, s.Heading, goal.X, goal.Y);
        if (goal.KeepWay) return helm;
        // Stopped, but still steering: the rudder answers under oars however
        // slowly she is going (BoatConfig.OarStandingTurn), so she pivots on
        // the spot while the way comes off. That is what lets a boat present
        // a broadside without orbiting her target.
        return Brake(s, new HelmOrder(false, false, helm.Port, helm.Starboard));
    }

    private static HelmOrder Brake(in AiSituation s, in HelmOrder helm) =>
        s.Speed > AiConfig.AiStopSpeed
            ? new HelmOrder(false, true, helm.Port, helm.Starboard)
            : helm;
}
