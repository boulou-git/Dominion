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

    // Authoritative remaining card counts for every Reserve pile.
    // DefinitionId uses qualified refs such as "base:cuivre".
    public List<SupplyPileSnapshot> SupplyPiles = new List<SupplyPileSnapshot>();

    // Shared Dominion trash zone ("Écartées" in the French UI).
    public List<int> TrashedCards = new List<int>();

    // At most one blocking player decision is active at a time for now.
    // Later this can evolve into a queue for simultaneous reactions/attacks.
    public PendingChoiceSnapshot PendingChoice;

    // Player order is fixed once the match starts.
    public List<PlayerStateSnapshot> Players = new List<PlayerStateSnapshot>();
}

[Serializable]
public class SupplyPileSnapshot
{
    public string DefinitionId;
    public int RemainingCount;

    public SupplyPileSnapshot()
    {
    }

    public SupplyPileSnapshot(string definitionId, int remainingCount)
    {
        DefinitionId = definitionId;
        RemainingCount = remainingCount;
    }
}

/// <summary>
/// Generic blocking choice created by card effects. ValidInstanceIds lets the UI apply
/// the same visual language everywhere: valid cards stay bright, invalid cards are dimmed.
/// </summary>
[Serializable]
public class PendingChoiceSnapshot
{
    public string ChoiceId;
    public string PlayerId;
    public string Kind;
    public string Prompt;
    public int SourceCardInstanceId;
    public int MinSelections;
    public int MaxSelections;
    public bool Optional;
    public List<int> ValidInstanceIds = new List<int>();
    public List<int> SelectedInstanceIds = new List<int>();

    public bool IsFor(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && string.Equals(PlayerId, playerId, StringComparison.Ordinal);
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
