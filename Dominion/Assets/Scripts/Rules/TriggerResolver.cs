using System;

public readonly struct TriggerResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public int EventsProcessed { get; }
    public int AbilitiesMatched { get; }
    public int EffectsResolved { get; }
    public string Error { get; }

    private TriggerResolutionResult(
        EffectResolutionStatus status,
        int eventsProcessed,
        int abilitiesMatched,
        int effectsResolved,
        string error)
    {
        Status = status;
        EventsProcessed = eventsProcessed;
        AbilitiesMatched = abilitiesMatched;
        EffectsResolved = effectsResolved;
        Error = error ?? string.Empty;
    }

    public static TriggerResolutionResult Applied(
        int eventsProcessed,
        int abilitiesMatched,
        int effectsResolved)
    {
        return new TriggerResolutionResult(
            EffectResolutionStatus.Applied,
            eventsProcessed,
            abilitiesMatched,
            effectsResolved,
            string.Empty);
    }

    public static TriggerResolutionResult WaitingForChoice(
        int eventsProcessed,
        int abilitiesMatched,
        int effectsResolved)
    {
        return new TriggerResolutionResult(
            EffectResolutionStatus.WaitingForChoice,
            eventsProcessed,
            abilitiesMatched,
            effectsResolved,
            string.Empty);
    }

    public static TriggerResolutionResult Rejected(
        int eventsProcessed,
        int abilitiesMatched,
        int effectsResolved,
        string error)
    {
        return new TriggerResolutionResult(
            EffectResolutionStatus.Rejected,
            eventsProcessed,
            abilitiesMatched,
            effectsResolved,
            error);
    }
}

/// <summary>
/// Deterministically consumes GameEvents and resolves declarative abilities triggered by
/// them. The first supported trigger source is the card that is the subject of the event
/// itself (played/gained/discarded/trashed). External listeners/reactions can be added here
/// later without changing command, network or effect code.
/// </summary>
public static class TriggerResolver
{
    private const int MaxEventsPerResolution = 512;

    public static TriggerResolutionResult ResolvePending(
        GameEventBus eventBus,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        if (eventBus == null)
            return TriggerResolutionResult.Rejected(0, 0, 0, "Game event bus is missing.");
        if (state == null)
            return TriggerResolutionResult.Rejected(0, 0, 0, "Game state is null.");
        if (resolveCardDefinition == null)
            return TriggerResolutionResult.Rejected(0, 0, 0, "Card definition resolver is missing.");

        int eventsProcessed = 0;
        int abilitiesMatched = 0;
        int effectsResolved = 0;

        while (eventBus.TryTakeNext(out GameEvent gameEvent))
        {
            eventsProcessed++;
            if (eventsProcessed > MaxEventsPerResolution)
            {
                return TriggerResolutionResult.Rejected(
                    eventsProcessed,
                    abilitiesMatched,
                    effectsResolved,
                    "Event resolution limit exceeded. A trigger loop is likely present.");
            }

            AbilityResolutionResult abilityResult = ResolveSubjectAbility(
                gameEvent,
                eventBus,
                state,
                resolveCardDefinition,
                random);

            abilitiesMatched += abilityResult.AbilitiesMatched;
            effectsResolved += abilityResult.EffectsResolved;

            if (abilityResult.Status == EffectResolutionStatus.Rejected)
            {
                return TriggerResolutionResult.Rejected(
                    eventsProcessed,
                    abilitiesMatched,
                    effectsResolved,
                    abilityResult.Error);
            }

            if (abilityResult.Status == EffectResolutionStatus.WaitingForChoice)
            {
                return TriggerResolutionResult.WaitingForChoice(
                    eventsProcessed,
                    abilitiesMatched,
                    effectsResolved);
            }
        }

        return TriggerResolutionResult.Applied(eventsProcessed, abilitiesMatched, effectsResolved);
    }

    private static AbilityResolutionResult ResolveSubjectAbility(
        GameEvent gameEvent,
        GameEventBus eventBus,
        GameStateSnapshot state,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        if (gameEvent == null)
            return AbilityResolutionResult.Applied(0, 0);

        string timing = ResolveTiming(gameEvent.Type);
        if (string.IsNullOrEmpty(timing) || gameEvent.CardInstanceId <= 0)
            return AbilityResolutionResult.Applied(0, 0);

        PlayerStateSnapshot actor = FindPlayer(state, gameEvent.PlayerId);
        if (actor == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger actor was not found for event " + gameEvent.Type + ".");

        CardInstance instance = FindCardInstance(state, gameEvent.CardInstanceId);
        if (instance == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger card instance was not found for event " + gameEvent.Type + ".");

        string definitionId = !string.IsNullOrEmpty(gameEvent.CardDefinitionId)
            ? gameEvent.CardDefinitionId
            : instance.DefinitionId;
        ExtensionCardData definition = resolveCardDefinition(definitionId);
        if (definition == null)
            return AbilityResolutionResult.Rejected(0, 0, "Trigger card definition could not be resolved: " + definitionId);

        return AbilityResolver.ResolveTiming(
            definition,
            timing,
            new EffectExecutionContext(
                state,
                actor,
                gameEvent.SourceCardInstanceId > 0
                    ? gameEvent.SourceCardInstanceId
                    : gameEvent.CardInstanceId,
                random,
                eventBus));
    }

    private static string ResolveTiming(GameEventType eventType)
    {
        switch (eventType)
        {
            // Backwards-compatible with the pilot JSON already using when="play".
            case GameEventType.CardPlayed:
                return "play";
            case GameEventType.CardGained:
                return "card_gained";
            case GameEventType.CardDiscarded:
                return "card_discarded";
            case GameEventType.CardTrashed:
                return "card_trashed";
            case GameEventType.TurnStarted:
                return "turn_started";
            case GameEventType.TurnEnded:
                return "turn_ended";
            case GameEventType.BuyStarted:
                return "buy_started";
            case GameEventType.PileEmptied:
                return "pile_emptied";
            case GameEventType.ArtifactGained:
                return "artifact_gained";
            case GameEventType.DiseaseGained:
                return "disease_gained";
            default:
                return string.Empty;
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
