using System;

/// <summary>
/// Central cost calculation for turn-scoped modifiers. Every rule that buys,
/// gains or compares card costs must use this helper instead of the printed cost.
/// </summary>
public static class CostRules
{
    public static int GetEffectiveCost(GameStateSnapshot state, ExtensionCardData definition)
    {
        if (definition == null || definition.cost < 0) return -1;
        int reduction = 0;
        if (state != null && state.Players != null && !string.IsNullOrEmpty(state.ActivePlayerId))
        {
            PlayerStateSnapshot active = state.Players.Find(player =>
                player != null && string.Equals(player.PlayerId, state.ActivePlayerId, StringComparison.Ordinal));
            if (active != null) reduction = Math.Max(0, active.CostReductionThisTurn);
        }
        return Math.Max(0, definition.cost - reduction);
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
}
