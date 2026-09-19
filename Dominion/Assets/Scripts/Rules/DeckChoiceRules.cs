using System;
using System.Collections.Generic;

/// <summary>Recognizes a whole-group discard/deck branch from its declarative effects.</summary>
public static class DeckChoiceRules
{
    public static bool TryDescribe(PendingDecisionSnapshot choice, ExtensionCardData card,
        out string discardOption, out string deckOption, out int deckEffect)
    {
        discardOption = null; deckOption = null; deckEffect = -1;
        if (choice == null || choice.Operation != "choose_options" || choice.MinSelections != 1 ||
            choice.MaxSelections != 1 || choice.CandidateDefinitionIds == null || choice.CandidateDefinitionIds.Count != 2 ||
            choice.CandidateInstanceIds == null || choice.CandidateInstanceIds.Count == 0 ||
            card?.abilities == null || choice.AbilityIndex < 0 || choice.AbilityIndex >= card.abilities.Count) return false;
        List<CardEffectData> effects = card.abilities[choice.AbilityIndex]?.effects;
        if (effects == null || choice.EffectIndex < 0 || choice.EffectIndex + 2 >= effects.Count) return false;
        for (int i = choice.EffectIndex + 1; i <= choice.EffectIndex + 2; i++)
        {
            CardEffectData effect = effects[i];
            if (effect == null || effect.op != "move_all_ordered" || effect.target != "self" ||
                effect.sourceZone != "inspected" || !choice.CandidateDefinitionIds.Contains(effect.requiresSelectedOption)) return false;
            if (effect.destinationZone == "discard") discardOption = effect.requiresSelectedOption;
            else if (effect.destinationZone == "deck") { deckOption = effect.requiresSelectedOption; deckEffect = i; }
            else return false;
        }
        return discardOption != null && deckOption != null && discardOption != deckOption;
    }

    public static bool IsPermutation(IList<int> candidates, IList<int> ordered)
    {
        if (candidates == null || ordered == null || candidates.Count != ordered.Count) return false;
        var remaining = new HashSet<int>(candidates);
        if (remaining.Count != candidates.Count) return false;
        foreach (int id in ordered) if (!remaining.Remove(id)) return false;
        return remaining.Count == 0;
    }

    // Caller owns a transactional state clone. Only a successful result may be published.
    public static GameRuleResult Submit(GameStateSnapshot state, string playerId, string decisionId,
        string[] options, int[] ordered, Func<string, ExtensionCardData> resolve, Random random)
    {
        PendingDecisionSnapshot choice = state?.Resolution?.PendingDecision;
        int sourceId = choice != null && choice.ListenerCardInstanceId > 0 ? choice.ListenerCardInstanceId : choice?.SourceCardInstanceId ?? 0;
        CardInstance source = state?.CardInstances?.Find(c => c != null && c.InstanceId == sourceId);
        if (choice == null || choice.DecisionId != decisionId || choice.PlayerId != playerId || source == null ||
            !TryDescribe(choice, resolve(source.DefinitionId), out _, out string deckOption, out int deckEffect) ||
            options == null || options.Length != 1 || options[0] != deckOption || !IsPermutation(choice.CandidateInstanceIds, ordered))
            return GameRuleResult.Rejected("Invalid whole-group deck order.");
        int sourceCard = choice.SourceCardInstanceId, listener = choice.ListenerCardInstanceId, ability = choice.AbilityIndex;
        GameRuleResult result = GameRules.TrySubmitOptionDecision(state, playerId, decisionId, options, resolve, random);
        if (result.Status == GameRuleStatus.Rejected || ordered.Length <= 1) return result;
        PendingDecisionSnapshot next = state.Resolution.PendingDecision;
        if (next == null || !next.IsPending || next.PlayerId != playerId || next.SourceCardInstanceId != sourceCard ||
            next.ListenerCardInstanceId != listener || next.AbilityIndex != ability || next.EffectIndex != deckEffect ||
            next.Operation != "move_all_ordered|deck" || !IsPermutation(next.CandidateInstanceIds, ordered))
            return GameRuleResult.Rejected("Deck continuation changed; order was not applied.");
        return GameRules.TrySubmitDecision(state, playerId, next.DecisionId, ordered, resolve, random);
    }
}
