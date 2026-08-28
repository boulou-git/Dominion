using System;
using System.Collections.Generic;

public enum GameRuleStatus { Applied, WaitingForChoice, Rejected }

public sealed class GameRuleResult
{
    public GameRuleStatus Status { get; }
    public string Error { get; }
    public List<GameEvent> Events { get; }
    public bool Succeeded => Status == GameRuleStatus.Applied;
    private GameRuleResult(GameRuleStatus s, string e, List<GameEvent> events) { Status = s; Error = e ?? string.Empty; Events = events ?? new List<GameEvent>(); }
    public static GameRuleResult Applied(List<GameEvent> e) => new GameRuleResult(GameRuleStatus.Applied, string.Empty, e);
    public static GameRuleResult WaitingForChoice(List<GameEvent> e) => new GameRuleResult(GameRuleStatus.WaitingForChoice, string.Empty, e);
    public static GameRuleResult Rejected(string e, List<GameEvent> events = null) => new GameRuleResult(GameRuleStatus.Rejected, e, events);
}

public static class GameRules
{
    public const string ActionPhase = "Action", BuyPhase = "Buy", CleanupPhase = "Cleanup";
    private const string AttackReactionDrawDiscardPrefix = "attack_reaction_draw_discard|";

    public static GameRuleResult TryPlayCard(GameStateSnapshot s, string playerId, int instanceId,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (s == null || string.IsNullOrEmpty(playerId) || instanceId <= 0 || resolve == null) return GameRuleResult.Rejected("Invalid play request.");
        PlayerStateSnapshot p = Player(s, playerId); if (p == null) return GameRuleResult.Rejected("Player was not found.");
        if (p.Hand == null || p.InPlay == null || !p.Hand.Contains(instanceId)) return GameRuleResult.Rejected("Card is not in the player's hand.");
        CardInstance i = Card(s, instanceId); if (i == null || i.OwnerPlayerId != playerId) return GameRuleResult.Rejected("Card instance/owner is invalid.");
        ExtensionCardData d = resolve(i.DefinitionId); if (d == null) return GameRuleResult.Rejected("Card definition could not be resolved: " + i.DefinitionId);
        string policy = ValidatePlayPolicy(s, p, d, out bool consumesAction); if (!string.IsNullOrEmpty(policy)) return GameRuleResult.Rejected(policy);
        if (!ResolutionQueue.TryBegin(s, playerId, out ResolutionQueue q, out string err)) return GameRuleResult.Rejected(err);
        if (!CardZoneRules.MoveCard(p, CardZone.Hand, CardZone.InPlay, instanceId)) return GameRuleResult.Rejected("Could not move card into play.");
        if (consumesAction) p.Actions--;
        if (CardDefinitionRules.HasType(d, "Action")) p.ActionsPlayedThisTurn++;
        if (CardDefinitionRules.HasType(d, "Attaque"))
        {
            GameRuleResult rr = TryStartAttackReactions(s, p, i, d, q, resolve); if (rr != null) return rr;
        }
        return ResolvePlayedCard(s, p, i, q, resolve, random);
    }

