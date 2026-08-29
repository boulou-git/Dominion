using System;
using System.Collections.Generic;

public enum EffectResolutionStatus { Applied, WaitingForChoice, Rejected }

public readonly struct EffectResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public string Error { get; }
    public bool Succeeded => Status == EffectResolutionStatus.Applied;
    private EffectResolutionResult(EffectResolutionStatus s, string e) { Status = s; Error = e ?? string.Empty; }
    public static EffectResolutionResult Applied() => new EffectResolutionResult(EffectResolutionStatus.Applied, string.Empty);
    public static EffectResolutionResult WaitingForChoice() => new EffectResolutionResult(EffectResolutionStatus.WaitingForChoice, string.Empty);
    public static EffectResolutionResult Rejected(string e) => new EffectResolutionResult(EffectResolutionStatus.Rejected, e);
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
        State = state; Actor = actor; SourceCardInstanceId = sourceCardInstanceId; Random = random; Resolution = resolution;
        TriggerEvent = triggerEvent; Timing = timing ?? string.Empty; ListenerCardInstanceId = listenerCardInstanceId;
        AbilityIndex = abilityIndex; EffectIndex = effectIndex;
    }
    public EffectExecutionContext WithCursor(string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex) =>
        new EffectExecutionContext(State, Actor, SourceCardInstanceId, Random, Resolution, TriggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex);
}

public static class EffectResolver
{
    private const int MaximumGenericOptionCount = 4;
    private delegate EffectResolutionResult Handler(CardEffectData e, EffectExecutionContext c);
    private static readonly Dictionary<string, Handler> H = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
    {
        {"add_resource", AddResource}, {"add_resource_per_last_selection", AddResourcePerSelection}, {"reduce_costs_this_turn", ReduceCostsThisTurn},
        {"draw", Draw}, {"draw_last_selection_count", DrawSelectionCount}, {"draw_to_hand_size", DrawToHandSize}, {"draw_to_hand_size_skipping_type", DrawToHandSizeSkippingType},
        {"choose_cards", ChooseCards}, {"choose_cards_per_empty_pile", ChoosePerEmptyPile}, {"choose_options", ChooseOptions},
        {"choose_options_repeated_per_empty_kingdom_pile", ChooseOptionsRepeatedPerEmptyKingdomPile},
        {"choose_options_per_selected_card_types", ChooseOptionsPerSelectedCardTypes}, {"name_card", NameCard},
        {"choose_each_other_cards", ChooseEachOtherCards}, {"reveal_each_other_cards", RevealEachOtherCards},
        {"reveal_each_other_top_trash_type_except", RevealEachOtherTopTrashTypeExcept},
        {"inspect_top_cards", InspectTopCards}, {"reveal_top_cards", RevealTopCards}, {"reveal_top_if_named", RevealTopIfNamed}, {"move_all_ordered", MoveAllOrdered},
        {"remember_selected_card_cost", RememberCost}, {"remember_selected_card", RememberSelectedCard}, {"reveal_selected", RevealSelected},
        {"trash_selected", TrashSelected}, {"discard_selected", DiscardSelected}, {"discard_source_card", DiscardSourceCard}, {"discard_others_down_to", DiscardOthersDownTo},
        {"move_selected", MoveSelected}, {"move_last_moved", MoveLastMoved}, {"move_all_matching_types", MoveAllMatchingTypes},
        {"move_top_card", MoveTopCard}, {"play_selected", PlaySelected}, {"play_selected_twice_then_trash", PlaySelectedTwiceThenTrash}, {"insert_selected_into_deck", InsertSelectedIntoDeck},
        {"play_trigger_card", PlayTriggerCard}, {"discard_trigger_card", DiscardTriggerCard},
        {"choose_supply", ChooseSupply}, {"gain_card", GainCard}, {"gain_selected_supply", GainSelectedSupply},
        {"gain_selected_trash", GainSelectedTrash}, {"trash_selected_supply", TrashSelectedSupply},
        {"reveal_zone", RevealZone}, {"trash_source_card", TrashSourceCard},
        {"simultaneous_pass_left", SimultaneousPassLeft}, {"discard_hand_draw", DiscardHandDraw},
        {"replace_each_other_top_card", ReplaceEachOtherTopCard}, {"each_other_choose_discard_or_gain", EachOtherChooseDiscardOrGain},
        {"gain_special_pile", GainSpecialPile}, {"take_artifact", TakeArtifact},
        {"gain_trigger_card_from_trash", GainTriggerCardFromTrash},
        {"add_resource_per_distinct_type_in_play", AddResourcePerDistinctTypeInPlay},
        {"set_next_cleanup_draw_penalty", SetNextCleanupDrawPenalty}, {"mark_duration_resolved", MarkDurationResolved},
        {"set_aside_selected_until_next_turn", SetAsideSelectedUntilNextTurn},
        {"set_aside_top_until_next_turn", SetAsideTopUntilNextTurn},
        {"set_aside_trigger_until_turn_end", SetAsideTriggerUntilTurnEnd},
        {"set_aside_trigger_until_next_turn", SetAsideTriggerUntilNextTurn},
        {"move_trigger_card", MoveTriggerCard},
        {"discard_others_named_card", DiscardOthersNamedCard}, {"end_action_phase", EndActionPhase},
        {"modify_next_cleanup_draw", ModifyNextCleanupDraw},
        {ReactionRules.DrawDiscardOperation, AttackReactionDrawDiscard}
    };

