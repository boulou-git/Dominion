#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class PendingDecisionInputLockTests
{
    [Test]
    public void ActivePendingDecision_LocksGameplayInput()
    {
        GameStateSnapshot state = StartedState();
        state.Resolution.IsActive = true;
        state.Resolution.PendingDecision.IsPending = true;

        Assert.IsTrue(PendingDecisionInputLock.IsActive(state));
    }

    [Test]
    public void ResolutionWithoutPendingDecision_DoesNotLockChoiceInput()
    {
        GameStateSnapshot state = StartedState();
        state.Resolution.IsActive = true;

        Assert.IsFalse(PendingDecisionInputLock.IsActive(state));
    }

    [Test]
    public void PendingFlagOutsideActiveResolution_DoesNotLockGameplayInput()
    {
        GameStateSnapshot state = StartedState();
        state.Resolution.PendingDecision.IsPending = true;

        Assert.IsFalse(PendingDecisionInputLock.IsActive(state));
    }

    private static GameStateSnapshot StartedState()
    {
        return new GameStateSnapshot { IsStarted = true };
    }
}
#endif
