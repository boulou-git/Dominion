using System;
using System.Collections.Generic;

public readonly struct TriggerResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public int EventsProcessed { get; }
    public int AbilitiesMatched { get; }
    public int EffectsResolved { get; }
    public string Error { get; }

    private TriggerResolutionResult(EffectResolutionStatus status, int eventsProcessed, int abilitiesMatched, int effectsResolved, string error)
    {
        Status = status;
        EventsProcessed = eventsProcessed;
        AbilitiesMatched = abilitiesMatched;
        EffectsResolved = effectsResolved;
        Error = error ?? string.Empty;
    }

    public static TriggerResolutionResult Applied(int eventsProcessed, int abilitiesMatched, int effectsResolved) =>
        new TriggerResolutionResult(EffectResolutionStatus.Applied, eventsProcessed, abilitiesMatched, effectsResolved, string.Empty);

    public static TriggerResolutionResult WaitingForChoice(int eventsProcessed, int abilitiesMatched, int effectsResolved) =>
        new TriggerResolutionResult(EffectResolutionStatus.WaitingForChoice, eventsProcessed, abilitiesMatched, effectsResolved, string.Empty);

    public static TriggerResolutionResult Rejected(int eventsProcessed, int abilitiesMatched, int effectsResolved, string error) =>
        new TriggerResolutionResult(EffectResolutionStatus.Rejected, eventsProcessed, abilitiesMatched, effectsResolved, error);
}

public static class TriggerResolver
{
    private const int MaxEventsPerResolution = 512;

    public static TriggerResolutionResult ResolvePending(
        ResolutionQueue resolution,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        if (resolution == null)
            return RejectAndAbort(state, 0, 0, 0, "Resolution queue is missing.");
        if (state == null)
            return RejectAndAbort(state, 0, 0, 0, "Game state is null.");
        if (resolveCardDefinition == null)
            return RejectAndAbort(state, 0, 0, 0, "Card definition resolver is missing.");

        int eventsProcessed = 0;
        int abilitiesMatched = 0;
        int effectsResolved = 0;

        while (resolution.Events.TryTakeNext(out GameEvent gameEvent))
        {
            eventsProcessed++;
            if (eventsProcessed > MaxEventsPerResolution)
                return RejectAndAbort(state, eventsProcessed, abilitiesMatched, effectsResolved, "Event resolution limit exceeded. A trigger loop is likely present.");

            string timing = ResolveTiming(gameEvent != null ? gameEvent.Type : default);
            if (string.IsNullOrEmpty(timing))
                continue;

            AbilityResolutionResult subjectResult = ResolveSubjectAbility(gameEvent, timing, resolution, state, resolveCardDefinition, random);
            abilitiesMatched += subjectResult.AbilitiesMatched;
            effectsResolved += subjectResult.EffectsResolved;
            if (subjectResult.Status == EffectResolutionStatus.Rejected)
                return RejectAndAbort(state, eventsProcessed, abilitiesMatched, effectsResolved, subjectResult.Error);
            if (subjectResult.Status == EffectResolutionStatus.WaitingForChoice)
                return TriggerResolutionResult.WaitingForChoice(eventsProcessed, abilitiesMatched, effectsResolved);

            AbilityResolutionResult listenerResult = ResolveExternalListeners(gameEvent, timing, resolution, state, resolveCardDefinition, random);
            abilitiesMatched += listenerResult.AbilitiesMatched;
            effectsResolved += listenerResult.EffectsResolved;
            if (listenerResult.Status == EffectResolutionStatus.Rejected)
                return RejectAndAbort(state, eventsProcessed, abilitiesMatched, effectsResolved, listenerResult.Error);
            if (listenerResult.Status == EffectResolutionStatus.WaitingForChoice)
                return TriggerResolutionResult.WaitingForChoice(eventsProcessed, abilitiesMatched, effectsResolved);
        }

        return TriggerResolutionResult.Applied(eventsProcessed, abilitiesMatched, effectsResolved);
    }