    public static bool IsSupported(string op) => !string.IsNullOrWhiteSpace(op) && H.ContainsKey(op);
    public static EffectResolutionResult Resolve(CardEffectData e, EffectExecutionContext c)
    {
        if (e == null) return EffectResolutionResult.Rejected("Effect is null.");
        if (c == null || c.State == null || c.Actor == null) return EffectResolutionResult.Rejected("Effect execution context is incomplete.");
        if (string.IsNullOrWhiteSpace(e.op)) return EffectResolutionResult.Rejected("Effect operation is missing.");
        if (e.requiresLastSelection)
        {
            if (c.Resolution == null) return EffectResolutionResult.Rejected("Conditional effect requires an active ResolutionQueue.");
            if (c.Resolution.LastSelectionCount <= 0) return EffectResolutionResult.Applied();
        }
        if (e.requiresNoLastSelection)
        {
            if (c.Resolution == null) return EffectResolutionResult.Rejected("Conditional effect requires an active ResolutionQueue.");
            if (c.Resolution.LastSelectionCount > 0) return EffectResolutionResult.Applied();
        }
        if (e.requiresLastSelectionCount > 0)
        {
            if (c.Resolution == null) return EffectResolutionResult.Rejected("Exact-selection conditional effect requires an active ResolutionQueue.");
            if (c.Resolution.LastSelectionCount != e.requiresLastSelectionCount) return EffectResolutionResult.Applied();
        }
        if (!string.IsNullOrWhiteSpace(e.requiresSelectedOption))
        {
            if (c.Resolution == null) return EffectResolutionResult.Rejected("Option-conditional effect requires an active ResolutionQueue.");
            bool selected = false;
            foreach (string optionId in c.Resolution.SelectedOptionIds)
                if (string.Equals(optionId, e.requiresSelectedOption, StringComparison.OrdinalIgnoreCase)) { selected = true; break; }
            if (!selected) return EffectResolutionResult.Applied();
        }
        if (!string.IsNullOrWhiteSpace(e.requiresNoCardType))
        {
            CardZone zone = CardZone.Hand;
            if (!string.IsNullOrWhiteSpace(e.conditionZone) && !CardZoneRules.TryParseZone(e.conditionZone, out zone))
                return EffectResolutionResult.Rejected("Conditional zone is invalid.");
            if (Eligible(c.State, c.Actor, zone, string.Empty, e.requiresNoCardType, 0).Count > 0)
                return EffectResolutionResult.Applied();
        }
        if (e.requiresMinActionsPlayedThisTurn > 0 && c.Actor.ActionsPlayedThisTurn < e.requiresMinActionsPlayedThisTurn)
            return EffectResolutionResult.Applied();
        if (e.requiresSourceInPlay && (c.SourceCardInstanceId <= 0 || c.Actor.InPlay == null ||
            !c.Actor.InPlay.Contains(c.SourceCardInstanceId)))
            return EffectResolutionResult.Applied();
        if (e.requiresMaxHandSize >= 0 && (c.Actor.Hand == null || c.Actor.Hand.Count > e.requiresMaxHandSize))
            return EffectResolutionResult.Applied();
        if (e.requiresMinDiscardedOrTrashedThisTurn > 0 &&
            c.Actor.CardsDiscardedThisTurn + c.Actor.CardsTrashedThisTurn < e.requiresMinDiscardedOrTrashedThisTurn)
            return EffectResolutionResult.Applied();
        if (e.requiresMinTrashedThisTurn > 0 && c.Actor.CardsTrashedThisTurn < e.requiresMinTrashedThisTurn)
            return EffectResolutionResult.Applied();
        if (e.requiresMinDistinctTypesInHand > 0 &&
            CountDistinctTypes(c.State, c.Actor.Hand) < e.requiresMinDistinctTypesInHand)
            return EffectResolutionResult.Applied();
        if (e.requiresMinMatchingCardsInHand > 0 &&
            CountMatchingTypes(c.State, c.Actor.Hand, e.matchingCardTypes) < e.requiresMinMatchingCardsInHand)
            return EffectResolutionResult.Applied();
        if (e.requiresArtifactIds != null)
            foreach (string artifactId in e.requiresArtifactIds)
                if (!ArtifactRules.Controls(c.State, c.Actor, artifactId)) return EffectResolutionResult.Applied();
        if (!string.IsNullOrWhiteSpace(e.requiresLastMovedCardType))
        {
            CardInstance moved = Find(c.State, c.Resolution != null ? c.Resolution.LastMovedCardInstanceId : 0);
            ExtensionCardData movedDefinition = moved != null ? Def(moved.DefinitionId) : null;
            if (!CardDefinitionRules.HasAnyType(movedDefinition, e.requiresLastMovedCardType)) return EffectResolutionResult.Applied();
        }
        return H.TryGetValue(e.op, out Handler h) ? h(e, c) : EffectResolutionResult.Rejected("Unsupported effect operation: " + e.op);
    }

