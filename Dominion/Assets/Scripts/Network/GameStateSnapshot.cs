using System;
using System.Collections.Generic;

/// <summary>
/// Serializable snapshot of the authoritative Dominion game state.
/// Keep this class free of Unity scene references so it can survive reconnects,
/// Master Client migration, save/load and future replay support.
/// </summary>
[Serializable]
public class GameStateSnapshot
{
    public string MatchId;

    // Monotonic version of the entire authoritative state.
    public int Version;

    // Increments whenever Photon elects a new Master Client.
    public int AuthorityEpoch;

    public bool IsStarted;
    public bool IsInitialised;
    public bool IsPaused;
    public bool ManualPauseRequested;
    public string PauseReason;

    public string ActivePlayerId;
    public int TurnNumber;
    public string Phase = "Setup";

    // One registry for every physical card created during the match.
    // Player zones only store InstanceId values referencing this registry.
    public int NextCardInstanceId = 1;
    public List<CardInstance> CardInstances = new List<CardInstance>();

    // Cards removed from player decks by the Dominion "trash" operation.
    // The physical CardInstance remains in CardInstances for logs/replay/inspection.
    public List<int> TrashedCards = new List<int>();

    // Authoritative Reserve state. Cost/types are copied from the immutable card definition
    // when the match starts so deterministic rules can filter piles without reaching into UI
    // or extension-loading infrastructure during a resolution.
    public List<SupplyPileSnapshot> SupplyPiles = new List<SupplyPileSnapshot>();

    // Durable in-progress rules resolution. Usually inactive/empty between commands, but
    // survives room replication when an effect must pause for a player's decision.
    public ResolutionQueueSnapshot Resolution = new ResolutionQueueSnapshot();

    // Player order is fixed once the match starts.
    public List<PlayerStateSnapshot> Players = new List<PlayerStateSnapshot>();
}

[Serializable]
public class SupplyPileSnapshot
{
    public string DefinitionId;
    public int RemainingCount;
    public int Cost;
    public List<string> Types = new List<string>();

    public SupplyPileSnapshot()
    {
    }

    public SupplyPileSnapshot(string definitionId, int remainingCount, int cost = 0, IEnumerable<string> types = null)
    {
        DefinitionId = definitionId;
        RemainingCount = remainingCount;
        Cost = cost;
        if (types != null)
            Types.AddRange(types);
    }

    public bool HasType(string type)
    {
        if (Types == null || string.IsNullOrWhiteSpace(type))
            return false;

        foreach (string declaredType in Types)
            if (string.Equals(declaredType, type, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

[Serializable]
public class PlayerStateSnapshot
{
    // Stable identity used by game rules and reconnection.
    public string PlayerId;

    // Photon actor number is useful for diagnostics and current-session routing only.
    public int ActorNumber;
    public string NickName;
    public bool IsConnected = true;

    // All zones contain CardInstance.InstanceId values.
    public List<int> Deck = new List<int>();
    public List<int> Hand = new List<int>();
    public List<int> Discard = new List<int>();
    public List<int> InPlay = new List<int>();

    public int Actions;
    public int Buys;
    public int Coins;
}
