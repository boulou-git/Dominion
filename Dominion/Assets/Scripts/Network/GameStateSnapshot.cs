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
    public string PauseReason;

    public string ActivePlayerId;
    public int TurnNumber;
    public string Phase = "Setup";

    // Player order is fixed once the match starts.
    public List<PlayerStateSnapshot> Players = new List<PlayerStateSnapshot>();
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

    // Card instance IDs will be stored here once the card runtime is implemented.
    public List<int> Deck = new List<int>();
    public List<int> Hand = new List<int>();
    public List<int> Discard = new List<int>();
    public List<int> InPlay = new List<int>();

    public int Actions;
    public int Buys;
    public int Coins;
}