    private static EffectResolutionResult AddResource(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid add_resource effect.");
        switch ((e.resource ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "actions": c.Actor.Actions += e.amount; break; case "buys": c.Actor.Buys += e.amount; break; case "coins": c.Actor.Coins += e.amount; break;
            default: return EffectResolutionResult.Rejected("Unsupported resource: " + e.resource);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult AddResourcePerSelection(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || e.amount < 0) return EffectResolutionResult.Rejected("Invalid add_resource_per_last_selection effect.");
        return AddResource(new CardEffectData { target = "self", resource = e.resource, amount = e.amount * c.Resolution.LastSelectionCount }, c);
    }

    private static EffectResolutionResult ReduceCostsThisTurn(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid reduce_costs_this_turn effect.");
        return CostRules.AddReductionForCurrentTurn(c.State, c.Actor, e.amount, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult Draw(CardEffectData e, EffectExecutionContext c)
    {
        if (e.amount < 0) return EffectResolutionResult.Rejected("draw amount cannot be negative.");
        if (Self(e)) return CardZoneRules.DrawCards(c.Actor, e.amount, c.Random, out string err) ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(err);
        if (!Others(e)) return EffectResolutionResult.Rejected("draw supports targets 'self' and 'others'.");
        if (c.State.Players == null) return EffectResolutionResult.Applied();
        foreach (PlayerStateSnapshot p in c.State.Players)
        {
            if (p == null || p.PlayerId == c.Actor.PlayerId || SkipAttackTarget(c, p)) continue;
            if (!CardZoneRules.DrawCards(p, e.amount, c.Random, out string err)) return EffectResolutionResult.Rejected(err);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult DrawSelectionCount(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("draw_last_selection_count requires self and an active resolution.");
        return CardZoneRules.DrawCards(c.Actor, c.Resolution.LastSelectionCount, c.Random, out string err) ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult DrawToHandSize(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || e.amount < 0 || c.Actor.Hand == null)
            return EffectResolutionResult.Rejected("Invalid draw_to_hand_size effect.");
        int count = Math.Max(0, e.amount - c.Actor.Hand.Count);
        return CardZoneRules.DrawCards(c.Actor, count, c.Random, out string error)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult DrawToHandSizeSkippingType(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c) || e.amount < 0 || string.IsNullOrWhiteSpace(e.cardType))
            return EffectResolutionResult.Rejected("Invalid draw_to_hand_size_skipping_type effect.");
        GameRuleResult result = AdvancedActionRules.TryStartDrawToHandSizeSkippingType(c.State, c.Actor, c.Resolution,
            e.amount, e.cardType, e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing,
            c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, Def, c.Random);
        return FromGameRuleResult(result);
    }

    private static EffectResolutionResult ChooseCards(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c)) return EffectResolutionResult.Rejected("Invalid choose_cards effect.");
        if (!CardZoneRules.TryParseZone(e.zone, out CardZone z) ||
            (z != CardZone.Hand && z != CardZone.Discard && z != CardZone.Inspected && z != CardZone.Trash))
            return EffectResolutionResult.Rejected("choose_cards currently supports hand, discard, inspected and trash zones.");
        int min = Math.Max(0, e.min), max = e.max > 0 ? e.max : min;
        List<int> candidates = Eligible(c.State, c.Actor, z, e.cardId, e.cardType,
            e.lastMovedOnly ? c.Resolution.LastMovedCardInstanceId : 0, e.maxCost, e.excludedCardId);
        if (e.minUpToAvailable) min = Math.Min(min, candidates.Count);
        max = Math.Min(max, candidates.Count);
        if (candidates.Count == 0 && e.allowNoEligible) { c.Resolution.ClearSelection(); return EffectResolutionResult.Applied(); }
        if (candidates.Count < min && e.allowPass) { c.Resolution.ClearSelection(); return EffectResolutionResult.Applied(); }
        if (min > candidates.Count) return EffectResolutionResult.Rejected("choose_cards does not have enough eligible cards for its minimum.");
        if (candidates.Count == 0 && min == 0) { c.Resolution.ClearSelection(); return EffectResolutionResult.Applied(); }
        return c.Resolution.TrySuspendForDecision(c.Actor.PlayerId, "choose_cards", e.zone, e.prompt, c.SourceCardInstanceId,
            min, max, candidates, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, e.allowPass, out string err)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult ChoosePerEmptyPile(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c)) return EffectResolutionResult.Rejected("Invalid choose_cards_per_empty_pile effect.");
        int empty = 0; if (c.State.SupplyPiles != null) foreach (SupplyPileSnapshot p in c.State.SupplyPiles) if (p != null && p.RemainingCount <= 0) empty++;
        int required = Math.Min(empty, c.Actor.Hand != null ? c.Actor.Hand.Count : 0); if (required <= 0) return EffectResolutionResult.Applied();
        return c.Resolution.TrySuspendForDecision(c.Actor.PlayerId, "choose_cards_per_empty_pile", "hand", e.prompt, c.SourceCardInstanceId,
            required, required, c.Actor.Hand, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult ChooseOptions(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c) || e.options == null || e.options.Count == 0)
            return EffectResolutionResult.Rejected("Invalid choose_options effect.");
        int min = Math.Max(0, e.min), max = e.max > 0 ? e.max : min;
        List<string> ids = new List<string>();
        List<string> labels = new List<string>();
        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CardChoiceOptionData option in e.options)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.id) || string.IsNullOrWhiteSpace(option.label) || !unique.Add(option.id))
                return EffectResolutionResult.Rejected("choose_options contains an invalid or duplicate option.");
            ids.Add(option.id);
            labels.Add(option.label);
        }
        if (ids.Count > MaximumGenericOptionCount)
            return EffectResolutionResult.Rejected("choose_options supports at most " + MaximumGenericOptionCount +
                " options; use a dedicated decision control for larger sets.");
        max = Math.Min(max, ids.Count);
        if (min > max) return EffectResolutionResult.Rejected("choose_options does not have enough distinct options.");
        if (!c.Resolution.TrySuspendForOptionDecision(c.Actor.PlayerId, "choose_options", e.prompt, c.SourceCardInstanceId,
                min, max, ids, labels, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err))
            return EffectResolutionResult.Rejected(err);

        // An option immediately following inspect_top_cards must show the privately
        // inspected cards before the player chooses what to do with them.
        List<int> inspected = CardZoneRules.ResolveZone(c.Actor, CardZone.Inspected);
        if (inspected != null && inspected.Count > 0)
            c.Resolution.PendingDecision.CandidateInstanceIds.AddRange(inspected);
        return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult ChooseOptionsRepeatedPerEmptyKingdomPile(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c))
            return EffectResolutionResult.Rejected("Invalid repeated Kingdom-pile option effect.");
        int empty = 0;
        if (c.State.SupplyPiles != null)
            foreach (SupplyPileSnapshot pile in c.State.SupplyPiles)
                if (pile != null && pile.IsKingdom && pile.RemainingCount <= 0) empty++;
        return FromGameRuleResult(AdvancedActionRules.TryStartRepeatedOptions(c.State, c.Actor, c.Resolution,
            e, 1 + empty, c.SourceCardInstanceId, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex));
    }

    private static EffectResolutionResult ChooseOptionsPerSelectedCardTypes(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c) || c.Resolution.SelectedInstanceIds.Count != 1 ||
            e.options == null || e.options.Count == 0)
            return EffectResolutionResult.Rejected("Invalid choose_options_per_selected_card_types effect.");
        CardInstance selected = Find(c.State, c.Resolution.SelectedInstanceIds[0]);
        ExtensionCardData definition = selected != null ? Def(selected.DefinitionId) : null;
        int required = definition != null && definition.types != null ? definition.types.Count : 0;
        if (required <= 0 || required > e.options.Count)
            return EffectResolutionResult.Rejected("Selected card has an unsupported number of types.");
        if (e.options.Count > MaximumGenericOptionCount)
            return EffectResolutionResult.Rejected("choose_options_per_selected_card_types supports at most " +
                MaximumGenericOptionCount + " options; use a dedicated decision control for larger sets.");
        List<string> ids = new List<string>(); List<string> labels = new List<string>();
        foreach (CardChoiceOptionData option in e.options) { ids.Add(option.id); labels.Add(option.label); }
        return c.Resolution.TrySuspendForOptionDecision(c.Actor.PlayerId, "choose_options_per_selected_card_types", e.prompt,
            c.SourceCardInstanceId, required, required, ids, labels, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex, out string error)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult NameCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c))
            return EffectResolutionResult.Rejected("Invalid name_card effect.");

        List<string> ids = new List<string>();
        List<string> labels = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action<string> addCandidate = definitionId =>
        {
            if (string.IsNullOrWhiteSpace(definitionId) || !seen.Add(definitionId)) return;
            ExtensionCardData definition = Def(definitionId);
            if (definition == null || string.IsNullOrWhiteSpace(definition.name)) return;
            if (!string.IsNullOrWhiteSpace(e.cardType) && !CardDefinitionRules.HasAnyType(definition, e.cardType)) return;
            ids.Add(definitionId);
            labels.Add(definition.name);
        };

        if (c.State.SupplyPiles != null)
            foreach (SupplyPileSnapshot pile in c.State.SupplyPiles)
                if (pile != null) addCandidate(pile.DefinitionId);
        if (c.State.CardInstances != null)
            foreach (CardInstance card in c.State.CardInstances)
                if (card != null) addCandidate(card.DefinitionId);

        if (ids.Count == 0) return EffectResolutionResult.Rejected("name_card has no known card definitions in this match.");
        return c.Resolution.TrySuspendForOptionDecision(c.Actor.PlayerId, "name_card", e.prompt, c.SourceCardInstanceId,
            1, 1, ids, labels, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult ChooseEachOtherCards(CardEffectData e, EffectExecutionContext c) =>
        ChooseEachOtherCards(e, c, false);

    private static EffectResolutionResult RevealEachOtherCards(CardEffectData e, EffectExecutionContext c) =>
        ChooseEachOtherCards(e, c, true);

    private static EffectResolutionResult ChooseEachOtherCards(CardEffectData e, EffectExecutionContext c, bool publicReveal)
    {
        if (!Others(e) || c.Resolution == null || !Cursor(c)) return EffectResolutionResult.Rejected("Invalid choose/reveal_each_other_cards effect.");
        if (!CardZoneRules.TryParseZone(e.zone, out CardZone src) || !CardZoneRules.TryParseZone(e.destinationZone, out CardZone dst) || src == dst)
            return EffectResolutionResult.Rejected("choose/reveal_each_other_cards requires distinct valid source and destination zones.");
        int min = Math.Max(0, e.min), max = e.max > 0 ? e.max : min; List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>(); List<List<int>> choices = new List<List<int>>();
        if (c.State.Players != null) foreach (PlayerStateSnapshot p in c.State.Players)
        {
            if (p == null || p.PlayerId == c.Actor.PlayerId || SkipAttackTarget(c, p)) continue; List<int> cards = Eligible(c.State, p, src, e.cardId, e.cardType, 0);
            if (cards.Count < min)
            {
                if (publicReveal) JournalRules.PublishRevealZone(c.State, p, src, c.SourceCardInstanceId, c.EventBus);
                continue;
            }
            targets.Add(p); choices.Add(cards);
        }
        if (targets.Count == 0) return EffectResolutionResult.Applied();
        string op = (publicReveal ? "choose_each_other_cards_reveal|" : "choose_each_other_cards|") + (e.cardId ?? string.Empty) + "|" + (e.cardType ?? string.Empty) + "|" + e.destinationZone;
        List<string> remaining = new List<string>(); for (int i = 1; i < targets.Count; i++) remaining.Add(targets[i].PlayerId);
        if (!c.Resolution.TrySuspendForDecision(targets[0].PlayerId, op, e.zone, e.prompt, c.SourceCardInstanceId,
            min, Math.Min(max, choices[0].Count), choices[0], c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)) return EffectResolutionResult.Rejected(err);
        c.Resolution.PendingDecision.RemainingPlayerIds.AddRange(remaining); return EffectResolutionResult.WaitingForChoice();
    }

    private static EffectResolutionResult RevealEachOtherTopTrashTypeExcept(CardEffectData e, EffectExecutionContext c)
    {
        if (!Others(e) || c.Resolution == null || !Cursor(c) || e.amount <= 0 || string.IsNullOrWhiteSpace(e.cardType))
            return EffectResolutionResult.Rejected("Invalid reveal_each_other_top_trash_type_except effect.");
        GameRuleResult result = GameRules.TryStartRevealTrashForOthers(c.State, c.Actor, c.Resolution, e.amount, e.cardType, e.cardId,
            e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, c.Random);
        if (result == null || result.Status == GameRuleStatus.Applied) return EffectResolutionResult.Applied();
        return result.Status == GameRuleStatus.WaitingForChoice ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(result.Error);
    }

    private static EffectResolutionResult InspectTopCards(CardEffectData e, EffectExecutionContext c) =>
        MoveTopCardsToTemporary(e, c, false);

    private static EffectResolutionResult RevealTopCards(CardEffectData e, EffectExecutionContext c) =>
        MoveTopCardsToTemporary(e, c, true);

    private static EffectResolutionResult RevealTopIfNamed(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || c.Resolution.SelectedOptionIds.Count != 1)
            return EffectResolutionResult.Rejected("reveal_top_if_named requires exactly one named card.");
        List<int> inspected = CardZoneRules.ResolveZone(c.Actor, CardZone.Inspected);
        if (inspected == null || inspected.Count > 0)
            return EffectResolutionResult.Rejected("Cannot reveal a named top card while temporary storage is unavailable or not empty.");
        if (!CardZoneRules.TryMoveTopCardFromDeck(c.Actor, CardZone.Inspected, c.Random, out int instanceId, out string error))
            return EffectResolutionResult.Rejected(error);
        if (instanceId <= 0)
        {
            c.Resolution.TakeSelectedOptionIds();
            return EffectResolutionResult.Applied();
        }

        CardInstance card = Find(c.State, instanceId);
        if (card == null) return EffectResolutionResult.Rejected("Revealed card instance could not be resolved.");
        JournalRules.PublishReveal(c.State, c.Actor, instanceId, CardZone.Inspected, c.SourceCardInstanceId, c.EventBus);
        string namedDefinitionId = c.Resolution.TakeSelectedOptionIds()[0];
        CardZone destination = string.Equals(card.DefinitionId, namedDefinitionId, StringComparison.OrdinalIgnoreCase)
            ? CardZone.Hand
            : CardZone.Deck;
        return CardZoneRules.MoveCard(c.Actor, CardZone.Inspected, destination, instanceId)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected("Could not return the revealed card to its destination.");
    }

    private static EffectResolutionResult MoveTopCardsToTemporary(CardEffectData e, EffectExecutionContext c, bool publicReveal)
    {
        if (!Self(e) || c.Resolution == null || e.amount < 0)
            return EffectResolutionResult.Rejected("Invalid inspect/reveal top cards effect.");
        List<int> inspected = CardZoneRules.ResolveZone(c.Actor, CardZone.Inspected);
        if (inspected == null) return EffectResolutionResult.Rejected("Inspected storage zone is unavailable.");
        if (inspected.Count > 0) return EffectResolutionResult.Rejected("Cannot inspect/reveal new cards while temporary storage is not empty.");
        for (int n = 0; n < e.amount; n++)
        {
            if (!CardZoneRules.TryMoveTopCardFromDeck(c.Actor, CardZone.Inspected, c.Random, out int id, out string error))
                return EffectResolutionResult.Rejected(error);
            if (id <= 0) break;
            if (publicReveal) JournalRules.PublishReveal(c.State, c.Actor, id, CardZone.Inspected, c.SourceCardInstanceId, c.EventBus);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult MoveAllOrdered(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c) ||
            !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source) ||
            !CardZoneRules.TryParseZone(e.destinationZone, out CardZone destination) || source == destination)
            return EffectResolutionResult.Rejected("Invalid move_all_ordered effect.");
        return FromGameRuleResult(AdvancedActionRules.TryStartMoveAllOrdered(c.State, c.Actor, c.Resolution,
            source, destination, e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing,
            c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex));
    }

    private static EffectResolutionResult RememberCost(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid remember_selected_card_cost effect.");
        if (c.Resolution.SelectedInstanceIds.Count == 0) { c.Resolution.SetLastSelectedCardCost(-1); return EffectResolutionResult.Applied(); }
        if (c.Resolution.SelectedInstanceIds.Count != 1) return EffectResolutionResult.Rejected("remember_selected_card_cost requires at most one selected card.");
        CardInstance i = Find(c.State, c.Resolution.SelectedInstanceIds[0]); ExtensionCardData d = i != null ? Def(i.DefinitionId) : null;
        int effectiveCost = CostRules.GetEffectiveCost(c.State, d);
        if (d == null || effectiveCost < 0) return EffectResolutionResult.Rejected("Selected card definition/cost is invalid.");
        c.Resolution.SetLastSelectedCardCost(effectiveCost); return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult RememberSelectedCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid remember_selected_card effect.");
        if (c.Resolution.SelectedInstanceIds.Count == 0) { c.Resolution.SetLastMovedCardInstanceId(0); return EffectResolutionResult.Applied(); }
        if (c.Resolution.SelectedInstanceIds.Count != 1) return EffectResolutionResult.Rejected("remember_selected_card requires exactly one selected card.");
        c.Resolution.SetLastMovedCardInstanceId(c.Resolution.SelectedInstanceIds[0]); return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult RevealSelected(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid reveal_selected effect.");
        foreach (int id in c.Resolution.SelectedInstanceIds)
        {
            CardZone source = CardZoneRules.TryFindOwnedZone(c.Actor, id, out CardZone found) ? found : CardZone.None;
            JournalRules.PublishReveal(c.State, c.Actor, id, source, c.SourceCardInstanceId, c.EventBus);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ChooseSupply(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c)) return EffectResolutionResult.Rejected("Invalid choose_supply effect.");
        int min = Math.Max(0, e.min), max = e.max > 0 ? e.max : Math.Max(1, min), ceiling = e.maxCost, exact = -1;
        if (e.useLastSelectionCost)
        {
            if (c.Resolution.LastSelectedCardCost < 0) return min == 0 ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected("choose_supply requires a remembered selected-card cost.");
            int dyn = c.Resolution.LastSelectedCardCost + e.costOffset; ceiling = ceiling >= 0 ? Math.Min(ceiling, dyn) : dyn;
            if (e.exactCost) exact = dyn;
        }
        List<string> candidates = new List<string>(); if (c.State.SupplyPiles != null) foreach (SupplyPileSnapshot p in c.State.SupplyPiles)
        {
            if (p == null || p.RemainingCount <= 0 || string.IsNullOrEmpty(p.DefinitionId)) continue; ExtensionCardData d = Def(p.DefinitionId); if (d == null) continue;
            int effectiveCost = CostRules.GetEffectiveCost(c.State, d);
            if (effectiveCost < 0 || (ceiling >= 0 && effectiveCost > ceiling) || (exact >= 0 && effectiveCost != exact)) continue; if (!string.IsNullOrWhiteSpace(e.cardId) && !string.Equals(p.DefinitionId, e.cardId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(e.cardType) && !CardDefinitionRules.HasType(d, e.cardType)) continue; candidates.Add(p.DefinitionId);
        }
        max = Math.Min(max, candidates.Count); if (candidates.Count == 0) return (min == 0 || e.allowNoEligible) ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected("choose_supply has no eligible pile.");
        if (min > candidates.Count) return EffectResolutionResult.Rejected("choose_supply does not have enough eligible piles.");
        return c.Resolution.TrySuspendForSupplyDecision(c.Actor.PlayerId, "choose_supply", e.prompt, c.SourceCardInstanceId,
            min, max, candidates, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult TrashSelected(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid trash_selected effect.");
        CardZone source = CardZone.Hand;
        if (!string.IsNullOrWhiteSpace(e.sourceZone) && !CardZoneRules.TryParseZone(e.sourceZone, out source))
            return EffectResolutionResult.Rejected("trash_selected sourceZone is invalid.");
        foreach (int id in c.Resolution.TakeSelectedInstanceIds())
            if (!TrashRules.TryTrashFromZone(c.State, c.Actor, source, id, c.SourceCardInstanceId, c.EventBus, out string err))
                return EffectResolutionResult.Rejected(err);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult DiscardSelected(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid discard_selected effect.");
        CardZone source = CardZone.Hand;
        if (!string.IsNullOrWhiteSpace(e.sourceZone) && !CardZoneRules.TryParseZone(e.sourceZone, out source))
            return EffectResolutionResult.Rejected("discard_selected sourceZone is invalid.");
        return DiscardRules.TryDiscardSelected(c.State, c.Actor, source, c.Resolution.TakeSelectedInstanceIds(), c.SourceCardInstanceId, c.EventBus, out string err)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult DiscardSourceCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.SourceCardInstanceId <= 0)
            return EffectResolutionResult.Rejected("Invalid discard_source_card effect.");
        CardZone source = CardZone.Hand;
        if (!string.IsNullOrWhiteSpace(e.sourceZone) && !CardZoneRules.TryParseZone(e.sourceZone, out source))
            return EffectResolutionResult.Rejected("discard_source_card sourceZone is invalid.");
        return DiscardRules.TryDiscardSelected(c.State, c.Actor, source, new[] { c.SourceCardInstanceId },
                c.SourceCardInstanceId, c.EventBus, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult DiscardOthersDownTo(CardEffectData e, EffectExecutionContext c)
    {
        if (!Others(e) || c.Resolution == null || !Cursor(c) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid discard_others_down_to effect.");
        List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>(); if (c.State.Players != null) foreach (PlayerStateSnapshot p in c.State.Players)
            if (p != null && p.PlayerId != c.Actor.PlayerId && !SkipAttackTarget(c, p) && p.Hand != null && p.Hand.Count > e.amount) targets.Add(p);
        if (targets.Count == 0) return EffectResolutionResult.Applied(); List<string> rem = new List<string>(); for (int i = 1; i < targets.Count; i++) rem.Add(targets[i].PlayerId);
        return c.Resolution.TrySuspendForDiscardDownDecision(targets[0].PlayerId, e.prompt, c.SourceCardInstanceId, e.amount, targets[0].Hand,
            rem, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)
            ? EffectResolutionResult.WaitingForChoice() : EffectResolutionResult.Rejected(err);
    }

    private static EffectResolutionResult MoveSelected(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !CardZoneRules.TryParseZone(e.sourceZone, out CardZone s) || !CardZoneRules.TryParseZone(e.destinationZone, out CardZone d) || s == d)
            return EffectResolutionResult.Rejected("Invalid move_selected effect.");
        foreach (int id in c.Resolution.TakeSelectedInstanceIds()) if (!CardZoneRules.MoveCard(c.Actor, s, d, id)) return EffectResolutionResult.Rejected("Selected card could not be moved.");
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult MoveLastMoved(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || c.Resolution.LastMovedCardInstanceId <= 0 ||
            !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source) ||
            !CardZoneRules.TryParseZone(e.destinationZone, out CardZone destination) || source == destination)
            return EffectResolutionResult.Rejected("Invalid move_last_moved effect.");
        return CardZoneRules.MoveCard(c.Actor, source, destination, c.Resolution.LastMovedCardInstanceId)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected("Last moved card could not be moved.");
    }

    private static EffectResolutionResult MoveAllMatchingTypes(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || string.IsNullOrWhiteSpace(e.cardType) ||
            !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source) ||
            !CardZoneRules.TryParseZone(e.destinationZone, out CardZone destination) || source == destination)
            return EffectResolutionResult.Rejected("Invalid move_all_matching_types effect.");
        List<int> sourceCards = CardZoneRules.ResolveZone(c.Actor, source);
        if (sourceCards == null) return EffectResolutionResult.Rejected("Matching-type source zone is unavailable.");
        foreach (int id in new List<int>(sourceCards))
        {
            CardInstance card = Find(c.State, id); ExtensionCardData definition = card != null ? Def(card.DefinitionId) : null;
            if (CardDefinitionRules.HasAnyType(definition, e.cardType) && !CardZoneRules.MoveCard(c.Actor, source, destination, id))
                return EffectResolutionResult.Rejected("Matching card could not be moved.");
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult MoveTopCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !CardZoneRules.TryParseZone(e.destinationZone, out CardZone d)) return EffectResolutionResult.Rejected("Invalid move_top_card effect.");
        c.Resolution.ClearSelection(); c.Resolution.SetLastMovedCardInstanceId(0);
        if (!CardZoneRules.TryMoveTopCardFromDeck(c.Actor, d, c.Random, out int id, out string err)) return EffectResolutionResult.Rejected(err);
        c.Resolution.SetLastMovedCardInstanceId(id);
        if (id > 0 && d == CardZone.Discard && c.EventBus != null)
        {
            CardInstance i = Find(c.State, id); if (i == null) return EffectResolutionResult.Rejected("Moved top card instance could not be resolved.");
            c.EventBus.Publish(GameEvent.CardDiscarded(c.Actor.PlayerId, i.InstanceId, i.DefinitionId, c.SourceCardInstanceId));
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult PlaySelected(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !CardZoneRules.TryParseZone(e.sourceZone, out CardZone s)) return EffectResolutionResult.Rejected("Invalid play_selected effect.");
        int id;
        if (e.lastMovedOnly) id = c.Resolution.LastMovedCardInstanceId;
        else
        {
            List<int> selected = c.Resolution.TakeSelectedInstanceIds(); if (selected.Count == 0) return EffectResolutionResult.Applied();
            if (selected.Count != 1) return EffectResolutionResult.Rejected("play_selected supports one card at a time."); id = selected[0];
        }
        if (id <= 0) return EffectResolutionResult.Applied(); List<int> zone = CardZoneRules.ResolveZone(c.Actor, s); if (zone == null || !zone.Contains(id)) return EffectResolutionResult.Rejected("Selected card is no longer in source zone.");
        CardInstance i = Find(c.State, id); ExtensionCardData d = i != null ? Def(i.DefinitionId) : null; if (d == null) return EffectResolutionResult.Rejected("Selected card definition could not be resolved.");
        if (s != CardZone.InPlay && !CardZoneRules.MoveCard(c.Actor, s, CardZone.InPlay, id)) return EffectResolutionResult.Rejected("Selected card could not be moved into play.");
        if (CardDefinitionRules.HasType(d, "Action")) c.Actor.ActionsPlayedThisTurn++;
        if (CardDefinitionRules.HasType(d, "Attaque"))
        {
            c.Resolution.ClearAttackProtection();
            GameRuleResult rr = GameRules.TryStartAttackReactions(c.State, c.Actor, i, d, c.Resolution, Def, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex);
            if (rr != null) return rr.Status == GameRuleStatus.Rejected ? EffectResolutionResult.Rejected(rr.Error) : EffectResolutionResult.WaitingForChoice();
        }
        if (c.EventBus == null) return EffectResolutionResult.Rejected("play_selected requires an event bus.");
        c.EventBus.Publish(GameEvent.CardPlayed(c.Actor.PlayerId, i.InstanceId, i.DefinitionId)); return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult PlayTriggerCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || c.EventBus == null || c.TriggerEvent == null ||
            c.TriggerEvent.Type != GameEventType.CardRevealed || c.TriggerEvent.CardInstanceId <= 0)
            return EffectResolutionResult.Rejected("Invalid play_trigger_card effect.");
        int id = c.TriggerEvent.CardInstanceId;
        CardInstance card = Find(c.State, id);
        ExtensionCardData definition = card != null ? Def(card.DefinitionId) : null;
        if (card == null || definition == null || card.OwnerPlayerId != c.Actor.PlayerId)
            return EffectResolutionResult.Rejected("Revealed trigger card is invalid.");
        bool foundOwnedZone = CardZoneRules.TryFindOwnedZone(c.Actor, id, out CardZone source);
        if (!foundOwnedZone && c.State.TrashedCards != null && c.State.TrashedCards.Contains(id)) source = CardZone.Trash;
        else if (!foundOwnedZone) return EffectResolutionResult.Rejected("Revealed trigger card is not in a playable zone.");
        if (source != CardZone.InPlay && !CardZoneRules.MoveCard(c.State, c.Actor, source, CardZone.InPlay, id))
            return EffectResolutionResult.Rejected("Revealed trigger card could not be moved into play.");
        if (CardDefinitionRules.HasType(definition, "Action")) c.Actor.ActionsPlayedThisTurn++;
        if (CardDefinitionRules.HasType(definition, "Attaque"))
        {
            c.Resolution.ClearAttackProtection();
            GameRuleResult reactions = GameRules.TryStartAttackReactions(c.State, c.Actor, card, definition,
                c.Resolution, Def, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
                c.AbilityIndex, c.EffectIndex);
            if (reactions != null)
                return reactions.Status == GameRuleStatus.Rejected
                    ? EffectResolutionResult.Rejected(reactions.Error)
                    : EffectResolutionResult.WaitingForChoice();
        }
        c.EventBus.Publish(GameEvent.CardPlayed(c.Actor.PlayerId, id, card.DefinitionId));
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult DiscardTriggerCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.TriggerEvent == null || c.TriggerEvent.CardInstanceId <= 0 || c.EventBus == null)
            return EffectResolutionResult.Rejected("Invalid discard_trigger_card effect.");
        int id = c.TriggerEvent.CardInstanceId;
        if (c.Actor.Discard != null && c.Actor.Discard.Contains(id)) return EffectResolutionResult.Applied();
        if (c.Actor.InPlay == null || !c.Actor.InPlay.Contains(id))
            return EffectResolutionResult.Rejected("Trigger card is not in play for discard.");
        return DiscardRules.TryDiscardSelected(c.State, c.Actor, CardZone.InPlay, new[] { id },
                c.SourceCardInstanceId, c.EventBus, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult PlaySelectedTwiceThenTrash(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || c.EventBus == null ||
            !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source))
            return EffectResolutionResult.Rejected("Invalid play_selected_twice_then_trash effect.");
        List<int> selected = c.Resolution.TakeSelectedInstanceIds();
        if (selected.Count == 0) return EffectResolutionResult.Applied();
        if (selected.Count != 1) return EffectResolutionResult.Rejected("Double play requires exactly one card.");
        int id = selected[0];
        CardInstance card = Find(c.State, id); ExtensionCardData definition = card != null ? Def(card.DefinitionId) : null;
        if (definition == null || !CardDefinitionRules.HasType(definition, "Action"))
            return EffectResolutionResult.Rejected("Double-play selection is not an Action card.");
        if (!CardZoneRules.MoveCard(c.Actor, source, CardZone.InPlay, id))
            return EffectResolutionResult.Rejected("Double-play card could not enter play.");
        c.Actor.ActionsPlayedThisTurn += 2;
        c.EventBus.Publish(GameEvent.CardPlayed(c.Actor.PlayerId, id, card.DefinitionId));
        c.EventBus.Publish(GameEvent.CardPlayed(c.Actor.PlayerId, id, card.DefinitionId));
        if (!TrashRules.TryTrashFromZone(c.State, c.Actor, CardZone.InPlay, id, c.SourceCardInstanceId, c.EventBus, out string error))
            return EffectResolutionResult.Rejected(error);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult SetAsideSelectedUntilNextTurn(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source))
            return EffectResolutionResult.Rejected("Invalid set_aside_selected_until_next_turn effect.");
        string mode = string.IsNullOrWhiteSpace(e.returnMode) ? SetAsideRules.ReturnToHand : e.returnMode;
        List<int> selected = c.Resolution.TakeSelectedInstanceIds();
        foreach (int id in selected)
            if (!SetAsideRules.TryScheduleFromZone(c.State, c.Actor, source, id, c.SourceCardInstanceId, mode, out string error))
                return EffectResolutionResult.Rejected(error);
        if (selected.Count == 0 && c.Actor.InPlay != null && c.Actor.InPlay.Contains(c.SourceCardInstanceId))
            DurationRules.TryMarkResolved(c.State, c.Actor, c.SourceCardInstanceId, Def, out _);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult SetAsideTopUntilNextTurn(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e)) return EffectResolutionResult.Rejected("Invalid set_aside_top_until_next_turn effect.");
        string mode = string.IsNullOrWhiteSpace(e.returnMode) ? SetAsideRules.ReturnToHand : e.returnMode;
        if (!SetAsideRules.TryScheduleTopDeck(c.State, c.Actor, c.Random, c.SourceCardInstanceId, mode, out string error))
            return EffectResolutionResult.Rejected(error);
        if ((c.State.SetAsideCards == null || !c.State.SetAsideCards.Exists(entry => entry != null &&
                entry.SourceCardInstanceId == c.SourceCardInstanceId)) && c.Actor.InPlay != null &&
                c.Actor.InPlay.Contains(c.SourceCardInstanceId))
            DurationRules.TryMarkResolved(c.State, c.Actor, c.SourceCardInstanceId, Def, out _);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult SetAsideTriggerUntilTurnEnd(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.TriggerEvent == null || c.TriggerEvent.CardInstanceId <= 0)
            return EffectResolutionResult.Rejected("Invalid set_aside_trigger_until_turn_end effect.");
        return SetAsideRules.TryScheduleFromZone(c.State, c.Actor, CardZone.Trash, c.TriggerEvent.CardInstanceId,
            c.SourceCardInstanceId, SetAsideRules.ReturnToSupplyAtTurnEnd, out string error)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult SetAsideTriggerUntilNextTurn(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.TriggerEvent == null || c.TriggerEvent.CardInstanceId <= 0)
            return EffectResolutionResult.Rejected("Invalid set_aside_trigger_until_next_turn effect.");
        CardZone source = c.TriggerEvent.Type == GameEventType.CardDiscarded ? CardZone.Discard :
            c.TriggerEvent.Type == GameEventType.CardTrashed ? CardZone.Trash : CardZone.None;
        if (source == CardZone.None)
            return EffectResolutionResult.Rejected("Only discarded or trashed cards can be replaced by set-aside.");
        if (!SetAsideRules.TryScheduleFromZone(c.State, c.Actor, source, c.TriggerEvent.CardInstanceId,
                c.SourceCardInstanceId, SetAsideRules.ReturnToHand, out string error))
            return EffectResolutionResult.Rejected(error);
        if (source == CardZone.Discard) c.Actor.CardsDiscardedThisTurn = Math.Max(0, c.Actor.CardsDiscardedThisTurn - 1);
        else c.Actor.CardsTrashedThisTurn = Math.Max(0, c.Actor.CardsTrashedThisTurn - 1);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult MoveTriggerCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.TriggerEvent == null || c.TriggerEvent.CardInstanceId <= 0 ||
            !CardZoneRules.TryParseZone(e.sourceZone, out CardZone source) ||
            !CardZoneRules.TryParseZone(e.destinationZone, out CardZone destination) || source == destination)
            return EffectResolutionResult.Rejected("Invalid move_trigger_card effect.");
        return CardZoneRules.MoveCard(c.State, c.Actor, source, destination, c.TriggerEvent.CardInstanceId)
            ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected("Trigger card is no longer in its expected zone.");
    }

    private static EffectResolutionResult DiscardOthersNamedCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Others(e) || c.Resolution == null || c.Resolution.SelectedOptionIds.Count != 1)
            return EffectResolutionResult.Rejected("discard_others_named_card requires one named card.");
        string namedId = c.Resolution.TakeSelectedOptionIds()[0];
        int discarded = 0;
        if (c.State.Players != null)
            foreach (PlayerStateSnapshot target in c.State.Players)
            {
                if (target == null || target.PlayerId == c.Actor.PlayerId || SkipAttackTarget(c, target)) continue;
                int match = target.Hand != null ? target.Hand.Find(id =>
                {
                    CardInstance card = Find(c.State, id);
                    return card != null && string.Equals(card.DefinitionId, namedId, StringComparison.OrdinalIgnoreCase);
                }) : 0;
                if (match <= 0) { JournalRules.PublishRevealZone(c.State, target, CardZone.Hand, c.SourceCardInstanceId, c.EventBus); continue; }
                if (!DiscardRules.TryDiscardSelectedFromHand(c.State, target, new[] { match }, c.SourceCardInstanceId,
                        c.EventBus, out string error)) return EffectResolutionResult.Rejected(error);
                discarded++;
            }
        c.Resolution.SetLastSelectionCount(discarded);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult EndActionPhase(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e)) return EffectResolutionResult.Rejected("end_action_phase requires target self.");
        if (string.Equals(c.State.Phase, GameRules.ActionPhase, StringComparison.OrdinalIgnoreCase))
            c.State.Phase = GameRules.BuyPhase;
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult GainCard(CardEffectData e, EffectExecutionContext c)
    {
        if ((!Self(e) && !Others(e)) || string.IsNullOrWhiteSpace(e.cardId) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid gain_card effect.");
        CardZone d = CardZone.Discard; if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out d)) return EffectResolutionResult.Rejected("gain_card destinationZone is invalid.");
        int count = e.amount > 0 ? e.amount : 1; List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>();
        if (Self(e)) targets.Add(c.Actor); else if (c.State.Players != null) foreach (PlayerStateSnapshot p in c.State.Players) if (p != null && p.PlayerId != c.Actor.PlayerId && !SkipAttackTarget(c, p)) targets.Add(p);
        foreach (PlayerStateSnapshot p in targets) for (int n = 0; n < count; n++)
        {
            SupplyPileSnapshot pile = c.State.SupplyPiles != null ? c.State.SupplyPiles.Find(x => x != null && string.Equals(x.DefinitionId, e.cardId, StringComparison.OrdinalIgnoreCase)) : null;
            if (pile == null || pile.RemainingCount <= 0) return EffectResolutionResult.Applied();
            if (!GainRules.TryGainFromSupply(c.State, p, e.cardId, d, c.SourceCardInstanceId, c.EventBus, out _, out string err)) return EffectResolutionResult.Rejected(err);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult GainSelectedSupply(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid gain_selected_supply effect.");
        CardZone d = CardZone.Discard; if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out d)) return EffectResolutionResult.Rejected("Invalid gain destination.");
        foreach (string id in c.Resolution.TakeSelectedDefinitionIds())
        {
            if (!GainRules.TryGainFromSupply(c.State, c.Actor, id, d, c.SourceCardInstanceId, c.EventBus, out int gainedId, out string err))
                return EffectResolutionResult.Rejected(err);
            c.Resolution.SetLastMovedCardInstanceId(gainedId);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult GainSelectedTrash(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid gain_selected_trash effect.");
        CardZone d = CardZone.Discard;
        if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out d))
            return EffectResolutionResult.Rejected("Invalid gain-from-trash destination.");
        foreach (int id in c.Resolution.TakeSelectedInstanceIds())
            if (!GainRules.TryGainFromTrash(c.State, c.Actor, id, d, c.SourceCardInstanceId, c.EventBus, out string err))
                return EffectResolutionResult.Rejected(err);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult GainSpecialPile(CardEffectData e, EffectExecutionContext c)
    {
        if ((!Self(e) && !Others(e)) || string.IsNullOrWhiteSpace(e.specialPileId) || e.amount < 0)
            return EffectResolutionResult.Rejected("Invalid gain_special_pile effect.");
        CardZone destination = CardZone.Discard;
        if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out destination))
            return EffectResolutionResult.Rejected("gain_special_pile destination is invalid.");
        int requested = e.amount > 0 ? e.amount : 1;
        List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>();
        if (Self(e)) targets.Add(c.Actor);
        else if (c.State.Players != null)
            foreach (PlayerStateSnapshot player in c.State.Players)
                if (player != null && player.PlayerId != c.Actor.PlayerId && !SkipAttackTarget(c, player)) targets.Add(player);

        foreach (PlayerStateSnapshot target in targets)
        {
            int gained = 0;
            for (int index = 0; index < requested; index++)
            {
                SpecialPileSnapshot pile = SpecialPileRules.Find(c.State, e.specialPileId);
                if (pile == null) return EffectResolutionResult.Rejected("Special pile was not found: " + e.specialPileId);
                if (pile.CardInstanceIds == null || pile.CardInstanceIds.Count == 0) break;
                if (!SpecialPileRules.TryGainTop(c.State, target, e.specialPileId, destination,
                        c.SourceCardInstanceId, c.EventBus, Def, out _, out string error))
                    return EffectResolutionResult.Rejected(error);
                gained++;
            }
            if (e.drawForMissing && gained < requested &&
                !CardZoneRules.DrawCards(target, requested - gained, c.Random, out string drawError))
                return EffectResolutionResult.Rejected(drawError);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult TakeArtifact(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || string.IsNullOrWhiteSpace(e.artifactId))
            return EffectResolutionResult.Rejected("Invalid take_artifact effect.");
        return ArtifactRules.TryTake(c.State, c.Actor, e.artifactId, c.SourceCardInstanceId,
            c.EventBus, out _, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult GainTriggerCardFromTrash(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.TriggerEvent == null || c.TriggerEvent.CardInstanceId <= 0)
            return EffectResolutionResult.Rejected("gain_trigger_card_from_trash requires a card event.");
        CardZone destination = CardZone.Hand;
        if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out destination))
            return EffectResolutionResult.Rejected("Trigger-card gain destination is invalid.");
        return GainRules.TryGainFromTrash(c.State, c.Actor, c.TriggerEvent.CardInstanceId, destination,
            c.SourceCardInstanceId, c.EventBus, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult AddResourcePerDistinctTypeInPlay(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid type-scaled resource effect.");
        int typeCount = CountDistinctTypes(c.State, c.Actor.InPlay);
        int units = e.max > 0 ? Math.Min(typeCount, e.max) : typeCount;
        return AddResource(new CardEffectData { target = "self", resource = e.resource, amount = units * e.amount }, c);
    }

    private static EffectResolutionResult SetNextCleanupDrawPenalty(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || e.amount < 0) return EffectResolutionResult.Rejected("Invalid cleanup draw penalty.");
        c.Actor.NextCleanupDrawModifier -= e.amount;
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ModifyNextCleanupDraw(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e)) return EffectResolutionResult.Rejected("modify_next_cleanup_draw requires target self.");
        c.Actor.NextCleanupDrawModifier += e.amount;
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult MarkDurationResolved(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e)) return EffectResolutionResult.Rejected("mark_duration_resolved requires target self.");
        return DurationRules.TryMarkResolved(c.State, c.Actor, c.SourceCardInstanceId, Def, out string error)
            ? EffectResolutionResult.Applied()
            : EffectResolutionResult.Rejected(error);
    }

    private static EffectResolutionResult TrashSelectedSupply(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid trash_selected_supply effect.");
        foreach (string id in c.Resolution.TakeSelectedDefinitionIds())
            if (!TrashRules.TryTrashFromSupply(c.State, c.Actor, id, c.SourceCardInstanceId, c.EventBus, out _, out string err))
                return EffectResolutionResult.Rejected(err);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult RevealZone(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || !CardZoneRules.TryParseZone(e.zone, out CardZone zone))
            return EffectResolutionResult.Rejected("Invalid reveal_zone effect.");
        JournalRules.PublishRevealZone(c.State, c.Actor, zone, c.SourceCardInstanceId, c.EventBus);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult TrashSourceCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || c.SourceCardInstanceId <= 0)
            return EffectResolutionResult.Rejected("Invalid trash_source_card effect.");
        if (!TrashRules.TryTrashFromZone(c.State, c.Actor, CardZone.InPlay, c.SourceCardInstanceId,
                c.SourceCardInstanceId, c.EventBus, out string error))
            return EffectResolutionResult.Rejected(error);
        c.Resolution.SetLastMovedCardInstanceId(c.SourceCardInstanceId);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult SimultaneousPassLeft(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c))
            return EffectResolutionResult.Rejected("Invalid simultaneous_pass_left effect.");
        return FromGameRuleResult(AdvancedActionRules.TryStartSimultaneousPassLeft(c.State, c.Actor, c.Resolution,
            e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex));
    }

    private static EffectResolutionResult InsertSelectedIntoDeck(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c))
            return EffectResolutionResult.Rejected("Invalid insert_selected_into_deck effect.");
        return FromGameRuleResult(AdvancedActionRules.TryStartInsertSelectedIntoDeck(c.State, c.Actor, c.Resolution,
            e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex));
    }

    private static EffectResolutionResult DiscardHandDraw(CardEffectData e, EffectExecutionContext c)
    {
        if ((!Self(e) && !Others(e)) || e.amount < 0 || c.EventBus == null)
            return EffectResolutionResult.Rejected("Invalid discard_hand_draw effect.");
        List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>();
        if (Self(e)) targets.Add(c.Actor);
        else if (c.State.Players != null) foreach (PlayerStateSnapshot player in c.State.Players)
            if (player != null && player.PlayerId != c.Actor.PlayerId && !SkipAttackTarget(c, player) &&
                player.Hand != null && player.Hand.Count >= e.minHandSize) targets.Add(player);
        foreach (PlayerStateSnapshot target in targets)
        {
            List<int> cards = target.Hand != null ? new List<int>(target.Hand) : new List<int>();
            if (!DiscardRules.TryDiscardSelectedFromHand(c.State, target, cards, c.SourceCardInstanceId, c.EventBus, out string discardError))
                return EffectResolutionResult.Rejected(discardError);
            if (!CardZoneRules.DrawCards(target, e.amount, c.Random, out string drawError)) return EffectResolutionResult.Rejected(drawError);
        }
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ReplaceEachOtherTopCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Others(e) || c.Resolution == null || !Cursor(c))
            return EffectResolutionResult.Rejected("Invalid replace_each_other_top_card effect.");
        return FromGameRuleResult(AdvancedActionRules.TryStartReplaceEachOtherTopCard(c.State, c.Actor, c.Resolution,
            e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex, Def, c.Random));
    }

    private static EffectResolutionResult EachOtherChooseDiscardOrGain(CardEffectData e, EffectExecutionContext c)
    {
        if (!Others(e) || c.Resolution == null || !Cursor(c) || e.amount < 0 || string.IsNullOrWhiteSpace(e.cardId))
            return EffectResolutionResult.Rejected("Invalid each_other_choose_discard_or_gain effect.");
        CardZone destination = CardZone.Discard;
        if (!string.IsNullOrWhiteSpace(e.destinationZone) && !CardZoneRules.TryParseZone(e.destinationZone, out destination))
            return EffectResolutionResult.Rejected("Invalid alternate-gain destination.");
        return FromGameRuleResult(AdvancedActionRules.TryStartEachOtherChooseDiscardOrGain(c.State, c.Actor, c.Resolution,
            e.amount, e.cardId, destination, e.prompt, c.SourceCardInstanceId, c.TriggerEvent, c.Timing,
            c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex));
    }

    private static EffectResolutionResult AttackReactionDrawDiscard(CardEffectData e, EffectExecutionContext c) =>
        EffectResolutionResult.Rejected("attack_reaction_draw_discard is resolved by the pre-Attack reaction pipeline.");

    private static EffectResolutionResult FromGameRuleResult(GameRuleResult result)
    {
        if (result == null || result.Status == GameRuleStatus.Applied) return EffectResolutionResult.Applied();
        return result.Status == GameRuleStatus.WaitingForChoice
            ? EffectResolutionResult.WaitingForChoice()
            : EffectResolutionResult.Rejected(result.Error);
    }

    private static List<int> Eligible(GameStateSnapshot state, PlayerStateSnapshot p, CardZone z, string cardId, string cardType,
        int onlyId, int maxCost = -1, string excludedCardId = null)
    {
        List<int> r = new List<int>(); List<int> source = CardZoneRules.ResolveZone(state, p, z); if (source == null) return r;
        foreach (int id in source)
        {
            if (onlyId > 0 && id != onlyId) continue; CardInstance i = Find(state, id); if (i == null) continue;
            if (!string.IsNullOrWhiteSpace(cardId) && !string.Equals(i.DefinitionId, cardId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(excludedCardId) && string.Equals(i.DefinitionId, excludedCardId, StringComparison.OrdinalIgnoreCase)) continue;
            ExtensionCardData definition = Def(i.DefinitionId);
            if (!string.IsNullOrWhiteSpace(cardType) && !CardDefinitionRules.HasType(definition, cardType)) continue;
            if (maxCost >= 0 && (definition == null || CostRules.GetEffectiveCost(state, definition) > maxCost)) continue;
            r.Add(id);
        }
        return r;
    }
    private static bool SkipAttackTarget(EffectExecutionContext c, PlayerStateSnapshot p)
    {
        if (c == null || p == null || c.Resolution == null || !c.Resolution.IsAttackProtected(p.PlayerId)) return false;
        CardInstance s = Find(c.State, c.SourceCardInstanceId); return s != null && CardDefinitionRules.HasType(Def(s.DefinitionId), "Attaque");
    }
    private static int CountDistinctTypes(GameStateSnapshot state, List<int> instanceIds)
    {
        HashSet<string> types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (instanceIds == null) return 0;
        foreach (int id in instanceIds)
        {
            CardInstance instance = Find(state, id);
            ExtensionCardData definition = instance != null ? Def(instance.DefinitionId) : null;
            if (definition == null || definition.types == null) continue;
            foreach (string type in definition.types)
                if (!string.IsNullOrWhiteSpace(type)) types.Add(type.Trim());
        }
        return types.Count;
    }
    private static int CountMatchingTypes(GameStateSnapshot state, List<int> instanceIds, List<string> requestedTypes)
    {
        if (instanceIds == null || requestedTypes == null || requestedTypes.Count == 0) return 0;
        int count = 0;
        foreach (int id in instanceIds)
        {
            CardInstance instance = Find(state, id);
            ExtensionCardData definition = instance != null ? Def(instance.DefinitionId) : null;
            foreach (string type in requestedTypes)
                if (CardDefinitionRules.HasType(definition, type)) { count++; break; }
        }
        return count;
    }
    // A durable decision may belong either to the event's subject card or to an
    // external listener in hand/in play. The listener id therefore does not have
    // to equal TriggerEvent.CardInstanceId; that relationship is preserved
    // separately by TriggerResolver through ListenerScope and its continuation.
    private static bool Cursor(EffectExecutionContext c) =>
        c.AbilityIndex >= 0 && c.EffectIndex >= 0 && c.ListenerCardInstanceId > 0 && c.TriggerEvent != null;
    private static CardInstance Find(GameStateSnapshot s, int id) => s != null && s.CardInstances != null && id > 0 ? s.CardInstances.Find(x => x != null && x.InstanceId == id) : null;
    private static ExtensionCardData Def(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null; int k = id.IndexOf(':'); if (k <= 0 || k >= id.Length - 1) return null;
        return ExtensionCatalog.FindCard(id.Substring(0, k), id.Substring(k + 1));
    }
    private static bool Self(CardEffectData e) => e != null && string.Equals(e.target, "self", StringComparison.OrdinalIgnoreCase);
    private static bool Others(CardEffectData e) => e != null && string.Equals(e.target, "others", StringComparison.OrdinalIgnoreCase);
}
