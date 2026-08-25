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

    // Durable match-end state. End conditions are evaluated at the end of the
    // active player's turn so they never interrupt an effect/decision mid-turn.
    public bool IsGameOver;
    public string GameEndReason;
    public int EndedTurnNumber;

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

    // Authoritative remaining card counts for every Reserve pile.
    // DefinitionId uses qualified refs such as "base:cuivre".
    public List<SupplyPileSnapshot> SupplyPiles = new List<SupplyPileSnapshot>();

    // Small replicated public journal. It intentionally stores semantic entries instead
    // of rendered text so every client can format/card-inspect them consistently.
    public int NextJournalSequence = 1;
    public List<GameJournalEntrySnapshot> Journal = new List<GameJournalEntrySnapshot>();

    // Durable bookkeeping for declarative abilities limited to one resolution per turn.
    // Keeping this in authoritative state makes the limit survive replication/reconnects.
    public List<AbilityUsageSnapshot> AbilityUsages = new List<AbilityUsageSnapshot>();

    // Durable in-progress rules resolution. Usually inactive/empty between commands, but
    // survives room replication when an effect must pause for a player's decision.
    public ResolutionQueueSnapshot Resolution = new ResolutionQueueSnapshot();

    // Player order is fixed once the match starts.
    public List<PlayerStateSnapshot> Players = new List<PlayerStateSnapshot>();
}

[Serializable]
public class GameJournalEntrySnapshot
{
    public int Sequence;
    public int TurnNumber;
    public string Kind;
    public string PlayerId;
    public string PlayerName;
    public string CardDefinitionId;
}

[Serializable]
public class AbilityUsageSnapshot
{
    public int CardInstanceId;
    public int AbilityIndex;
    public int TurnNumber;

    public AbilityUsageSnapshot()
    {
    }

    public AbilityUsageSnapshot(int cardInstanceId, int abilityIndex, int turnNumber)
    {
        CardInstanceId = cardInstanceId;
        AbilityIndex = abilityIndex;
        TurnNumber = turnNumber;
    }
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

    // Temporary cards privately inspected or set aside by an in-progress effect.
    // This is deliberately not a "revealed" zone: effects such as Soldat let only
    // the owning player look at these cards.
    public List<int> Inspected = new List<int>();

    public int Actions;
    public int Buys;
    public int Coins;
}
