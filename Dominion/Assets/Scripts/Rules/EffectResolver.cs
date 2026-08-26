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
    private delegate EffectResolutionResult Handler(CardEffectData e, EffectExecutionContext c);
    private static readonly Dictionary<string, Handler> H = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
    {
        {"add_resource", AddResource}, {"add_resource_per_last_selection", AddResourcePerSelection},
        {"draw", Draw}, {"draw_last_selection_count", DrawSelectionCount}, {"draw_to_hand_size_skipping_type", DrawToHandSizeSkippingType},
        {"choose_cards", ChooseCards}, {"choose_cards_per_empty_pile", ChoosePerEmptyPile},
        {"choose_each_other_cards", ChooseEachOtherCards}, {"reveal_each_other_cards", RevealEachOtherCards},
        {"reveal_each_other_top_trash_type_except", RevealEachOtherTopTrashTypeExcept},
        {"inspect_top_cards", InspectTopCards}, {"reveal_top_cards", RevealTopCards}, {"move_all_ordered", MoveAllOrdered},
        {"remember_selected_card_cost", RememberCost}, {"remember_selected_card", RememberSelectedCard},
        {"trash_selected", TrashSelected}, {"discard_selected", DiscardSelected}, {"discard_others_down_to", DiscardOthersDownTo},
        {"move_selected", MoveSelected}, {"move_top_card", MoveTopCard}, {"play_selected", PlaySelected},
        {"choose_supply", ChooseSupply}, {"gain_card", GainCard}, {"gain_selected_supply", GainSelectedSupply},
        {"gain_selected_trash", GainSelectedTrash}, {"trash_selected_supply", TrashSelectedSupply}
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
        List<int> candidates = Eligible(c.State, c.Actor, z, e.cardId, e.cardType, e.lastMovedOnly ? c.Resolution.LastMovedCardInstanceId : 0);
        max = Math.Min(max, candidates.Count);
        if (candidates.Count == 0 && e.allowNoEligible) { c.Resolution.ClearSelection(); return EffectResolutionResult.Applied(); }
        if (min > candidates.Count) return EffectResolutionResult.Rejected("choose_cards does not have enough eligible cards for its minimum.");
        if (candidates.Count == 0 && min == 0) { c.Resolution.ClearSelection(); return EffectResolutionResult.Applied(); }
        return c.Resolution.TrySuspendForDecision(c.Actor.PlayerId, "choose_cards", e.zone, e.prompt, c.SourceCardInstanceId,
            min, max, candidates, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)
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
                if (publicReveal) JournalRules.RecordRevealZone(c.State, p, src);
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
            if (publicReveal) JournalRules.RecordReveal(c.State, c.Actor, id);
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
        if (d == null || d.cost < 0) return EffectResolutionResult.Rejected("Selected card definition/cost is invalid.");
        c.Resolution.SetLastSelectedCardCost(d.cost); return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult RememberSelectedCard(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid remember_selected_card effect.");
        if (c.Resolution.SelectedInstanceIds.Count == 0) { c.Resolution.SetLastMovedCardInstanceId(0); return EffectResolutionResult.Applied(); }
        if (c.Resolution.SelectedInstanceIds.Count != 1) return EffectResolutionResult.Rejected("remember_selected_card requires exactly one selected card.");
        c.Resolution.SetLastMovedCardInstanceId(c.Resolution.SelectedInstanceIds[0]); return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ChooseSupply(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null || !Cursor(c)) return EffectResolutionResult.Rejected("Invalid choose_supply effect.");
        int min = Math.Max(0, e.min), max = e.max > 0 ? e.max : Math.Max(1, min), ceiling = e.maxCost;
        if (e.useLastSelectionCost)
        {
            if (c.Resolution.LastSelectedCardCost < 0) return min == 0 ? EffectResolutionResult.Applied() : EffectResolutionResult.Rejected("choose_supply requires a remembered selected-card cost.");
            int dyn = c.Resolution.LastSelectedCardCost + e.costOffset; ceiling = ceiling >= 0 ? Math.Min(ceiling, dyn) : dyn;
        }
        List<string> candidates = new List<string>(); if (c.State.SupplyPiles != null) foreach (SupplyPileSnapshot p in c.State.SupplyPiles)
        {
            if (p == null || p.RemainingCount <= 0 || string.IsNullOrEmpty(p.DefinitionId)) continue; ExtensionCardData d = Def(p.DefinitionId); if (d == null) continue;
            if (ceiling >= 0 && d.cost > ceiling) continue; if (!string.IsNullOrWhiteSpace(e.cardId) && !string.Equals(p.DefinitionId, e.cardId, StringComparison.OrdinalIgnoreCase)) continue;
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
        if (CardDefinitionRules.HasType(d, "Attaque"))
        {
            c.Resolution.ClearAttackProtection();
            GameRuleResult rr = GameRules.TryStartAttackReactions(c.State, c.Actor, i, d, c.Resolution, Def, c.TriggerEvent, c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex);
            if (rr != null) return rr.Status == GameRuleStatus.Rejected ? EffectResolutionResult.Rejected(rr.Error) : EffectResolutionResult.WaitingForChoice();
        }
        if (c.EventBus == null) return EffectResolutionResult.Rejected("play_selected requires an event bus.");
        c.EventBus.Publish(GameEvent.CardPlayed(c.Actor.PlayerId, i.InstanceId, i.DefinitionId)); return EffectResolutionResult.Applied();
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
        foreach (string id in c.Resolution.TakeSelectedDefinitionIds()) if (!GainRules.TryGainFromSupply(c.State, c.Actor, id, d, c.SourceCardInstanceId, c.EventBus, out _, out string err)) return EffectResolutionResult.Rejected(err);
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

    private static EffectResolutionResult TrashSelectedSupply(CardEffectData e, EffectExecutionContext c)
    {
        if (!Self(e) || c.Resolution == null) return EffectResolutionResult.Rejected("Invalid trash_selected_supply effect.");
        foreach (string id in c.Resolution.TakeSelectedDefinitionIds())
            if (!TrashRules.TryTrashFromSupply(c.State, c.Actor, id, c.SourceCardInstanceId, c.EventBus, out _, out string err))
                return EffectResolutionResult.Rejected(err);
        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult FromGameRuleResult(GameRuleResult result)
    {
        if (result == null || result.Status == GameRuleStatus.Applied) return EffectResolutionResult.Applied();
        return result.Status == GameRuleStatus.WaitingForChoice
            ? EffectResolutionResult.WaitingForChoice()
            : EffectResolutionResult.Rejected(result.Error);
    }

    private static List<int> Eligible(GameStateSnapshot state, PlayerStateSnapshot p, CardZone z, string cardId, string cardType, int onlyId)
    {
        List<int> r = new List<int>(); List<int> source = CardZoneRules.ResolveZone(state, p, z); if (source == null) return r;
        foreach (int id in source)
        {
            if (onlyId > 0 && id != onlyId) continue; CardInstance i = Find(state, id); if (i == null) continue;
            if (!string.IsNullOrWhiteSpace(cardId) && !string.Equals(i.DefinitionId, cardId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(cardType) && !CardDefinitionRules.HasType(Def(i.DefinitionId), cardType)) continue; r.Add(id);
        }
        return r;
    }
    private static bool SkipAttackTarget(EffectExecutionContext c, PlayerStateSnapshot p)
    {
        if (c == null || p == null || c.Resolution == null || !c.Resolution.IsAttackProtected(p.PlayerId)) return false;
        CardInstance s = Find(c.State, c.SourceCardInstanceId); return s != null && CardDefinitionRules.HasType(Def(s.DefinitionId), "Attaque");
    }
    private static bool Cursor(EffectExecutionContext c) => c.AbilityIndex >= 0 && c.EffectIndex >= 0 && c.ListenerCardInstanceId > 0 && c.TriggerEvent != null && c.TriggerEvent.CardInstanceId == c.ListenerCardInstanceId;
    private static CardInstance Find(GameStateSnapshot s, int id) => s != null && s.CardInstances != null && id > 0 ? s.CardInstances.Find(x => x != null && x.InstanceId == id) : null;
    private static ExtensionCardData Def(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null; int k = id.IndexOf(':'); if (k <= 0 || k >= id.Length - 1) return null;
        return ExtensionCatalog.FindCard(id.Substring(0, k), id.Substring(k + 1));
    }
    private static bool Self(CardEffectData e) => e != null && string.Equals(e.target, "self", StringComparison.OrdinalIgnoreCase);
    private static bool Others(CardEffectData e) => e != null && string.Equals(e.target, "others", StringComparison.OrdinalIgnoreCase);
}
