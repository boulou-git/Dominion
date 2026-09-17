using System;

/// <summary>
/// Central cost calculation. General cost changes affect buys, gains and comparisons;
/// purchase-only penalties are layered on by GetPurchaseCost.
/// </summary>
public static class CostRules
{
    public static int GetEffectiveCost(GameStateSnapshot state, ExtensionCardData definition,
        Func<string, ExtensionCardData> resolve = null)
    {
        if (definition == null || definition.cost < 0) return -1;
        int reduction = 0;
        if (state != null && state.Players != null && !string.IsNullOrEmpty(state.ActivePlayerId))
        {
            PlayerStateSnapshot active = state.Players.Find(player =>
                player != null && string.Equals(player.PlayerId, state.ActivePlayerId, StringComparison.Ordinal));
            if (active != null)
            {
                reduction = Math.Max(0, active.CostReductionThisTurn);
            }
        }
        return Math.Max(0, definition.cost - reduction);
    }

    public static int GetPurchaseCost(GameStateSnapshot state, ExtensionCardData definition,
        Func<string, ExtensionCardData> resolve = null)
    {
        int effectiveCost = GetEffectiveCost(state, definition, resolve);
        if (effectiveCost < 0 || state == null || state.Players == null) return effectiveCost;
        PlayerStateSnapshot active = state.Players.Find(player => player != null &&
            string.Equals(player.PlayerId, state.ActivePlayerId, StringComparison.Ordinal));
        return active == null ? effectiveCost : effectiveCost + GetConditionalIncrease(state, active, resolve);
    }

    public static bool AddReductionForCurrentTurn(GameStateSnapshot state, PlayerStateSnapshot actor, int amount, out string error)
    {
        error = string.Empty;
        if (state == null || actor == null || amount < 0 ||
            !string.Equals(state.ActivePlayerId, actor.PlayerId, StringComparison.Ordinal))
        {
            error = "Cost reduction requires the active player and a non-negative amount.";
            return false;
        }
        actor.CostReductionThisTurn += amount;
        return true;
    }

    public static void ResetForTurn(PlayerStateSnapshot player)
    {
        if (player != null) player.CostReductionThisTurn = 0;
    }

    public static void AddConditionalModifier(PlayerStateSnapshot target, int sourceCardInstanceId,
        int amount, System.Collections.Generic.IEnumerable<string> matchingCardTypes)
    {
        if (target == null || sourceCardInstanceId <= 0 || amount <= 0 || matchingCardTypes == null) return;
        if (target.ConditionalCostModifiers == null)
            target.ConditionalCostModifiers = new System.Collections.Generic.List<ConditionalCostModifierSnapshot>();
        ConditionalCostModifierSnapshot added = new ConditionalCostModifierSnapshot
        {
            SourceCardInstanceId = sourceCardInstanceId,
            Amount = amount
        };
        foreach (string type in matchingCardTypes)
            if (!string.IsNullOrWhiteSpace(type)) added.MatchingCardTypes.Add(type.Trim());
        if (added.MatchingCardTypes.Count > 0) target.ConditionalCostModifiers.Add(added);
    }

    public static void RemoveConditionalModifiers(GameStateSnapshot state, int sourceCardInstanceId)
    {
        if (state == null || state.Players == null || sourceCardInstanceId <= 0) return;
        foreach (PlayerStateSnapshot player in state.Players)
            player?.ConditionalCostModifiers?.RemoveAll(modifier => modifier != null &&
                modifier.SourceCardInstanceId == sourceCardInstanceId);
    }

    private static int GetConditionalIncrease(GameStateSnapshot state, PlayerStateSnapshot active,
        Func<string, ExtensionCardData> resolve)
    {
        if (active.ConditionalCostModifiers == null || active.Hand == null || state.CardInstances == null) return 0;
        int increase = 0;
        foreach (ConditionalCostModifierSnapshot modifier in active.ConditionalCostModifiers)
        {
            if (modifier == null || modifier.Amount <= 0 || modifier.MatchingCardTypes == null ||
                !SourceIsInPlay(state, modifier.SourceCardInstanceId)) continue;
            bool matched = false;
            foreach (int instanceId in active.Hand)
            {
                CardInstance instance = state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
                ExtensionCardData card = instance != null ? Resolve(instance.DefinitionId, resolve) : null;
                foreach (string type in modifier.MatchingCardTypes)
                    if (CardDefinitionRules.HasType(card, type)) { matched = true; break; }
                if (matched) break;
            }
            if (matched) increase += modifier.Amount;
        }
        return Math.Max(0, increase);
    }

    private static bool SourceIsInPlay(GameStateSnapshot state, int sourceCardInstanceId)
    {
        if (state == null || state.Players == null || sourceCardInstanceId <= 0) return false;
        foreach (PlayerStateSnapshot player in state.Players)
            if (player != null && player.InPlay != null && player.InPlay.Contains(sourceCardInstanceId) &&
                (player.ResolvedDurationCards == null || !player.ResolvedDurationCards.Contains(sourceCardInstanceId)))
                return true;
        return false;
    }

    private static ExtensionCardData Resolve(string definitionId, Func<string, ExtensionCardData> resolve)
    {
        if (resolve != null) return resolve(definitionId);
        return RoomGameSetup.TryResolveCard(definitionId, out ExtensionPackageData extension,
            out ExtensionCardData definition) ? definition : null;
    }
}
