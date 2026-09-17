/// <summary>
/// Single definition of the gameplay input lock used while a durable card effect is
/// waiting for a player decision. The Escape menu is intentionally outside this lock.
/// </summary>
public static class PendingDecisionInputLock
{
    public static bool IsActive(GameStateSnapshot state)
    {
        return state != null && state.IsStarted &&
               state.Resolution != null && state.Resolution.IsActive &&
               state.Resolution.PendingDecision != null &&
               state.Resolution.PendingDecision.IsPending;
    }
}
