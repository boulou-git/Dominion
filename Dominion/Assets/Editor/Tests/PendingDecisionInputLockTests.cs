#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
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

    [Test]
    public void SingleSelection_ClickingAnotherChoice_ReplacesCurrentChoice()
    {
        HashSet<int> selected = new HashSet<int> { 12 };

        Assert.IsTrue(PendingDecisionSelectionRules.Toggle(selected, 34, 1));

        CollectionAssert.AreEquivalent(new[] { 34 }, selected);
    }

    [Test]
    public void MultipleSelection_ClickingAnotherChoice_KeepsCurrentChoices()
    {
        HashSet<string> selected = new HashSet<string> { "first" };

        Assert.IsTrue(PendingDecisionSelectionRules.Toggle(selected, "second", 2));

        CollectionAssert.AreEquivalent(new[] { "first", "second" }, selected);
    }

    private static GameStateSnapshot StartedState()
    {
        return new GameStateSnapshot { IsStarted = true };
    }
}
#endif
