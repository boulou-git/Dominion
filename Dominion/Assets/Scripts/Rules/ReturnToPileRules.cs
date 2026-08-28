using System;

/// <summary>
/// Returns cards marked returnsToPileAfterPlay only after their complete CardPlayed
/// event has resolved. Special-pile cards keep their physical instance; Supply cards
/// restore the abstract pile count and release their owned instance.
/// </summary>
public static class ReturnToPileRules
{
    public static bool TryReturnAfterResolvedPlay(
        GameStateSnapshot state,
        GameEvent gameEvent,
        Func<string, ExtensionCardData> resolveCardDefinition,
        out string error)
    {
        error = string.Empty;
        if (gameEvent == null || gameEvent.Type != GameEventType.CardPlayed)
            return true;
        if (state == null || resolveCardDefinition == null)
        {
            error = "Return-to-pile resolution is missing its state or card resolver.";
            return false;
        }

        string definitionId = gameEvent.CardDefinitionId ?? string.Empty;
        ExtensionCardData definition = resolveCardDefinition(definitionId);
        if (definition == null)
        {
            error = "Return-to-pile card definition could not be resolved: " + definitionId;
            return false;
        }
        if (!definition.returnsToPileAfterPlay)
            return true;
        if (!HasDeclarativePlayEffect(definition))
            return true;
        if (!CardDefinitionRules.HasType(definition, "Consommable"))
        {
            error = "Card marked returnsToPileAfterPlay is not a Consommable: " + definitionId;
            return false;
        }

        PlayerStateSnapshot owner = state.Players != null
            ? state.Players.Find(player => player != null &&
                string.Equals(player.PlayerId, gameEvent.PlayerId, StringComparison.Ordinal))
            : null;
        CardInstance instance = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == gameEvent.CardInstanceId)
            : null;
        if (owner == null || instance == null ||
            !string.Equals(instance.OwnerPlayerId, owner.PlayerId, StringComparison.Ordinal))
        {
            error = "Played Consommable owner or physical instance is invalid: " + definitionId;
            return false;
        }
        if (owner.InPlay == null || !owner.InPlay.Contains(instance.InstanceId))
        {
            error = "Played Consommable is no longer in play when it should return: " + definitionId;
            return false;
        }

        if (TryFindConfiguredSpecialPile(definitionId, out string specialPileId))
            return SpecialPileRules.TryReturn(state, owner, instance.InstanceId, specialPileId, out error);

        SupplyPileSnapshot supply = state.SupplyPiles != null
            ? state.SupplyPiles.Find(pile => pile != null &&
                string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (supply == null)
        {
            error = "No Special or Kingdom pile can receive played Consommable: " + definitionId;
            return false;
        }

        owner.InPlay.Remove(instance.InstanceId);
        state.CardInstances.Remove(instance);
        supply.RemainingCount++;
        return true;
    }

    private static bool TryFindConfiguredSpecialPile(string definitionId, out string pileId)
    {
        pileId = string.Empty;
        if (!CardDefinitionReference.TryParseQualified(definitionId, out string extensionId, out string cardId))
            return false;
        ExtensionPackageData extension = ExtensionCatalog.Find(extensionId);
        if (extension == null || extension.specialPiles == null)
            return false;

        foreach (ExtensionSpecialPileData pile in extension.specialPiles)
        {
            if (pile == null || pile.cardIds == null)
                continue;
            foreach (string candidate in pile.cardIds)
            {
                if (!string.Equals(candidate, cardId, StringComparison.OrdinalIgnoreCase))
                    continue;
                pileId = CardDefinitionReference.Format(extensionId, pile.id);
                return !string.IsNullOrEmpty(pileId);
            }
        }
        return false;
    }

    private static bool HasDeclarativePlayEffect(ExtensionCardData definition)
    {
        if (definition == null || definition.abilities == null)
            return false;
        foreach (CardAbilityData ability in definition.abilities)
            if (ability != null &&
                string.Equals(ability.when, DeclarativeRuleVocabulary.PlayTiming, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(ability.scope) ||
                 string.Equals(ability.scope, DeclarativeRuleVocabulary.SubjectScope, StringComparison.OrdinalIgnoreCase)) &&
                ability.effects != null && ability.effects.Count > 0)
                return true;
        return false;
    }
}
