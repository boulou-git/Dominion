using System;
using System.Collections.Generic;

public enum EffectResolutionStatus { Applied, WaitingForChoice, Rejected }

public readonly struct EffectResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public string Error { get; }
    public bool Succeeded => Status == EffectResolutionStatus.Applied;
    private EffectResolutionResult(EffectResolutionStatus status, string error) { Status = status; Error = error ?? string.Empty; }
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

    public EffectExecutionContext(GameStateSnapshot state, PlayerStateSnapshot actor, int sourceCardInstanceId = 0,
        System.Random random = null, ResolutionQueue resolution = null, GameEvent triggerEvent = null,
        string timing = null, int listenerCardInstanceId = 0, int abilityIndex = -1, int effectIndex = -1)
    {
        State = state; Actor = actor; SourceCardInstanceId = sourceCardInstanceId; Random = random;
        Resolution = resolution; TriggerEvent = triggerEvent; Timing = timing ?? string.Empty;
        ListenerCardInstanceId = listenerCardInstanceId; AbilityIndex = abilityIndex; EffectIndex = effectIndex;
    }

    public EffectExecutionContext WithCursor(string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex)
    {
        return new EffectExecutionContext(State, Actor, SourceCardInstanceId, Random, Resolution, TriggerEvent,
            timing, listenerCardInstanceId, abilityIndex, effectIndex);
    }
}

public static class EffectResolver
{
    private delegate EffectResolutionResult EffectHandler(CardEffectData effect, EffectExecutionContext context);
    private static readonly Dictionary<string, EffectHandler> Handlers = new Dictionary<string, EffectHandler>(StringComparer.OrdinalIgnoreCase)
    {
        { "add_resource", ResolveAddResource }, { "add_resource_per_last_selection", ResolveAddResourcePerLastSelection },
        { "draw", ResolveDraw }, { "draw_last_selection_count", ResolveDrawLastSelectionCount },
        { "choose_cards", ResolveChooseCards }, { "remember_selected_card_cost", ResolveRememberSelectedCardCost },
        { "trash_selected", ResolveTrashSelected }, { "discard_selected", ResolveDiscardSelected },
        { "discard_others_down_to", ResolveDiscardOthersDownTo },
        { "move_selected", ResolveMoveSelected }, { "choose_supply", ResolveChooseSupply },
        { "gain_selected_supply", ResolveGainSelectedSupply }
    };

    public static bool IsSupported(string operation) => !string.IsNullOrWhiteSpace(operation) && Handlers.ContainsKey(operation);

    public static EffectResolutionResult Resolve(CardEffectData effect, EffectExecutionContext context)
    {
        if (effect == null) return EffectResolutionResult.Rejected("Effect is null.");
        if (context == null || context.State == null || context.Actor == null) return EffectResolutionResult.Rejected("Effect execution context is incomplete.");
        if (string.IsNullOrWhiteSpace(effect.op)) return EffectResolutionResult.Rejected("Effect operation is missing.");
        if (effect.requiresLastSelection)
        {
            if (context.Resolution == null) return EffectResolutionResult.Rejected("Conditional effect requires an active ResolutionQueue.");
            if (context.Resolution.LastSelectionCount <= 0) return EffectResolutionResult.Applied();
        }
        if (!Handlers.TryGetValue(effect.op, out EffectHandler handler)) return EffectResolutionResult.Rejected("Unsupported effect operation: " + effect.op);
        return handler(effect, context);
    }

    private static EffectResolutionResult ResolveAddResource(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("add_resource currently supports target 'self' only.");
        if (effect.amount < 0) return EffectResolutionResult.Rejected("add_resource amount cannot be negative.");
        return AddResource(context.Actor, effect.resource, effect.amount);
    }

