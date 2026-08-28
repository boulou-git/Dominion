using System;

/// <summary>
/// Local-only selection of the public player board currently displayed by this client.
/// Manual inspection remains stable during a turn; optional follow mode jumps to the
/// next active player only when ActivePlayerId actually changes.
/// </summary>
public sealed class PlayerBoardSelectionModel
{
    public string ViewedPlayerId { get; private set; } = string.Empty;
    public bool FollowActivePlayer { get; private set; } = true;

    private string _lastActivePlayerId = string.Empty;

    public bool Synchronise(GameStateSnapshot state)
    {
        string previous = ViewedPlayerId;
        string active = ResolveActiveOrFirstPlayerId(state);
        bool activeChanged = !string.Equals(_lastActivePlayerId, active, StringComparison.Ordinal);
        _lastActivePlayerId = active;

        if (!ContainsPlayer(state, ViewedPlayerId))
            ViewedPlayerId = active;
        else if (FollowActivePlayer && activeChanged && !string.IsNullOrEmpty(active))
            ViewedPlayerId = active;

        return !string.Equals(previous, ViewedPlayerId, StringComparison.Ordinal);
    }

    public bool SelectPlayer(GameStateSnapshot state, string playerId)
    {
        if (!ContainsPlayer(state, playerId) ||
            string.Equals(ViewedPlayerId, playerId, StringComparison.Ordinal))
            return false;
        ViewedPlayerId = playerId;
        return true;
    }

    public bool SetFollowActivePlayer(GameStateSnapshot state, bool follow)
    {
        bool changed = FollowActivePlayer != follow;
        FollowActivePlayer = follow;
        string active = ResolveActiveOrFirstPlayerId(state);
        _lastActivePlayerId = active;
        if (follow && !string.IsNullOrEmpty(active) &&
            !string.Equals(ViewedPlayerId, active, StringComparison.Ordinal))
        {
            ViewedPlayerId = active;
            changed = true;
        }
        return changed;
    }

    public PlayerStateSnapshot ResolvePlayer(GameStateSnapshot state)
    {
        Synchronise(state);
        return state != null && state.Players != null
            ? state.Players.Find(player => player != null &&
                string.Equals(player.PlayerId, ViewedPlayerId, StringComparison.Ordinal))
            : null;
    }

    private static bool ContainsPlayer(GameStateSnapshot state, string playerId)
    {
        return state != null && state.Players != null && !string.IsNullOrEmpty(playerId) &&
               state.Players.Exists(player => player != null &&
                   string.Equals(player.PlayerId, playerId, StringComparison.Ordinal));
    }

    private static string ResolveActiveOrFirstPlayerId(GameStateSnapshot state)
    {
        if (state == null || state.Players == null || state.Players.Count == 0)
            return string.Empty;
        if (ContainsPlayer(state, state.ActivePlayerId))
            return state.ActivePlayerId;
        PlayerStateSnapshot first = state.Players.Find(player => player != null && !string.IsNullOrEmpty(player.PlayerId));
        return first != null ? first.PlayerId : string.Empty;
    }
}