    public static TriggerResolutionResult ResumeSubjectDecision(
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        if (resolution == null || continuation == null || continuation.TriggerEvent == null)
            return RejectAndAbort(state, 0, 0, 0, "Decision continuation is incomplete.");
        if (!continuation.TriggerEvent.TryToRuntime(out GameEvent gameEvent))
            return RejectAndAbort(state, 0, 0, 0, "Decision trigger event is invalid.");
        if (continuation.ListenerCardInstanceId <= 0 || continuation.ListenerCardInstanceId != gameEvent.CardInstanceId)
            return RejectAndAbort(state, 0, 0, 0, "Only subject-card decision continuation is supported currently.");

        CardInstance instance = FindCardInstance(state, continuation.ListenerCardInstanceId);
        if (instance == null)
            return RejectAndAbort(state, 0, 0, 0, "Decision source card instance was not found.");
        PlayerStateSnapshot owner = FindPlayer(state, instance.OwnerPlayerId);
        if (owner == null)
            return RejectAndAbort(state, 0, 0, 0, "Decision source card owner was not found.");
        ExtensionCardData definition = resolveCardDefinition(instance.DefinitionId);
        if (definition == null)
            return RejectAndAbort(state, 0, 0, 0, "Decision source card definition was not found.");

        AbilityResolutionResult resumed = AbilityResolver.ResolveTimingFromCursor(
            definition,
            continuation.Timing,
            BuildContext(state, owner, instance.InstanceId, random, resolution, gameEvent),
            ability => IsSubjectScope(ability) && FilterMatches(ability != null ? ability.filter : null, gameEvent, owner, resolveCardDefinition),
            continuation.AbilityIndex,
            continuation.EffectIndex + 1);

        if (resumed.Status == EffectResolutionStatus.Rejected)
            return RejectAndAbort(state, 0, resumed.AbilitiesMatched, resumed.EffectsResolved, resumed.Error);
        if (resumed.Status == EffectResolutionStatus.WaitingForChoice)
            return TriggerResolutionResult.WaitingForChoice(0, resumed.AbilitiesMatched, resumed.EffectsResolved);

        AbilityResolutionResult listeners = ResolveExternalListeners(
            gameEvent,
            continuation.Timing,
            resolution,
            state,
            resolveCardDefinition,
            random);

        int matched = resumed.AbilitiesMatched + listeners.AbilitiesMatched;
        int resolved = resumed.EffectsResolved + listeners.EffectsResolved;
        if (listeners.Status == EffectResolutionStatus.Rejected)
            return RejectAndAbort(state, 0, matched, resolved, listeners.Error);
        if (listeners.Status == EffectResolutionStatus.WaitingForChoice)
            return TriggerResolutionResult.WaitingForChoice(0, matched, resolved);

        TriggerResolutionResult remaining = ResolvePending(resolution, state, resolveCardDefinition, random);
        matched += remaining.AbilitiesMatched;
        resolved += remaining.EffectsResolved;
        if (remaining.Status == EffectResolutionStatus.Rejected)
            return TriggerResolutionResult.Rejected(remaining.EventsProcessed, matched, resolved, remaining.Error);
        if (remaining.Status == EffectResolutionStatus.WaitingForChoice)
            return TriggerResolutionResult.WaitingForChoice(remaining.EventsProcessed, matched, resolved);

        return TriggerResolutionResult.Applied(remaining.EventsProcessed, matched, resolved);
    }

    private static TriggerResolutionResult RejectAndAbort(
        GameStateSnapshot state,
        int eventsProcessed,
        int abilitiesMatched,
        int effectsResolved,
        string error)
    {
        AbortResolution(state);
        return TriggerResolutionResult.Rejected(eventsProcessed, abilitiesMatched, effectsResolved, error);
    }

    private static void AbortResolution(GameStateSnapshot state)
    {
        if (state == null || state.Resolution == null)
            return;

        ResolutionQueue.EnsureSnapshot(state);
        state.Resolution.IsActive = false;
        state.Resolution.OwnerPlayerId = string.Empty;
        state.Resolution.PendingEvents.Clear();
        state.Resolution.PendingDecision.Clear();
        state.Resolution.SelectedInstanceIds.Clear();
        state.Resolution.SelectedDefinitionIds.Clear();
        state.Resolution.AttackProtectedPlayerIds.Clear();
        state.Resolution.LastSelectionCount = 0;
        state.Resolution.LastSelectedCardCost = -1;
        state.Resolution.LastMovedCardInstanceId = 0;
    }

