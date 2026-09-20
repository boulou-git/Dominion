using System;
using System.Collections.Generic;

/// <summary>
/// Detects an optional choice between one card in hand and one supply pile. The HUD can expose
/// both visible zones at once while the existing declarative effects remain authoritative.
/// </summary>
public static class DirectBoardBranchChoiceRules
{
    public sealed class Description
    {
        public string HandOptionId;
        public string SupplyOptionId;
        public CardEffectData HandChoice;
        public CardEffectData SupplyChoice;
        public int HandEffectIndex;
        public int SupplyEffectIndex;
    }

    public static bool TryDescribe(PendingDecisionSnapshot decision, ExtensionCardData source, out Description description)
    {
        description = null;
        if (decision == null || source?.abilities == null ||
            !string.Equals(decision.Zone, "options", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(decision.Operation, "choose_options", StringComparison.OrdinalIgnoreCase) ||
            decision.MinSelections != 0 || decision.MaxSelections != 1 ||
            decision.AbilityIndex < 0 || decision.AbilityIndex >= source.abilities.Count) return false;
        List<CardEffectData> effects = source.abilities[decision.AbilityIndex]?.effects;
        if (effects == null || decision.EffectIndex < 0 || decision.EffectIndex >= effects.Count) return false;

        CardEffectData optionEffect = effects[decision.EffectIndex];
        if (optionEffect?.options == null || optionEffect.options.Count != 2) return false;
        var result = new Description();
        for (int i = decision.EffectIndex + 1; i < effects.Count; i++)
        {
            CardEffectData effect = effects[i];
            if (effect == null || string.IsNullOrWhiteSpace(effect.requiresSelectedOption)) continue;
            if (effect.op == "choose_cards" && string.Equals(effect.zone, "hand", StringComparison.OrdinalIgnoreCase) &&
                effect.min == 1 && effect.max == 1 && result.HandChoice == null)
            {
                result.HandChoice = effect; result.HandOptionId = effect.requiresSelectedOption; result.HandEffectIndex = i;
            }
            else if (effect.op == "choose_supply" && effect.min == 1 && effect.max == 1 &&
                     !effect.useLastSelectionCost && result.SupplyChoice == null)
            {
                result.SupplyChoice = effect; result.SupplyOptionId = effect.requiresSelectedOption; result.SupplyEffectIndex = i;
            }
        }
        if (result.HandChoice == null || result.SupplyChoice == null ||
            string.Equals(result.HandOptionId, result.SupplyOptionId, StringComparison.OrdinalIgnoreCase) ||
            !HasOption(decision, result.HandOptionId) || !HasOption(decision, result.SupplyOptionId)) return false;
        description = result;
        return true;
    }

    public static List<int> HandCandidates(GameStateSnapshot state, PlayerStateSnapshot player,
        CardEffectData choice, Func<string, ExtensionCardData> resolve)
    {
        var result = new List<int>();
        if (state == null || player?.Hand == null || choice == null || resolve == null) return result;
        foreach (int id in player.Hand)
        {
            CardInstance card = NetworkGameState.FindCardInstance(state, id);
            ExtensionCardData definition = card != null ? resolve(card.DefinitionId) : null;
            if (!MatchesCard(state, card, definition, choice, resolve)) continue;
            result.Add(id);
        }
        return result;
    }

    public static List<string> SupplyCandidates(GameStateSnapshot state, CardEffectData choice,
        Func<string, ExtensionCardData> resolve)
    {
        var result = new List<string>();
        if (state?.SupplyPiles == null || choice == null || resolve == null) return result;
        foreach (SupplyPileSnapshot pile in state.SupplyPiles)
        {
            if (pile == null || pile.RemainingCount <= 0 || string.IsNullOrWhiteSpace(pile.DefinitionId)) continue;
            ExtensionCardData definition = resolve(pile.DefinitionId);
            if (definition == null || !MatchesDefinition(state, pile.DefinitionId, definition, choice, resolve)) continue;
            result.Add(pile.DefinitionId);
        }
        return result;
    }

    private static bool MatchesCard(GameStateSnapshot state, CardInstance card, ExtensionCardData definition,
        CardEffectData choice, Func<string, ExtensionCardData> resolve)
    {
        return card != null && definition != null && MatchesDefinition(state, card.DefinitionId, definition, choice, resolve);
    }

    private static bool MatchesDefinition(GameStateSnapshot state, string definitionId, ExtensionCardData definition,
        CardEffectData choice, Func<string, ExtensionCardData> resolve)
    {
        if (!string.IsNullOrWhiteSpace(choice.cardId) &&
            !string.Equals(choice.cardId, definitionId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(choice.excludedCardId) &&
            string.Equals(choice.excludedCardId, definitionId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(choice.cardType) && !CardDefinitionRules.HasType(definition, choice.cardType)) return false;
        if (!string.IsNullOrWhiteSpace(choice.excludedCardType) && CardDefinitionRules.HasType(definition, choice.excludedCardType)) return false;
        return choice.maxCost < 0 || CostRules.GetEffectiveCost(state, definition, resolve) <= choice.maxCost;
    }

    private static bool HasOption(PendingDecisionSnapshot decision, string optionId)
    {
        if (decision.CandidateDefinitionIds == null) return false;
        foreach (string candidate in decision.CandidateDefinitionIds)
            if (string.Equals(candidate, optionId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
