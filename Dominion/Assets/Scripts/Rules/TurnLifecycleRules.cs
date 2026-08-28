using System;
using System.Collections.Generic;

/// <summary>Publishes deterministic turn-boundary events through the normal rules queue.</summary>
public static class TurnLifecycleRules
{
    public static GameRuleResult TryResolveTurnEnded(GameStateSnapshot state, PlayerStateSnapshot player,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (state == null || player == null || string.IsNullOrEmpty(player.PlayerId) || resolve == null)
            return GameRuleResult.Rejected("End-of-turn resolution is incomplete.");
        if (!string.Equals(state.ActivePlayerId, player.PlayerId, StringComparison.Ordinal))
            return GameRuleResult.Rejected("End-of-turn player is not active.");
        if (!ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError))
            return GameRuleResult.Rejected(beginError);
        queue.Events.Publish(GameEvent.TurnEnded(player.PlayerId));
        TriggerResolutionResult resolved = TriggerResolver.ResolvePending(queue, state, resolve, random);
        List<GameEvent> events = queue.Events.SnapshotHistory();
        if (resolved.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected(resolved.Error, events);
        if (resolved.Status == EffectResolutionStatus.WaitingForChoice)
            return queue.IsWaitingForDecision ? GameRuleResult.WaitingForChoice(events) :
                GameRuleResult.Rejected("End-of-turn trigger is waiting without a durable decision.", events);
        queue.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }

    public static GameRuleResult TryResolveTurnStarted(GameStateSnapshot state, PlayerStateSnapshot player,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (state == null || player == null || string.IsNullOrEmpty(player.PlayerId) || resolve == null)
            return GameRuleResult.Rejected("Start-of-turn resolution is incomplete.");
        if (!string.Equals(state.ActivePlayerId, player.PlayerId, StringComparison.Ordinal))
            return GameRuleResult.Rejected("Start-of-turn player is not active.");
        if (!ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError))
            return GameRuleResult.Rejected(beginError);

        if (!SetAsideRules.TryResolveTurnStart(state, player, queue, resolve, out string setAsideError))
            return GameRuleResult.Rejected(setAsideError, queue.Events.SnapshotHistory());

        queue.Events.Publish(GameEvent.TurnStarted(player.PlayerId));
        TriggerResolutionResult resolved = TriggerResolver.ResolvePending(queue, state, resolve, random);
        List<GameEvent> events = queue.Events.SnapshotHistory();
        if (resolved.Status == EffectResolutionStatus.Rejected)
            return GameRuleResult.Rejected(resolved.Error, events);
        if (resolved.Status == EffectResolutionStatus.WaitingForChoice)
            return queue.IsWaitingForDecision
                ? GameRuleResult.WaitingForChoice(events)
                : GameRuleResult.Rejected("Start-of-turn trigger is waiting without a durable decision.", events);
        queue.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }
}
