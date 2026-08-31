// The whole per-frame decision, in one place: find something to fight, ask
// the order what to do about it, and turn the answer into helm orders.
//
// Three lines, and every one of them is somebody else's job — which is the
// point. Adding a mission is a new IAiOrder, adding a fighting style is a new
// IAiTactic, and adding a team rule is a filter inside Targeting. None of
// them changes this.
public static class AiPilot
{
    public static AiGoal Decide(in AiSituation s, AiMemory memory, IAiOrder order) =>
        s.HasTarget && order.Tactic != null
            ? order.Tactic.Fight(s, memory)
            : order.Idle(s, memory);
}
