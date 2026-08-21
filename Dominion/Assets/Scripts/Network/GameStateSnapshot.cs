using System;
using System.Collections.Generic;

/// <summary>
/// Serializable snapshot of the authoritative Dominion game state.
/// Keep this class free of Unity scene references so it can be rebuilt after reconnects.
/// </summary>
[Serializable]
public class GameStateSnapshot
{
    public int Version;
    public int ActivePlayerActorNumber;
    public string Phase = "Setup";
    public List<PlayerStateSnapshot> Players = new List<PlayerStateSnapshot>();

    public void IncrementVersion()
    {
        Version++;
    }
}

[Serializable]
public class PlayerStateSnapshot
{
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
