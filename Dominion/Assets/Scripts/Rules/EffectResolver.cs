using System;
using System.Collections.Generic;

public enum EffectResolutionStatus
{
    Applied,
    WaitingForChoice,
    Rejected
}

public readonly struct EffectResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public string Error { get; }
    public bool Succeeded => Status == EffectResolutionStatus.Applied;

    private EffectResolutionResult(EffectResolutionStatus status, string error)
    {
        Status = status;
        Error = error ?? string.Empty;
    }

    public static EffectResolutionResult Applied() => new EffectResolutionResult(EffectResolutionStatus.Applied, string.Empty);
    public static EffectResolutionResult WaitingForChoice() => new EffectResolutionResult(EffectResolutionStatus.WaitingForChoice, string.Empty);
    public static EffectResolutionResult Rejected(string error) => new EffectResolutionResult(EffectResolutionStatus.Rejected, error);
}

public sealed class EffectExecutionContext
{
    public GameStateSnapshot State { get; }
    public PlayerStateSnapshot Actor { get; }
    public int SourceCardInstanceId { get; }
    public System.Random Random { get; }
    public ResolutionQueue Resolution { get; }
    public GameEventBus EventBus => Resolution != null ? Resolution.Events : null;
    public GameEvent TriggerEvent { get; }
    public string Timing { get; }
    public int ListenerCardInstanceId { get; }
    public int AbilityIndex { get; }
    public int EffectIndex { get; }

    public EffectExecutionContext(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        int sourceCardInstanceId = 0,
        System.Random random = null,
        ResolutionQueue resolution = null,
        GameEvent triggerEvent = null,
        string timing = null,
        int listenerCardInstanceId = 0,
        int abilityIndex = -1,
        int effectIndex = -1)
    {
        State = state;
        Actor = actor;
        SourceCardInstanceId = sourceCardInstanceId;
        Random = random;
        Resolution = resolution;
        TriggerEvent = triggerEvent;
        Timing = timing ?? string.Empty;
        ListenerCardInstanceId = listenerCardInstanceId;
        AbilityIndex = abilityIndex;
        EffectIndex = effectIndex;
    }

    public EffectExecutionContext WithCursor(string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex)
    {
        return new EffectExecutionContext(
            State,
            Actor,
            SourceCardInstanceId,
            Random,
            Resolution,
            TriggerEvent,
            timing,
            listenerCardInstanceId,
            abilityIndex,
            effectIndex);
    }
}

public static class EffectResolver
{
    private delegate EffectResolutionResult EffectHandler(CardEffectData effect, EffectExecutionContext context);

    private static readonly Dictionary<string, EffectHandler> Handlers =
        new Dictionary<string, EffectHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "add_resource", ResolveAddResource },
            { "draw", ResolveDraw },
            { "choose_cards", ResolveChooseCards },
            { "trash_selected", ResolveTrashSelected }
        };

    public static bool IsSupported(string operation)
    {
        return !string.IsNullOrWhiteSpace(operation) && Handlers.ContainsKey(operation);
    }

    public static EffectResolutionResult Resolve(CardEffectData effect, EffectExecutionContext context)
    {
        if (effect == null)
            return EffectResolutionResult.Rejected("Effect is null.");
        if (context == null || context.State == null || context.Actor == null)
            return EffectResolutionResult.Rejected("Effect execution context is incomplete.");
        if (string.IsNullOrWhiteSpace(effect.op))
            return EffectResolutionResult.Rejected("Effect operation is missing.");
        if (!Handlers.TryGetValue(effect.op, out EffectHandler handler))
            return EffectResolutionResult.Rejected("Unsupported effect operation: " + effect.op);

        return handler(effect, context);
    }

    private static EffectResolutionResult ResolveAddResource(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected("add_resource currently supports target 'self' only.");
        if (effect.amount < 0)
            return EffectResolutionResult.Rejected("add_resource amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(effect.resource))
            return EffectResolutionResult.Rejected("add_resource resource is missing.");

        switch (effect.resource.Trim().ToLowerInvariant())
        {
            case "actions": context.Actor.Actions += effect.amount; break;
            case "buys": context.Actor.Buys += effect.amount; break;
            case "coins": context.Actor.Coins += effect.amount; break;
            default: return EffectResolutionResult.Rejected("Unsupported add_resource resource: " + effect.resource);
        }

        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveDraw(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected("draw currently supports target 'self' only.");
        if (effect.amount < 0)
            return EffectResolutionResult.Rejected("draw amount cannot be negative.");
        if (!CardZoneRules.DrawCards(context.Actor, effect.amount, context.Random, out string error))
            return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveChooseCards(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected("choose_cards currently supports target 'self' only.");
        if (context.Resolution == null)
            return EffectResolutionResult.Rejected("choose_cards requires an active ResolutionQueue.");
        if (!string.Equals(effect.zone, "hand", StringComparison.OrdinalIgnoreCase))
            return EffectResolutionResult.Rejected("choose_cards currently supports zone 'hand' only.");
        if (context.AbilityIndex < 0 || context.EffectIndex < 0 || context.ListenerCardInstanceId <= 0)
            return EffectResolutionResult.Rejected("choose_cards is missing its continuation cursor.");
        if (context.TriggerEvent == null || context.TriggerEvent.CardInstanceId != context.ListenerCardInstanceId)
            return EffectResolutionResult.Rejected("choose_cards currently supports subject abilities only.");

        int min = Math.Max(0, effect.min);
        int max = effect.max > 0 ? effect.max : min;
        if (max < min)
            return EffectResolutionResult.Rejected("choose_cards max cannot be lower than min.");

        List<int> candidates = context.Actor.Hand != null ? new List<int>(context.Actor.Hand) : new List<int>();
        max = Math.Min(max, candidates.Count);
        if (min > candidates.Count)
            return EffectResolutionResult.Rejected("choose_cards does not have enough eligible cards for its minimum.");

        if (!context.Resolution.TrySuspendForDecision(
                context.Actor.PlayerId,
                "choose_cards",
                effect.prompt,
                context.SourceCardInstanceId,
                min,
                max,
                candidates,
                context.TriggerEvent,
                context.Timing,
                context.ListenerCardInstanceId,
                context.AbilityIndex,
                context.EffectIndex,
                out string error))
            return EffectResolutionResult.Rejected(error);

        return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult ResolveTrashSelected(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected("trash_selected currently supports target 'self' only.");
        if (context.Resolution == null)
            return EffectResolutionResult.Rejected("trash_selected requires an active ResolutionQueue.");

        List<int> selected = context.Resolution.TakeSelectedInstanceIds();
        foreach (int instanceId in selected)
        {
            if (!TrashRules.TryTrashFromHand(
                    context.State,
                    context.Actor,
                    instanceId,
                    context.SourceCardInstanceId,
                    context.EventBus,
                    out string error))
                return EffectResolutionResult.Rejected(error);
        }

        return EffectResolutionResult.Applied();
    }

    private static bool TargetsSelf(CardEffectData effect)
    {
        return effect != null && string.Equals(effect.target, "self", StringComparison.OrdinalIgnoreCase);
    }
}
