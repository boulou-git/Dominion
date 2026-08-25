using System;
using System.Collections.Generic;

/// <summary>
/// Rules for preventive reactions that must resolve before an Attack's normal CardPlayed effects.
/// This stays separate from post-event TriggerResolver listeners because prevention must be known
/// before any target-facing effect is applied.
/// </summary>
public static class ReactionRules
{
    public const string AttackReactionTiming = "attack_reaction";
    public const string BlockAttackOperation = "block_attack";

    public static List<int> FindBlockAttackCandidates(
        GameStateSnapshot state,
        PlayerStateSnapshot defender,
        ExtensionCardData attackDefinition,
        Func<string, ExtensionCardData> resolveCardDefinition)
    {
        List<int> result = new List<int>();
        if (state == null || defender == null || defender.Hand == null || attackDefinition == null || resolveCardDefinition == null)
            return result;
        if (!CardDefinitionRules.HasType(attackDefinition, "Attaque"))
            return result;

        foreach (int instanceId in defender.Hand)
        {
            CardInstance instance = FindCardInstance(state, instanceId);
            if (instance == null) continue;
            ExtensionCardData reactionDefinition = resolveCardDefinition(instance.DefinitionId);
            if (!CanBlockAttack(reactionDefinition, attackDefinition)) continue;
            result.Add(instanceId);
        }
        return result;
    }

    private static bool CanBlockAttack(ExtensionCardData reactionDefinition, ExtensionCardData attackDefinition)
    {
        if (reactionDefinition == null || reactionDefinition.abilities == null) return false;
        foreach (CardAbilityData ability in reactionDefinition.abilities)
        {
            if (ability == null || !string.Equals(ability.when, AttackReactionTiming, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(ability.scope) && !string.Equals(ability.scope, "in_hand", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!FilterMatches(ability.filter, attackDefinition)) continue;
            if (ability.effects == null) continue;
            foreach (CardEffectData effect in ability.effects)
                if (effect != null && string.Equals(effect.op, BlockAttackOperation, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }

    private static bool FilterMatches(CardTriggerFilterData filter, ExtensionCardData attackDefinition)
    {
        if (filter == null) return true;
        if (!string.IsNullOrWhiteSpace(filter.cardType) && !CardDefinitionRules.HasType(attackDefinition, filter.cardType))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.cardId))
        {
            if (!CardDefinitionReference.TryGetCardId(filter.cardId, out string requestedCardId))
                return false;
            if (!string.Equals(requestedCardId, attackDefinition.id ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null || instanceId <= 0) return null;
        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }
}
