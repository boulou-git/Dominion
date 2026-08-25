using System;

/// <summary>
/// Central Dominion end-condition rules. Conditions may become true during a turn,
/// but the match is only finalised at the cleanup/turn boundary.
/// </summary>
public static class GameEndRules
{
    public const string GameOverPhase = "GameOver";
    public const string ProvinceEmptyReason = "province_empty";
    public const string ThreePilesEmptyReason = "three_piles_empty";
    public const string ProvinceDefinitionId = "base:province";
    public const int EmptyPileThreshold = 3;

    public static bool TryGetEndReason(GameStateSnapshot state, out string reason)
    {
        reason = string.Empty;
        if (state == null || state.SupplyPiles == null)
            return false;

        SupplyPileSnapshot province = state.SupplyPiles.Find(pile =>
            pile != null && string.Equals(pile.DefinitionId, ProvinceDefinitionId, StringComparison.OrdinalIgnoreCase));
        if (province != null && province.RemainingCount <= 0)
        {
            reason = ProvinceEmptyReason;
            return true;
        }

        int emptyPiles = 0;
        foreach (SupplyPileSnapshot pile in state.SupplyPiles)
        {
            if (pile == null || pile.RemainingCount > 0)
                continue;
            emptyPiles++;
            if (emptyPiles >= EmptyPileThreshold)
            {
                reason = ThreePilesEmptyReason;
                return true;
            }
        }
        return false;
    }

    public static bool TryFinaliseAtTurnBoundary(GameStateSnapshot state)
    {
        if (state == null || state.IsGameOver)
            return state != null && state.IsGameOver;
        if (!TryGetEndReason(state, out string reason))
            return false;

        state.IsGameOver = true;
        state.GameEndReason = reason;
        state.EndedTurnNumber = state.TurnNumber;
        state.Phase = GameOverPhase;
        // Existing command validation already rejects gameplay when IsStarted is false.
        state.IsStarted = false;
        return true;
    }
}
