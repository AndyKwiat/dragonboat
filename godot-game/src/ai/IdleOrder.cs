// Do nothing, for ever. What a boat has unless a scenario says otherwise,
// and the baseline every other order is measured against: an idle boat is
// unarmed, never looks for a target, and gives no helm orders at all.
public sealed class IdleOrder : IAiOrder
{
    public AiOrderKind Kind => AiOrderKind.Idle;
    public IAiTactic? Tactic => null;

    public bool Acquire(in AiSituation s, in AiContact c, AiMemory m) => false;
    public bool Keep(in AiSituation s, in AiContact c, AiMemory m) => false;

    public AiGoal Idle(in AiSituation s, AiMemory m)
    {
        m.Enter(AiState.Idle);
        return AiGoal.Stay(s.X, s.Y);
    }
}