    private static EffectResolutionResult ResolveAddResourcePerLastSelection(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("add_resource_per_last_selection currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("add_resource_per_last_selection requires an active ResolutionQueue.");
        if (effect.amount < 0) return EffectResolutionResult.Rejected("add_resource_per_last_selection amount cannot be negative.");
        return AddResource(context.Actor, effect.resource, effect.amount * context.Resolution.LastSelectionCount);
    }

    private static EffectResolutionResult AddResource(PlayerStateSnapshot actor, string resource, int amount)
    {
        if (actor == null) return EffectResolutionResult.Rejected("Resource actor is missing.");
        if (string.IsNullOrWhiteSpace(resource)) return EffectResolutionResult.Rejected("Resource name is missing.");
        switch (resource.Trim().ToLowerInvariant())
        {
            case "actions": actor.Actions += amount; break;
            case "buys": actor.Buys += amount; break;
            case "coins": actor.Coins += amount; break;
            default: return EffectResolutionResult.Rejected("Unsupported resource: " + resource);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveDraw(CardEffectData effect, EffectExecutionContext context)
    {
        if (effect.amount < 0) return EffectResolutionResult.Rejected("draw amount cannot be negative.");
        if (TargetsSelf(effect))
            return CardZoneRules.DrawCards(context.Actor, effect.amount, context.Random, out string selfError)
                ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(selfError);
        if (!TargetsOthers(effect)) return EffectResolutionResult.Rejected("draw supports targets 'self' and 'others'.");
        if (context.State.Players == null) return EffectResolutionResult.Applied();

        foreach (PlayerStateSnapshot player in context.State.Players)
        {
            if (player == null || string.Equals(player.PlayerId, context.Actor.PlayerId, StringComparison.Ordinal)) continue;
            if (!CardZoneRules.DrawCards(player, effect.amount, context.Random, out string error))
                return EffectResolutionResult.Rejected(error);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveDrawLastSelectionCount(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("draw_last_selection_count currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("draw_last_selection_count requires an active ResolutionQueue.");
        return CardZoneRules.DrawCards(context.Actor, context.Resolution.LastSelectionCount, context.Random, out string error)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult ResolveChooseCards(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("choose_cards currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("choose_cards requires an active ResolutionQueue.");
        if (!CardZoneRules.TryParseZone(effect.zone, out CardZone choiceZone) || (choiceZone != CardZone.Hand && choiceZone != CardZone.Discard))
            return EffectResolutionResult.Rejected("choose_cards currently supports zones 'hand' and 'discard'.");
        if (!HasDecisionCursor(context)) return EffectResolutionResult.Rejected("choose_cards is missing its continuation cursor.");

        int min = Math.Max(0, effect.min);
        int max = effect.max > 0 ? effect.max : min;
        if (max < min) return EffectResolutionResult.Rejected("choose_cards max cannot be lower than min.");
        List<int> source = CardZoneRules.ResolveZone(context.Actor, choiceZone);
        List<int> candidates = new List<int>();
        if (source != null)
        {
            foreach (int instanceId in source)
            {
                CardInstance instance = FindCardInstance(context.State, instanceId);
                if (instance == null) continue;
                if (!string.IsNullOrWhiteSpace(effect.cardId) &&
                    !string.Equals(instance.DefinitionId, effect.cardId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(effect.cardType))
                {
                    ExtensionCardData definition = ResolveDefinition(instance.DefinitionId);
                    if (!CardDefinitionRules.HasType(definition, effect.cardType)) continue;
                }
                candidates.Add(instanceId);
            }
        }

        max = Math.Min(max, candidates.Count);
        if (min > candidates.Count) return EffectResolutionResult.Rejected("choose_cards does not have enough eligible cards for its minimum.");
        if (candidates.Count == 0 && min == 0) return EffectResolutionResult.Applied();

        if (!context.Resolution.TrySuspendForDecision(context.Actor.PlayerId, "choose_cards", effect.zone, effect.prompt,
                context.SourceCardInstanceId, min, max, candidates, context.TriggerEvent, context.Timing,
                context.ListenerCardInstanceId, context.AbilityIndex, context.EffectIndex, out string error))
            return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult ResolveRememberSelectedCardCost(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("remember_selected_card_cost currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("remember_selected_card_cost requires an active ResolutionQueue.");
        if (context.Resolution.SelectedInstanceIds.Count == 0)
        {
            context.Resolution.SetLastSelectedCardCost(-1);
            return EffectResolutionResult.Applied();
        }
        if (context.Resolution.SelectedInstanceIds.Count != 1)
            return EffectResolutionResult.Rejected("remember_selected_card_cost requires at most one selected card.");

        CardInstance instance = FindCardInstance(context.State, context.Resolution.SelectedInstanceIds[0]);
        if (instance == null) return EffectResolutionResult.Rejected("Selected card instance could not be resolved.");
        ExtensionCardData definition = ResolveDefinition(instance.DefinitionId);
        if (definition == null) return EffectResolutionResult.Rejected("Selected card definition could not be resolved: " + instance.DefinitionId);
        if (definition.cost < 0) return EffectResolutionResult.Rejected("Selected card has an invalid negative cost.");

        context.Resolution.SetLastSelectedCardCost(definition.cost);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveChooseSupply(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("choose_supply currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("choose_supply requires an active ResolutionQueue.");
        if (!HasDecisionCursor(context)) return EffectResolutionResult.Rejected("choose_supply is missing its continuation cursor.");

        int min = Math.Max(0, effect.min);
        int max = effect.max > 0 ? effect.max : Math.Max(1, min);
        int costCeiling = effect.maxCost;
        if (effect.useLastSelectionCost)
        {
            if (context.Resolution.LastSelectedCardCost < 0)
                return min == 0
                    ? EffectResolutionResult.Applied()
                    : EffectResolutionResult.Rejected("choose_supply requires a remembered selected-card cost.");
            int dynamicCeiling = context.Resolution.LastSelectedCardCost + effect.costOffset;
            costCeiling = costCeiling >= 0 ? Math.Min(costCeiling, dynamicCeiling) : dynamicCeiling;
        }

        List<string> candidates = new List<string>();
        if (context.State.SupplyPiles != null)
        {
            foreach (SupplyPileSnapshot pile in context.State.SupplyPiles)
            {
                if (pile == null || pile.RemainingCount <= 0 || string.IsNullOrEmpty(pile.DefinitionId)) continue;
                ExtensionCardData definition = ResolveDefinition(pile.DefinitionId);
                if (definition == null) continue;
                if (costCeiling >= 0 && definition.cost > costCeiling) continue;
                if (!string.IsNullOrWhiteSpace(effect.cardId) &&
                    !string.Equals(pile.DefinitionId, effect.cardId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(effect.cardType) && !CardDefinitionRules.HasType(definition, effect.cardType)) continue;
                candidates.Add(pile.DefinitionId);
            }
        }

        max = Math.Min(max, candidates.Count);
        if (candidates.Count == 0)
            return (min == 0 || effect.allowNoEligible)
                ? EffectResolutionResult.Applied()
                : EffectResolutionResult.Rejected("choose_supply has no eligible pile for its required choice.");
        if (min > candidates.Count) return EffectResolutionResult.Rejected("choose_supply does not have enough eligible piles for its minimum.");

        if (!context.Resolution.TrySuspendForSupplyDecision(context.Actor.PlayerId, "choose_supply", effect.prompt,
                context.SourceCardInstanceId, min, max, candidates, context.TriggerEvent, context.Timing,
                context.ListenerCardInstanceId, context.AbilityIndex, context.EffectIndex, out string error))
            return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult ResolveTrashSelected(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("trash_selected currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("trash_selected requires an active ResolutionQueue.");
        List<int> selected = context.Resolution.TakeSelectedInstanceIds();
        foreach (int instanceId in selected)
            if (!TrashRules.TryTrashFromHand(context.State, context.Actor, instanceId, context.SourceCardInstanceId, context.EventBus, out string error))
                return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveDiscardSelected(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("discard_selected currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("discard_selected requires an active ResolutionQueue.");
        List<int> selected = context.Resolution.TakeSelectedInstanceIds();
        return DiscardRules.TryDiscardSelectedFromHand(context.State, context.Actor, selected, context.SourceCardInstanceId,
            context.EventBus, out string error) ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult ResolveDiscardOthersDownTo(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsOthers(effect)) return EffectResolutionResult.Rejected("discard_others_down_to requires target 'others'.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("discard_others_down_to requires an active ResolutionQueue.");
        if (!HasDecisionCursor(context)) return EffectResolutionResult.Rejected("discard_others_down_to is missing its continuation cursor.");
        if (effect.amount < 0) return EffectResolutionResult.Rejected("discard_others_down_to target hand size cannot be negative.");
        if (context.State.Players == null || context.State.Players.Count <= 1) return EffectResolutionResult.Applied();

        List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>();
        foreach (PlayerStateSnapshot player in context.State.Players)
        {
            if (player == null || string.Equals(player.PlayerId, context.Actor.PlayerId, StringComparison.Ordinal)) continue;
            if (player.Hand != null && player.Hand.Count > effect.amount) targets.Add(player);
        }
        if (targets.Count == 0) return EffectResolutionResult.Applied();

        PlayerStateSnapshot first = targets[0];
        List<string> remaining = new List<string>();
        for (int i = 1; i < targets.Count; i++) remaining.Add(targets[i].PlayerId);

        if (!context.Resolution.TrySuspendForDiscardDownDecision(
                first.PlayerId,
                string.IsNullOrWhiteSpace(effect.prompt) ? "Défaussez jusqu’à la taille de main demandée." : effect.prompt,
                context.SourceCardInstanceId,
                effect.amount,
                first.Hand,
                remaining,
                context.TriggerEvent,
                context.Timing,
                context.ListenerCardInstanceId,
                context.AbilityIndex,
                context.EffectIndex,
                out string error))
            return EffectResolutionResult.Rejected(error);

        return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult ResolveMoveSelected(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("move_selected currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("move_selected requires an active ResolutionQueue.");
        if (!CardZoneRules.TryParseZone(effect.sourceZone, out CardZone source) || !CardZoneRules.TryParseZone(effect.destinationZone, out CardZone destination))
            return EffectResolutionResult.Rejected("move_selected requires valid sourceZone and destinationZone.");
        if (source == destination) return EffectResolutionResult.Rejected("move_selected source and destination cannot match.");
        List<int> selected = context.Resolution.TakeSelectedInstanceIds();
        foreach (int instanceId in selected)
            if (!CardZoneRules.MoveCard(context.Actor, source, destination, instanceId))
                return EffectResolutionResult.Rejected("Selected card could not be moved from " + source + " to " + destination + ".");
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveGainSelectedSupply(CardEffectData effect, EffectExecutionContext context)
    {
        if (!TargetsSelf(effect)) return EffectResolutionResult.Rejected("gain_selected_supply currently supports target 'self' only.");
        if (context.Resolution == null) return EffectResolutionResult.Rejected("gain_selected_supply requires an active ResolutionQueue.");
        CardZone destination = CardZone.Discard;
        if (!string.IsNullOrWhiteSpace(effect.destinationZone) && !CardZoneRules.TryParseZone(effect.destinationZone, out destination))
            return EffectResolutionResult.Rejected("gain_selected_supply destinationZone is invalid.");

        List<string> selected = context.Resolution.TakeSelectedDefinitionIds();
        foreach (string definitionId in selected)
            if (!GainRules.TryGainFromSupply(context.State, context.Actor, definitionId, destination, context.SourceCardInstanceId,
                    context.EventBus, out _, out string error))
                return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.Applied();
    }

    private static bool HasDecisionCursor(EffectExecutionContext context)
    {
        return context.AbilityIndex >= 0 && context.EffectIndex >= 0 && context.ListenerCardInstanceId > 0 &&
               context.TriggerEvent != null && context.TriggerEvent.CardInstanceId == context.ListenerCardInstanceId;
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null || instanceId <= 0) return null;
        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }

    private static ExtensionCardData ResolveDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return null;
        int separator = definitionId.IndexOf(':');
        if (separator <= 0 || separator >= definitionId.Length - 1) return null;
        return ExtensionCatalog.FindCard(definitionId.Substring(0, separator), definitionId.Substring(separator + 1));
    }

    private static bool TargetsSelf(CardEffectData effect) => effect != null && string.Equals(effect.target, "self", StringComparison.OrdinalIgnoreCase);
    private static bool TargetsOthers(CardEffectData effect) => effect != null && string.Equals(effect.target, "others", StringComparison.OrdinalIgnoreCase);
}