    public static GameRuleResult TryBuyCard(GameStateSnapshot s, string playerId, string definitionId,
        Func<string, ExtensionCardData> resolve, System.Random random = null)
    {
        if (s == null || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(definitionId) || resolve == null) return GameRuleResult.Rejected("Invalid buy request.");
        if (s.Phase != BuyPhase) return GameRuleResult.Rejected("Cards can only be bought during the Buy phase.");
        PlayerStateSnapshot p = Player(s, playerId); if (p == null || p.Buys <= 0) return GameRuleResult.Rejected("Player was not found or has no Buys.");
        ExtensionCardData d = resolve(definitionId); int effectiveCost = CostRules.GetEffectiveCost(s, d);
        if (d == null || effectiveCost < 0 || effectiveCost > p.Coins) return GameRuleResult.Rejected("Card definition/cost is invalid for this purchase.");
        if (!GainRules.CanGainFromSupply(s, definitionId, out string gainCheckErr)) return GameRuleResult.Rejected(gainCheckErr);
        if (!ResolutionQueue.TryBegin(s, playerId, out ResolutionQueue q, out string err)) return GameRuleResult.Rejected(err);
        p.Coins -= effectiveCost; p.Buys--;
        if (!GainRules.TryGainFromSupply(s, p, definitionId, CardZone.Discard, 0, q.Events, out _, out string gainErr)) return GameRuleResult.Rejected(gainErr, q.Events.SnapshotHistory());
        TriggerResolutionResult tr = TriggerResolver.ResolvePending(q, s, resolve, random); List<GameEvent> events = q.Events.SnapshotHistory();
        if (tr.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected(tr.Error, events);
        if (tr.Status == EffectResolutionStatus.WaitingForChoice) return q.IsWaitingForDecision ? GameRuleResult.WaitingForChoice(events) : GameRuleResult.Rejected("Waiting trigger has no durable decision.", events);
        if (p.Buys <= 0) s.Phase = CleanupPhase;
        q.CompleteIfIdle(); return GameRuleResult.Applied(events);
    }

    public static GameRuleResult TrySubmitDecision(GameStateSnapshot s, string playerId, string decisionId,
        int[] selected, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (!PrepareResume(s, playerId, decisionId, resolve, out ResolutionQueue q, out GameRuleResult rejected)) return rejected;
        if (!q.TrySubmitDecision(playerId, decisionId, selected, out PendingDecisionSnapshot c, out string err)) return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
        PlayerStateSnapshot p = Player(s, playerId);
        if ((c.Operation ?? string.Empty).StartsWith(AttackReactionDrawDiscardPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveAttackReactionDrawDiscardDecision(s, p, q, c, resolve, random);
        if (AdvancedActionRules.IsContinuation(c.Operation))
        {
            GameRuleResult advanced = AdvancedActionRules.ResolveContinuation(s, p, q, c, resolve, random);
            if (advanced.Status != GameRuleStatus.Applied) return advanced;
            return ResumeDecision(s, q, c, resolve, random);
        }
        string operation = c.Operation ?? string.Empty;
        if (operation.StartsWith("reveal_each_other_top_trash_type_except|", StringComparison.OrdinalIgnoreCase))
            return ResolveRevealTrashDecision(s, p, q, c, resolve, random);
        if (!CardZoneRules.TryParseZone(c.Zone, out CardZone choiceZone)) return GameRuleResult.Rejected("Decision source zone is invalid.", q.Events.SnapshotHistory());
        List<int> source = CardZoneRules.ResolveZone(s, p, choiceZone); if (source == null) return GameRuleResult.Rejected("Decision source zone is unavailable.", q.Events.SnapshotHistory());
        foreach (int id in q.SelectedInstanceIds) if (!source.Contains(id)) return GameRuleResult.Rejected("Selected card is no longer in the decision source zone.", q.Events.SnapshotHistory());
        if (Eq(c.Operation, "block_attack_reaction")) return ResolveAttackReactionDecision(s, p, q, c, resolve, random);
        if (Eq(c.Operation, "discard_down_to")) return ResolveDiscardDownDecision(s, p, q, c, resolve, random);
        if (operation.StartsWith("choose_each_other_cards|", StringComparison.OrdinalIgnoreCase) ||
            operation.StartsWith("choose_each_other_cards_reveal|", StringComparison.OrdinalIgnoreCase))
            return ResolveEachOtherCardDecision(s, p, q, c, resolve, random);
        return ResumeDecision(s, q, c, resolve, random);
    }

    public static GameRuleResult TrySubmitSupplyDecision(GameStateSnapshot s, string playerId, string decisionId,
        string[] selected, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (!PrepareResume(s, playerId, decisionId, resolve, out ResolutionQueue q, out GameRuleResult rejected)) return rejected;
        if (!q.TrySubmitSupplyDecision(playerId, decisionId, selected, out PendingDecisionSnapshot c, out string err)) return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
        foreach (string id in q.SelectedDefinitionIds)
        {
            SupplyPileSnapshot pile = s.SupplyPiles != null ? s.SupplyPiles.Find(x => x != null && Eq(x.DefinitionId, id)) : null;
            if (pile == null || pile.RemainingCount <= 0) return GameRuleResult.Rejected("Selected supply pile is no longer available: " + id, q.Events.SnapshotHistory());
        }
        if (AdvancedActionRules.IsSupplyContinuation(c.Operation))
        {
            PlayerStateSnapshot actor = Player(s, s.Resolution.OwnerPlayerId);
            GameRuleResult advanced = AdvancedActionRules.ResolveSupplyContinuation(s, actor, q, c, resolve, random);
            if (advanced.Status != GameRuleStatus.Applied) return advanced;
            return ResumeDecision(s, q, c, resolve, random);
        }
        return ResumeDecision(s, q, c, resolve, random);
    }

    public static GameRuleResult TrySubmitOptionDecision(GameStateSnapshot s, string playerId, string decisionId,
        string[] selected, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (!PrepareResume(s, playerId, decisionId, resolve, out ResolutionQueue q, out GameRuleResult rejected)) return rejected;
        if (!q.TrySubmitOptionDecision(playerId, decisionId, selected, out PendingDecisionSnapshot c, out string err))
            return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
        if (AdvancedActionRules.IsOptionContinuation(c.Operation))
        {
            PlayerStateSnapshot responder = Player(s, playerId);
            GameRuleResult advanced = AdvancedActionRules.ResolveOptionContinuation(s, responder, q, c, resolve, random);
            if (advanced.Status != GameRuleStatus.Applied) return advanced;
        }
        return ResumeDecision(s, q, c, resolve, random);
    }

    internal static GameRuleResult TryStartAttackReactions(GameStateSnapshot s, PlayerStateSnapshot attacker, CardInstance attack,
        ExtensionCardData definition, ResolutionQueue q, Func<string, ExtensionCardData> resolve, GameEvent triggerEvent = null,
        string timing = null, int listenerCardInstanceId = 0, int abilityIndex = -1, int effectIndex = -1)
    {
        if (s.Players == null || s.Players.Count <= 1) return null;
        List<PlayerStateSnapshot> targets = new List<PlayerStateSnapshot>(); List<List<int>> candidates = new List<List<int>>();
        foreach (PlayerStateSnapshot p in s.Players)
        {
            if (p == null || p.PlayerId == attacker.PlayerId) continue;
            List<int> c = ReactionRules.FindAttackReactionCandidates(s, p, definition, resolve); if (c.Count == 0) continue;
            targets.Add(p); candidates.Add(c);
        }
        if (targets.Count == 0) return null;
        List<string> remaining = new List<string>(); for (int i = 1; i < targets.Count; i++) remaining.Add(targets[i].PlayerId);
        if (!q.TrySuspendForAttackReaction(targets[0].PlayerId, "Vous pouvez révéler une Réaction à cette Attaque.",
            attack.InstanceId, candidates[0], remaining, triggerEvent, timing, listenerCardInstanceId > 0 ? listenerCardInstanceId : attack.InstanceId,
            abilityIndex, effectIndex, out string err)) return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
        return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
    }

    internal static GameRuleResult TryStartRevealTrashForOthers(GameStateSnapshot s, PlayerStateSnapshot attacker, ResolutionQueue q,
        int revealCount, string cardType, string excludedCardId, string prompt, int sourceCardInstanceId, GameEvent triggerEvent,
        string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex, System.Random random)
    {
        if (s == null || attacker == null || q == null || revealCount <= 0 || string.IsNullOrWhiteSpace(cardType))
            return GameRuleResult.Rejected("Invalid revealed-card attack request.", q != null ? q.Events.SnapshotHistory() : null);
        List<string> targets = new List<string>();
        if (s.Players != null) foreach (PlayerStateSnapshot p in s.Players)
            if (p != null && p.PlayerId != attacker.PlayerId && !q.IsAttackProtected(p.PlayerId)) targets.Add(p.PlayerId);
        return TrySuspendNextRevealTrash(s, q, targets, revealCount, cardType, excludedCardId, prompt, sourceCardInstanceId,
            triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex, random);
    }

    private static GameRuleResult ResolveAttackReactionDecision(GameStateSnapshot s, PlayerStateSnapshot responder, ResolutionQueue q,
        PendingDecisionSnapshot c, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        List<int> selected = q.TakeSelectedInstanceIds();
        if (selected.Count > 0)
        {
            foreach (int id in selected) JournalRules.RecordReveal(s, responder, id);
            CardInstance reaction = Card(s, selected[0]); CardInstance attackCard = Card(s, c.SourceCardInstanceId);
            ExtensionCardData reactionDefinition = reaction != null ? resolve(reaction.DefinitionId) : null;
            ExtensionCardData attackDefinition = attackCard != null ? resolve(attackCard.DefinitionId) : null;
            if (!ReactionRules.TryGetAttackReactionEffect(reactionDefinition, attackDefinition, responder.Hand.Count, out CardEffectData effect))
                return GameRuleResult.Rejected("Selected Attack reaction is no longer valid.", q.Events.SnapshotHistory());
            if (string.Equals(effect.op, ReactionRules.BlockAttackOperation, StringComparison.OrdinalIgnoreCase))
                q.MarkAttackProtected(responder.PlayerId);
            else if (string.Equals(effect.op, ReactionRules.SetAsideAndPlayNextTurnOperation, StringComparison.OrdinalIgnoreCase))
            {
                if (!SetAsideRules.TryScheduleFromZone(s, responder, CardZone.Hand, reaction.InstanceId,
                        c.SourceCardInstanceId, SetAsideRules.PlayAtTurnStart, out string setAsideError))
                    return GameRuleResult.Rejected(setAsideError, q.Events.SnapshotHistory());
            }
            else
            {
                if (effect.amount < 0 || effect.max < 0)
                    return GameRuleResult.Rejected("Invalid draw/discard reaction.", q.Events.SnapshotHistory());
                if (!CardZoneRules.DrawCards(responder, effect.amount, random, out string drawError))
                    return GameRuleResult.Rejected(drawError, q.Events.SnapshotHistory());
                List<string> remainingForReaction = c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>();
                int required = Math.Min(effect.max, responder.Hand != null ? responder.Hand.Count : 0);
                string operation = AttackReactionDrawDiscardPrefix + c.SourceCardInstanceId;
                if (!q.TrySuspendForDecision(responder.PlayerId, operation, "hand", "Défaussez " + required + " cartes.",
                        c.SourceCardInstanceId, required, required, responder.Hand, RestoreEvent(c), c.Timing,
                        c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string suspendError))
                    return GameRuleResult.Rejected(suspendError, q.Events.SnapshotHistory());
                q.PendingDecision.RemainingPlayerIds.AddRange(remainingForReaction);
                return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
            }
        }
        CardInstance attack = Card(s, c.SourceCardInstanceId); if (attack == null) return GameRuleResult.Rejected("Attack card was not found while resuming reactions.", q.Events.SnapshotHistory());
        PlayerStateSnapshot attacker = Player(s, attack.OwnerPlayerId); ExtensionCardData d = resolve(attack.DefinitionId);
        if (attacker == null || d == null || !CardDefinitionRules.HasType(d, "Attaque")) return GameRuleResult.Rejected("Attack continuation is invalid.", q.Events.SnapshotHistory());
        List<string> remaining = c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>();
        return ContinueAttackReactions(s, q, c, attack, attacker, d, remaining, resolve, random);
    }

    private static GameRuleResult ResolveAttackReactionDrawDiscardDecision(GameStateSnapshot s, PlayerStateSnapshot responder,
        ResolutionQueue q, PendingDecisionSnapshot c, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (!DiscardRules.TryDiscardSelectedFromHand(s, responder, q.TakeSelectedInstanceIds(), c.SourceCardInstanceId,
                q.Events, out string discardError)) return GameRuleResult.Rejected(discardError, q.Events.SnapshotHistory());
        if (!int.TryParse((c.Operation ?? string.Empty).Substring(AttackReactionDrawDiscardPrefix.Length), out int attackId))
            return GameRuleResult.Rejected("Attack reaction continuation is invalid.", q.Events.SnapshotHistory());
        CardInstance attack = Card(s, attackId); PlayerStateSnapshot attacker = attack != null ? Player(s, attack.OwnerPlayerId) : null;
        ExtensionCardData definition = attack != null ? resolve(attack.DefinitionId) : null;
        if (attack == null || attacker == null || definition == null) return GameRuleResult.Rejected("Attack could not resume after reaction.", q.Events.SnapshotHistory());
        List<string> remaining = c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>();
        return ContinueAttackReactions(s, q, c, attack, attacker, definition, remaining, resolve, random);
    }

    private static GameRuleResult ContinueAttackReactions(GameStateSnapshot s, ResolutionQueue q, PendingDecisionSnapshot c,
        CardInstance attack, PlayerStateSnapshot attacker, ExtensionCardData d, List<string> remaining,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        while (remaining.Count > 0)
        {
            string id = remaining[0]; remaining.RemoveAt(0); PlayerStateSnapshot p = Player(s, id); if (p == null) continue;
            List<int> cand = ReactionRules.FindAttackReactionCandidates(s, p, d, resolve); if (cand.Count == 0) continue;
            if (!q.TrySuspendForAttackReaction(p.PlayerId, c.Prompt, attack.InstanceId, cand, remaining, RestoreEvent(c), c.Timing,
                c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err)) return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
            return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
        }
        if (c.TriggerEvent != null && c.AbilityIndex >= 0 && c.EffectIndex >= 0)
        {
            q.Events.Publish(GameEvent.CardPlayed(attacker.PlayerId, attack.InstanceId, attack.DefinitionId));
            return ResumeDecision(s, q, c, resolve, random);
        }
        return ResolvePlayedCard(s, attacker, attack, q, resolve, random);
    }

    private static GameRuleResult ResolvePlayedCard(GameStateSnapshot s, PlayerStateSnapshot p, CardInstance i, ResolutionQueue q,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        q.Events.Publish(GameEvent.CardPlayed(p.PlayerId, i.InstanceId, i.DefinitionId));
        TriggerResolutionResult tr = TriggerResolver.ResolvePending(q, s, resolve, random); List<GameEvent> events = q.Events.SnapshotHistory();
        if (tr.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected("Could not resolve CardPlayed triggers for " + i.DefinitionId + ": " + tr.Error, events);
        if (tr.Status == EffectResolutionStatus.WaitingForChoice) return q.IsWaitingForDecision ? GameRuleResult.WaitingForChoice(events) : GameRuleResult.Rejected("Waiting trigger has no durable decision.", events);
        if (tr.AbilitiesMatched == 0 || tr.EffectsResolved == 0) return GameRuleResult.Rejected("Card has no resolvable declarative play effects yet: " + i.DefinitionId, events);
        q.CompleteIfIdle(); return GameRuleResult.Applied(events);
    }

    private static GameRuleResult ResolveDiscardDownDecision(GameStateSnapshot s, PlayerStateSnapshot responder, ResolutionQueue q,
        PendingDecisionSnapshot c, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        if (!DiscardRules.TryDiscardSelectedFromHand(s, responder, q.TakeSelectedInstanceIds(), c.SourceCardInstanceId, q.Events, out string err)) return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
        List<string> remaining = c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>();
        while (remaining.Count > 0)
        {
            string id = remaining[0]; remaining.RemoveAt(0); PlayerStateSnapshot p = Player(s, id);
            if (p == null || p.Hand == null || p.Hand.Count <= c.TargetHandSize) continue;
            if (!q.TrySuspendForDiscardDownDecision(p.PlayerId, c.Prompt, c.SourceCardInstanceId, c.TargetHandSize, p.Hand, remaining,
                RestoreEvent(c), c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string suspendErr)) return GameRuleResult.Rejected(suspendErr, q.Events.SnapshotHistory());
            return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
        }
        return ResumeDecision(s, q, c, resolve, random);
    }

    private static GameRuleResult ResolveEachOtherCardDecision(GameStateSnapshot s, PlayerStateSnapshot responder, ResolutionQueue q,
        PendingDecisionSnapshot c, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        string[] parts = (c.Operation ?? string.Empty).Split('|');
        if (parts.Length != 4 || !CardZoneRules.TryParseZone(c.Zone, out CardZone src) || !CardZoneRules.TryParseZone(parts[3], out CardZone dst) || src == dst)
            return GameRuleResult.Rejected("Opponent card-choice continuation is invalid.", q.Events.SnapshotHistory());
        bool publicReveal = Eq(parts[0], "choose_each_other_cards_reveal");
        string cardId = parts[1], cardType = parts[2];
        List<int> selected = q.TakeSelectedInstanceIds();
        foreach (int id in selected)
        {
            if (publicReveal) JournalRules.RecordReveal(s, responder, id);
            if (!CardZoneRules.MoveCard(responder, src, dst, id)) return GameRuleResult.Rejected("Chosen card could not be moved.", q.Events.SnapshotHistory());
        }
        List<string> remaining = c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>();
        while (remaining.Count > 0)
        {
            string nextId = remaining[0]; remaining.RemoveAt(0); PlayerStateSnapshot p = Player(s, nextId); if (p == null) continue;
            List<int> cand = Eligible(s, p, src, cardId, cardType, resolve);
            if (cand.Count < c.MinSelections)
            {
                if (publicReveal) JournalRules.RecordRevealZone(s, p, src);
                continue;
            }
            if (!q.TrySuspendForDecision(p.PlayerId, c.Operation, c.Zone, c.Prompt, c.SourceCardInstanceId, c.MinSelections,
                Math.Min(c.MaxSelections, cand.Count), cand, RestoreEvent(c), c.Timing, c.ListenerCardInstanceId, c.AbilityIndex, c.EffectIndex, out string err))
                return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
            q.PendingDecision.RemainingPlayerIds.AddRange(remaining);
            return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
        }
        return ResumeDecision(s, q, c, resolve, random);
    }

    private static GameRuleResult ResolveRevealTrashDecision(GameStateSnapshot s, PlayerStateSnapshot responder, ResolutionQueue q,
        PendingDecisionSnapshot c, Func<string, ExtensionCardData> resolve, System.Random random)
    {
        string[] parts = (c.Operation ?? string.Empty).Split('|');
        if (parts.Length != 4 || !int.TryParse(parts[1], out int revealCount) || revealCount <= 0 || responder == null)
            return GameRuleResult.Rejected("Revealed-card continuation is invalid.", q.Events.SnapshotHistory());
        List<int> revealed = new List<int>();
        if (c.CandidateDefinitionIds != null) foreach (string raw in c.CandidateDefinitionIds) if (int.TryParse(raw, out int id)) revealed.Add(id);
        List<int> selected = q.TakeSelectedInstanceIds();
        if (selected.Count != 1 || !revealed.Contains(selected[0])) return GameRuleResult.Rejected("Revealed-card trash selection is invalid.", q.Events.SnapshotHistory());
        int trashedId = selected[0]; CardInstance trashed = Card(s, trashedId); if (trashed == null) return GameRuleResult.Rejected("Revealed trash card was not found.", q.Events.SnapshotHistory());
        if (s.TrashedCards == null) s.TrashedCards = new List<int>(); s.TrashedCards.Add(trashedId);
        q.Events.Publish(GameEvent.CardTrashed(responder.PlayerId, trashedId, trashed.DefinitionId, c.SourceCardInstanceId));
        foreach (int id in revealed)
        {
            if (id == trashedId) continue; CardInstance card = Card(s, id); if (card == null) return GameRuleResult.Rejected("Revealed discard card was not found.", q.Events.SnapshotHistory());
            if (responder.Discard == null) responder.Discard = new List<int>(); responder.Discard.Add(id);
            q.Events.Publish(GameEvent.CardDiscarded(responder.PlayerId, id, card.DefinitionId, c.SourceCardInstanceId));
        }
        GameRuleResult next = TrySuspendNextRevealTrash(s, q, c.RemainingPlayerIds != null ? new List<string>(c.RemainingPlayerIds) : new List<string>(),
            revealCount, parts[2], parts[3], c.Prompt, c.SourceCardInstanceId, RestoreEvent(c), c.Timing, c.ListenerCardInstanceId,
            c.AbilityIndex, c.EffectIndex, random);
        if (next.Status != GameRuleStatus.Applied) return next;
        return ResumeDecision(s, q, c, resolve, random);
    }

    private static GameRuleResult TrySuspendNextRevealTrash(GameStateSnapshot s, ResolutionQueue q, List<string> remaining,
        int revealCount, string cardType, string excludedCardId, string prompt, int sourceCardInstanceId, GameEvent triggerEvent,
        string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex, System.Random random)
    {
        remaining = remaining ?? new List<string>();
        while (remaining.Count > 0)
        {
            string playerId = remaining[0]; remaining.RemoveAt(0); PlayerStateSnapshot p = Player(s, playerId); if (p == null) continue;
            if (!TryTakeTopCards(p, revealCount, random, out List<int> revealed, out string revealError)) return GameRuleResult.Rejected(revealError, q.Events.SnapshotHistory());
            foreach (int id in revealed) JournalRules.RecordReveal(s, p, id);
            List<int> candidates = new List<int>();
            foreach (int id in revealed)
            {
                CardInstance card = Card(s, id); ExtensionCardData definition = card != null ? ExtensionCatalog.FindCard(card.DefinitionId.Substring(0, card.DefinitionId.IndexOf(':')), card.DefinitionId.Substring(card.DefinitionId.IndexOf(':') + 1)) : null;
                if (card != null && definition != null && CardDefinitionRules.HasType(definition, cardType) && !Eq(card.DefinitionId, excludedCardId)) candidates.Add(id);
            }
            if (candidates.Count == 0)
            {
                if (!DiscardDetached(s, p, revealed, sourceCardInstanceId, q.Events, out string discardError)) return GameRuleResult.Rejected(discardError, q.Events.SnapshotHistory());
                continue;
            }
            string op = "reveal_each_other_top_trash_type_except|" + revealCount + "|" + cardType + "|" + (excludedCardId ?? string.Empty);
            if (!q.TrySuspendForDecision(p.PlayerId, op, "deck", string.IsNullOrWhiteSpace(prompt) ? "Choisissez une carte révélée à écarter." : prompt,
                sourceCardInstanceId, 1, 1, candidates, triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex, out string err))
                return GameRuleResult.Rejected(err, q.Events.SnapshotHistory());
            q.PendingDecision.RemainingPlayerIds.AddRange(remaining);
            foreach (int id in revealed) q.PendingDecision.CandidateDefinitionIds.Add(id.ToString());
            return GameRuleResult.WaitingForChoice(q.Events.SnapshotHistory());
        }
        return GameRuleResult.Applied(q.Events.SnapshotHistory());
    }

    private static bool TryTakeTopCards(PlayerStateSnapshot p, int count, System.Random random, out List<int> revealed, out string error)
    {
        revealed = new List<int>(); error = string.Empty;
        if (p == null || p.Deck == null || p.Discard == null || count < 0) { error = "Reveal requires a valid player, deck and discard."; return false; }
        for (int n = 0; n < count; n++)
        {
            if (p.Deck.Count == 0)
            {
                if (p.Discard.Count == 0) break;
                if (random == null) { error = "Reveal requires an injected random source when the discard pile must be shuffled."; return false; }
                if (!CardZoneRules.MoveAll(p.Discard, p.Deck) || !CardZoneRules.Shuffle(p.Deck, random)) { error = "Could not reshuffle discard for reveal."; return false; }
            }
            int index = p.Deck.Count - 1; int id = p.Deck[index]; p.Deck.RemoveAt(index); revealed.Add(id);
        }
        return true;
    }

    private static bool DiscardDetached(GameStateSnapshot s, PlayerStateSnapshot owner, List<int> cards, int sourceCardInstanceId,
        GameEventBus events, out string error)
    {
        error = string.Empty; if (owner == null) { error = "Detached discard owner is missing."; return false; }
        if (owner.Discard == null) owner.Discard = new List<int>();
        if (cards == null) return true;
        foreach (int id in cards)
        {
            CardInstance card = Card(s, id); if (card == null) { error = "Detached discard card was not found."; return false; }
            owner.Discard.Add(id); events?.Publish(GameEvent.CardDiscarded(owner.PlayerId, id, card.DefinitionId, sourceCardInstanceId));
        }
        return true;
    }

    private static List<int> Eligible(GameStateSnapshot s, PlayerStateSnapshot p, CardZone z, string cardId, string cardType,
        Func<string, ExtensionCardData> resolve)
    {
        List<int> r = new List<int>(); List<int> source = CardZoneRules.ResolveZone(p, z); if (source == null) return r;
        foreach (int id in source)
        {
            CardInstance i = Card(s, id); if (i == null) continue;
            if (!string.IsNullOrWhiteSpace(cardId) && !Eq(i.DefinitionId, cardId)) continue;
            if (!string.IsNullOrWhiteSpace(cardType) && !CardDefinitionRules.HasType(resolve(i.DefinitionId), cardType)) continue;
            r.Add(id);
        }
        return r;
    }

    private static bool PrepareResume(GameStateSnapshot s, string playerId, string decisionId, Func<string, ExtensionCardData> resolve,
        out ResolutionQueue q, out GameRuleResult rejected)
    {
        q = null; rejected = null;
        if (s == null || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(decisionId) || resolve == null) { rejected = GameRuleResult.Rejected("Invalid decision resume request."); return false; }
        if (!ResolutionQueue.TryResume(s, out q, out string err)) { rejected = GameRuleResult.Rejected(err); return false; }
        if (Player(s, playerId) == null) { rejected = GameRuleResult.Rejected("Decision player was not found.", q.Events.SnapshotHistory()); return false; }
        return true;
    }

    private static GameRuleResult ResumeDecision(GameStateSnapshot s, ResolutionQueue q, PendingDecisionSnapshot c,
        Func<string, ExtensionCardData> resolve, System.Random random)
    {
        TriggerResolutionResult tr = TriggerResolver.ResumeSubjectDecision(q, c, s, resolve, random); List<GameEvent> events = q.Events.SnapshotHistory();
        if (tr.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected(tr.Error, events);
        if (tr.Status == EffectResolutionStatus.WaitingForChoice) return q.IsWaitingForDecision ? GameRuleResult.WaitingForChoice(events) : GameRuleResult.Rejected("Resumed trigger has no durable decision.", events);
        q.CompleteIfIdle(); return GameRuleResult.Applied(events);
    }

    private static GameEvent RestoreEvent(PendingDecisionSnapshot c) => c != null && c.TriggerEvent != null && c.TriggerEvent.TryToRuntime(out GameEvent e) ? e : null;
    private static string ValidatePlayPolicy(GameStateSnapshot s, PlayerStateSnapshot p, ExtensionCardData d, out bool consumes)
    {
        consumes = false;
        if (s.Phase == ActionPhase) { if (!CardDefinitionRules.HasType(d, "Action")) return "Only Action cards can be played during the Action phase."; if (p.Actions <= 0) return "No Actions remain."; consumes = true; return string.Empty; }
        if (s.Phase == BuyPhase) return CardDefinitionRules.HasType(d, "Trésor") ? string.Empty : "Only Treasure cards can be played during the Buy phase.";
        return "Cards cannot be played during phase: " + (s.Phase ?? string.Empty);
    }
    private static PlayerStateSnapshot Player(GameStateSnapshot s, string id) => s != null && s.Players != null ? s.Players.Find(x => x != null && x.PlayerId == id) : null;
    private static CardInstance Card(GameStateSnapshot s, int id) => s != null && s.CardInstances != null ? s.CardInstances.Find(x => x != null && x.InstanceId == id) : null;
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