    private static AbilityResolutionResult ResolveSubjectAbility(
        GameEvent gameEvent,
        string timing,
        ResolutionQueue resolution,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        if (gameEvent == null || gameEvent.CardInstanceId <= 0)
            return AbilityResolutionResult.Applied(0, 0);

        CardInstance instance = FindCardInstance(state, gameEvent.CardInstanceId);
        if (instance == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger card instance was not found for event " + gameEvent.Type + ".");
        PlayerStateSnapshot owner = FindPlayer(state, instance.OwnerPlayerId);
        if (owner == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger card owner was not found for event " + gameEvent.Type + ".");

        string definitionId = !string.IsNullOrEmpty(gameEvent.CardDefinitionId) ? gameEvent.CardDefinitionId : instance.DefinitionId;
        ExtensionCardData definition = resolveCardDefinition(definitionId);
        if (definition == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger card definition could not be resolved: " + definitionId);

        return AbilityResolver.ResolveTiming(
            definition,
            timing,
            BuildContext(state, owner, instance.InstanceId, random, resolution, gameEvent),
            ability => IsSubjectScope(ability) && FilterMatches(ability != null ? ability.filter : null, gameEvent, owner, resolveCardDefinition));
    }

    private static AbilityResolutionResult ResolveExternalListeners(
        GameEvent gameEvent,
        string timing,
        ResolutionQueue resolution,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        int matched = 0;
        int resolved = 0;
        if (gameEvent == null || state.Players == null)
            return AbilityResolutionResult.Applied(0, 0);

        foreach (PlayerStateSnapshot owner in state.Players)
        {
            if (owner == null)
                continue;

            AbilityResolutionResult handResult = ResolveZoneListeners(owner, owner.Hand, DeclarativeRuleVocabulary.InHandScope, gameEvent, timing, resolution, state, resolveCardDefinition, random);
            matched += handResult.AbilitiesMatched;
            resolved += handResult.EffectsResolved;
            if (handResult.Status == EffectResolutionStatus.Rejected)
                return AbilityResolutionResult.Rejected(matched, resolved, handResult.Error);
            if (handResult.Status == EffectResolutionStatus.WaitingForChoice)
                return AbilityResolutionResult.WaitingForChoice(matched, resolved);

            AbilityResolutionResult inPlayResult = ResolveZoneListeners(owner, owner.InPlay, DeclarativeRuleVocabulary.InPlayScope, gameEvent, timing, resolution, state, resolveCardDefinition, random);
            matched += inPlayResult.AbilitiesMatched;
            resolved += inPlayResult.EffectsResolved;
            if (inPlayResult.Status == EffectResolutionStatus.Rejected)
                return AbilityResolutionResult.Rejected(matched, resolved, inPlayResult.Error);
            if (inPlayResult.Status == EffectResolutionStatus.WaitingForChoice)
                return AbilityResolutionResult.WaitingForChoice(matched, resolved);
        }

        return AbilityResolutionResult.Applied(matched, resolved);
    }

    private static AbilityResolutionResult ResolveZoneListeners(
        PlayerStateSnapshot owner,
        List<int> zone,
        string requiredScope,
        GameEvent gameEvent,
        string timing,
        ResolutionQueue resolution,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        int matched = 0;
        int resolved = 0;
        if (zone == null || zone.Count == 0)
            return AbilityResolutionResult.Applied(0, 0);

        int[] listenerIds = zone.ToArray();
        foreach (int instanceId in listenerIds)
        {
            if (!zone.Contains(instanceId))
                continue;

            CardInstance instance = FindCardInstance(state, instanceId);
            if (instance == null)
                return AbilityResolutionResult.Rejected(matched, resolved, "Listener card instance was not found: " + instanceId);
            ExtensionCardData definition = resolveCardDefinition(instance.DefinitionId);
            if (definition == null)
                return AbilityResolutionResult.Rejected(matched, resolved, "Listener card definition could not be resolved: " + instance.DefinitionId);

            AbilityResolutionResult result = AbilityResolver.ResolveTiming(
                definition,
                timing,
                BuildContext(state, owner, instanceId, random, resolution, gameEvent),
                ability => ScopeEquals(ability, requiredScope) && FilterMatches(ability != null ? ability.filter : null, gameEvent, owner, resolveCardDefinition));

            matched += result.AbilitiesMatched;
            resolved += result.EffectsResolved;
            if (result.Status == EffectResolutionStatus.Rejected)
                return AbilityResolutionResult.Rejected(matched, resolved, result.Error);
            if (result.Status == EffectResolutionStatus.WaitingForChoice)
                return AbilityResolutionResult.WaitingForChoice(matched, resolved);
        }

        return AbilityResolutionResult.Applied(matched, resolved);
    }

    private static EffectExecutionContext BuildContext(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        int sourceCardInstanceId,
        System.Random random,
        ResolutionQueue resolution,
        GameEvent gameEvent)
    {
        return new EffectExecutionContext(state, owner, sourceCardInstanceId, random, resolution, gameEvent);
    }

    private static bool IsSubjectScope(CardAbilityData ability)
    {
        return ability != null && (string.IsNullOrWhiteSpace(ability.scope) || string.Equals(ability.scope, DeclarativeRuleVocabulary.SubjectScope, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ScopeEquals(CardAbilityData ability, string scope)
    {
        return ability != null && !string.IsNullOrWhiteSpace(ability.scope) && string.Equals(ability.scope, scope, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FilterMatches(
        CardTriggerFilterData filter,
        GameEvent gameEvent,
        PlayerStateSnapshot listenerOwner,
        Func<string, ExtensionCardData> resolveCardDefinition)
    {
        if (filter == null)
            return true;

        string eventPlayer = string.IsNullOrWhiteSpace(filter.eventPlayer)
            ? DeclarativeRuleVocabulary.AnyEventPlayer
            : filter.eventPlayer.Trim();
        if (string.Equals(eventPlayer, DeclarativeRuleVocabulary.SelfEventPlayer, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(gameEvent.PlayerId) || !string.Equals(gameEvent.PlayerId, listenerOwner.PlayerId, StringComparison.Ordinal))
                return false;
        }
        else if (string.Equals(eventPlayer, DeclarativeRuleVocabulary.OtherEventPlayer, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(gameEvent.PlayerId) || string.Equals(gameEvent.PlayerId, listenerOwner.PlayerId, StringComparison.Ordinal))
                return false;
        }
        else if (!string.Equals(eventPlayer, DeclarativeRuleVocabulary.AnyEventPlayer, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(filter.cardId) && !CardDefinitionReference.Matches(filter.cardId, gameEvent.CardDefinitionId))
            return false;

        if (!string.IsNullOrWhiteSpace(filter.cardType))
        {
            if (string.IsNullOrEmpty(gameEvent.CardDefinitionId))
                return false;
            ExtensionCardData eventCard = resolveCardDefinition(gameEvent.CardDefinitionId);
            if (!CardDefinitionRules.HasType(eventCard, filter.cardType))
                return false;
        }

        return true;
    }

    private static string ResolveTiming(GameEventType eventType)
    {
        switch (eventType)
        {
            case GameEventType.CardPlayed: return DeclarativeRuleVocabulary.PlayTiming;
            case GameEventType.CardGained: return DeclarativeRuleVocabulary.CardGainedTiming;
            case GameEventType.CardDiscarded: return DeclarativeRuleVocabulary.CardDiscardedTiming;
            case GameEventType.CardTrashed: return DeclarativeRuleVocabulary.CardTrashedTiming;
            case GameEventType.TurnStarted: return DeclarativeRuleVocabulary.TurnStartedTiming;
            case GameEventType.TurnEnded: return DeclarativeRuleVocabulary.TurnEndedTiming;
            case GameEventType.BuyStarted: return DeclarativeRuleVocabulary.BuyStartedTiming;
            case GameEventType.PileEmptied: return DeclarativeRuleVocabulary.PileEmptiedTiming;
            case GameEventType.ArtifactGained: return DeclarativeRuleVocabulary.ArtifactGainedTiming;
            case GameEventType.DiseaseGained: return DeclarativeRuleVocabulary.DiseaseGainedTiming;
            default: return string.Empty;
        }
    }

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId)
    {
        if (state == null || state.Players == null || string.IsNullOrEmpty(playerId))
            return null;
        return state.Players.Find(player => player != null && player.PlayerId == playerId);
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null || instanceId <= 0)
            return null;
        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }
}
