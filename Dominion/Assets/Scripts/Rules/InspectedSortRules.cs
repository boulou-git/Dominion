using System;
using System.Collections.Generic;

[Serializable]
public sealed class InspectedSortPlan
{
    public string PlayerId;
    public int SourceId, ListenerId, AbilityIndex, FirstEffect, Stage;
    public string Timing;
    public List<int> Original = new List<int>();
    public List<int> Discard = new List<int>();
    public List<int> Deck = new List<int>();
}

/// <summary>One confirmed draft, executed through the original effects and reaction windows.</summary>
public static class InspectedSortRules
{
    public static bool CanSort(PendingDecisionSnapshot d, ExtensionCardData card)
    {
        if (d == null || d.Operation != "choose_cards" || d.Zone != "inspected" || d.MinSelections != 0 ||
            d.CandidateInstanceIds == null || d.CandidateInstanceIds.Count == 0 || card?.abilities == null ||
            d.AbilityIndex < 0 || d.AbilityIndex >= card.abilities.Count) return false;
        var e = card.abilities[d.AbilityIndex]?.effects;
        int i = d.EffectIndex;
        if (e == null || i < 0 || i + 4 >= e.Count) return false;
        for (int n = i; n <= i + 4; n++)
            if (e[n] == null || e[n].target != "self" || !string.IsNullOrEmpty(e[n].requiresSelectedOption)) return false;
        return e[i].op == "choose_cards" && e[i].zone == "inspected" && e[i].min == 0 &&
            e[i].max >= d.CandidateInstanceIds.Count && Unfiltered(e[i]) &&
            e[i + 1].op == "trash_selected" && e[i + 1].sourceZone == "inspected" &&
            e[i + 2].op == "choose_cards" && e[i + 2].zone == "inspected" && e[i + 2].min == 0 &&
            e[i + 2].max >= d.CandidateInstanceIds.Count && Unfiltered(e[i + 2]) &&
            e[i + 3].op == "discard_selected" && e[i + 3].sourceZone == "inspected" &&
            e[i + 4].op == "move_all_ordered" && e[i + 4].sourceZone == "inspected" && e[i + 4].destinationZone == "deck";
    }

    private static bool Unfiltered(CardEffectData e) => string.IsNullOrEmpty(e.cardType) &&
        string.IsNullOrEmpty(e.cardId) && string.IsNullOrEmpty(e.excludedCardType) &&
        string.IsNullOrEmpty(e.excludedCardId) && !e.lastMovedOnly;

    public static GameRuleResult Submit(GameStateSnapshot state, string playerId, string decisionId,
        int[] trash, int[] discard, int[] deck, Func<string, ExtensionCardData> resolve, Random random)
    {
        var d = state?.Resolution?.PendingDecision;
        int source = d != null && d.ListenerCardInstanceId > 0 ? d.ListenerCardInstanceId : d?.SourceCardInstanceId ?? 0;
        var card = state?.CardInstances?.Find(c => c != null && c.InstanceId == source);
        if (d == null || !d.IsPending || d.PlayerId != playerId || d.DecisionId != decisionId || card == null ||
            !CanSort(d, resolve(card.DefinitionId)) || trash == null || discard == null || deck == null)
            return GameRuleResult.Rejected("Invalid inspected-card sorting request.");
        var all = new List<int>(trash); all.AddRange(discard); all.AddRange(deck);
        if (!DeckChoiceRules.IsPermutation(d.CandidateInstanceIds, all))
            return GameRuleResult.Rejected("Every inspected card must occur in exactly one destination.");
        var plan = new InspectedSortPlan { PlayerId = playerId, SourceId = d.SourceCardInstanceId,
            ListenerId = d.ListenerCardInstanceId, AbilityIndex = d.AbilityIndex, FirstEffect = d.EffectIndex,
            Timing = d.Timing, Stage = 1, Original = new List<int>(all), Discard = new List<int>(discard), Deck = new List<int>(deck) };
        if (state.Resolution.SortPlans == null) state.Resolution.SortPlans = new List<InspectedSortPlan>();
        state.Resolution.SortPlans.Add(plan);
        JournalRules.RecordInstanceChoice(state, d, trash);
        var result = GameRules.TrySubmitDecision(state, playerId, decisionId, trash, resolve, random);
        if (result.Status == GameRuleStatus.Rejected) state.Resolution.SortPlans.Remove(plan);
        return result;
    }

    public static GameRuleResult Continue(GameStateSnapshot state, GameRuleResult result,
        Func<string, ExtensionCardData> resolve, Random random)
    {
        if (state?.Resolution?.SortPlans == null) return result;
        for (int guard = 0; guard < 64; guard++)
        {
            var plans = state.Resolution.SortPlans;
            if (!state.Resolution.IsActive) { plans.Clear(); return result; }
            var d = state.Resolution.PendingDecision;
            if (d == null || !d.IsPending) return result;
            bool advanced = false;
            for (int i = plans.Count - 1; i >= 0; i--)
            {
                var p = plans[i];
                var player = state.Players.Find(x => x.PlayerId == p.PlayerId);
                if (player == null || player.Inspected.Count == 0) { plans.RemoveAt(i); continue; }
                bool same = d.PlayerId == p.PlayerId && d.SourceCardInstanceId == p.SourceId &&
                    d.ListenerCardInstanceId == p.ListenerId && d.AbilityIndex == p.AbilityIndex && d.Timing == p.Timing;
                if (same && d.EffectIndex == p.FirstEffect) { plans.RemoveAt(i); continue; }
                if (!same) continue; // A reaction must still be answered by its owner.
                int expected = p.FirstEffect + (p.Stage == 1 ? 2 : 4);
                if (d.EffectIndex != expected) continue;
                string operation = p.Stage == 1 ? "choose_cards" : "move_all_ordered|deck";
                if (d.Zone != "inspected" || d.Operation != operation ||
                    d.CandidateInstanceIds.Exists(id => !p.Original.Contains(id)))
                { plans.RemoveAt(i); continue; } // Changed candidates require a fresh explicit choice.
                var ids = (p.Stage == 1 ? p.Discard : p.Deck).FindAll(id => d.CandidateInstanceIds.Contains(id));
                if (p.Stage == 2 && !DeckChoiceRules.IsPermutation(d.CandidateInstanceIds, ids))
                { plans.RemoveAt(i); continue; }
                if (p.Stage == 1) p.Stage = 2; else plans.RemoveAt(i);
                JournalRules.RecordInstanceChoice(state, d, ids.ToArray());
                result = GameRules.TrySubmitDecision(state, p.PlayerId, d.DecisionId, ids.ToArray(), resolve, random);
                if (result.Status == GameRuleStatus.Rejected) return result;
                JournalRules.RecordEvents(state, result.Events);
                advanced = true; break;
            }
            if (!advanced) return result;
        }
        return GameRuleResult.Rejected("Too many automatic sorting continuations.");
    }
}
