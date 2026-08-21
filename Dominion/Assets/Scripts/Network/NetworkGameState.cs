using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Replicated Dominion game state stored in Photon room custom properties.
/// The Master Client is the only writer, but every client keeps the latest snapshot.
/// Because the snapshot lives on the room, it survives Master Client migration and is
/// automatically available to players that successfully rejoin the room.
/// </summary>
public static class NetworkGameState
{
    private const string StatePropertyKey = "dominion.gameState.v1";

    public const string ActionPhase = "Action";
    public const string BuyPhase = "Buy";
    public const string CleanupPhase = "Cleanup";

    private static GameStateSnapshot _state;

    public static event Action<GameStateSnapshot> StateChanged;

    public static GameStateSnapshot State => _state;
    public static int Version => _state != null ? _state.Version : 0;
    public static int AuthorityEpoch => _state != null ? _state.AuthorityEpoch : 0;
    public static bool IsStarted => _state != null && _state.IsStarted;
    public static bool IsPaused => _state != null && _state.IsPaused;

    public static string LocalPlayerId
    {
        get
        {
            if (PhotonNetwork.LocalPlayer != null)
            {
                string id = GetPlayerId(PhotonNetwork.LocalPlayer);
                if (!string.IsNullOrEmpty(id))
                    return id;
            }

            return PhotonNetwork.AuthValues != null ? PhotonNetwork.AuthValues.UserId : string.Empty;
        }
    }

    public static string GetPlayerId(Player player)
    {
        if (player == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(player.UserId))
            return player.UserId;

        return "actor:" + player.ActorNumber;
    }

    public static bool HydrateFromRoom(bool force = false)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return false;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(StatePropertyKey))
            return false;

        return ApplyJson(PhotonNetwork.CurrentRoom.CustomProperties[StatePropertyKey] as string, force);
    }

    public static bool ApplyRoomProperties(Hashtable changedProperties)
    {
        if (changedProperties == null || !changedProperties.ContainsKey(StatePropertyKey))
            return false;

        return ApplyJson(changedProperties[StatePropertyKey] as string, false);
    }

    public static void ResetLocalState()
    {
        _state = null;
        StateChanged?.Invoke(null);
    }

    public static bool InitialiseAuthoritativeState()
    {
        if (!CanWrite())
            return false;

        HydrateFromRoom(true);
        if (_state != null && _state.IsStarted)
            return false;

        List<Player> roomPlayers = PhotonNetwork.CurrentRoom.Players.Values
            .OrderBy(player => player.ActorNumber)
            .ToList();

        if (roomPlayers.Count == 0)
            return false;

        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = Guid.NewGuid().ToString("N"),
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = false,
            IsPaused = roomPlayers.Any(player => player.IsInactive),
            ManualPauseRequested = false,
            TurnNumber = 1,
            Phase = ActionPhase
        };

        foreach (Player player in roomPlayers)
        {
            state.Players.Add(new PlayerStateSnapshot
            {
                PlayerId = GetPlayerId(player),
                ActorNumber = player.ActorNumber,
                NickName = player.NickName,
                IsConnected = !player.IsInactive
            });
        }

        UpdatePauseState(state);

        PlayerStateSnapshot firstConnectedPlayer = state.Players.Find(player => player.IsConnected);
        state.ActivePlayerId = firstConnectedPlayer != null ? firstConnectedPlayer.PlayerId : state.Players[0].PlayerId;

        return CommitState(state);
    }

    public static bool MarkInitialised()
    {
        if (!CanWrite() || _state == null || _state.IsInitialised)
            return false;

        GameStateSnapshot next = Clone(_state);
        next.IsInitialised = true;
        return CommitState(next);
    }

    public static bool SetManualPause(bool paused)
    {
        if (!CanWrite() || _state == null || !_state.IsStarted)
            return false;

        if (_state.ManualPauseRequested == paused)
            return true;

        GameStateSnapshot next = Clone(_state);
        next.ManualPauseRequested = paused;
        UpdatePauseState(next);
        return CommitState(next);
    }

    /// <summary>
    /// A disconnected player remains in the immutable match player order. Any absence
    /// pauses the authoritative game until every player is connected again.
    /// </summary>
    public static bool SetPlayerConnectivity(Player photonPlayer, bool connected)
    {
        if (!CanWrite() || _state == null || photonPlayer == null)
            return false;

        GameStateSnapshot next = Clone(_state);
        string playerId = GetPlayerId(photonPlayer);
        PlayerStateSnapshot playerState = next.Players.Find(player => player.PlayerId == playerId);

        if (playerState == null)
            return false;

        bool changed = false;

        if (playerState.IsConnected != connected)
        {
            playerState.IsConnected = connected;
            changed = true;
        }

        if (playerState.ActorNumber != photonPlayer.ActorNumber)
        {
            playerState.ActorNumber = photonPlayer.ActorNumber;
            changed = true;
        }

        if (playerState.NickName != photonPlayer.NickName)
        {
            playerState.NickName = photonPlayer.NickName;
            changed = true;
        }

        bool previousPause = next.IsPaused;
        string previousReason = next.PauseReason;
        UpdatePauseState(next);

        if (previousPause != next.IsPaused || previousReason != next.PauseReason)
            changed = true;

        return changed && CommitState(next);
    }

    public static bool HandleMasterMigration()
    {
        if (!CanWrite())
            return false;

        HydrateFromRoom(true);
        if (_state == null)
            return false;

        GameStateSnapshot next = Clone(_state);
        next.AuthorityEpoch++;

        foreach (PlayerStateSnapshot playerState in next.Players)
        {
            Player photonPlayer = PhotonNetwork.CurrentRoom.Players.Values.FirstOrDefault(
                player => GetPlayerId(player) == playerState.PlayerId);

            bool connected = photonPlayer != null && !photonPlayer.IsInactive;
            playerState.IsConnected = connected;

            if (photonPlayer != null)
            {
                playerState.ActorNumber = photonPlayer.ActorNumber;
                playerState.NickName = photonPlayer.NickName;
            }
        }

        UpdatePauseState(next);
        return CommitState(next);
    }

    /// <summary>
    /// Advances Action -> Buy -> Cleanup. Advancing from Cleanup starts the next player's
    /// Action phase. Only the active player may request this and stale requests are rejected.
    /// </summary>
    public static bool TryAdvancePhase(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch))
            return false;

        GameStateSnapshot next = Clone(_state);

        switch (next.Phase)
        {
            case ActionPhase:
                next.Phase = BuyPhase;
                break;

            case BuyPhase:
                next.Phase = CleanupPhase;
                break;

            case CleanupPhase:
                return AdvanceToNextPlayer(next);

            default:
                return false;
        }

        return CommitState(next);
    }

    public static bool TryAdvanceTurn(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch))
            return false;

        return AdvanceToNextPlayer(Clone(_state));
    }

    private static bool ValidateActivePlayerCommand(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!CanWrite() || _state == null || !_state.IsStarted || _state.IsPaused)
            return false;

        if (_state.Version != expectedVersion || _state.AuthorityEpoch != expectedAuthorityEpoch)
            return false;

        return _state.ActivePlayerId == requesterPlayerId && _state.Players.Count > 0;
    }

    private static bool AdvanceToNextPlayer(GameStateSnapshot next)
    {
        int currentIndex = next.Players.FindIndex(player => player.PlayerId == next.ActivePlayerId);
        if (currentIndex < 0)
            return false;

        int nextIndex = (currentIndex + 1) % next.Players.Count;
        next.ActivePlayerId = next.Players[nextIndex].PlayerId;
        next.TurnNumber++;
        next.Phase = ActionPhase;

        return CommitState(next);
    }

    private static void UpdatePauseState(GameStateSnapshot state)
    {
        if (state == null)
            return;

        List<string> missingPlayers = state.Players
            .Where(player => !player.IsConnected)
            .Select(player => string.IsNullOrEmpty(player.NickName) ? "Joueur" : player.NickName)
            .ToList();

        if (missingPlayers.Count > 0)
        {
            state.IsPaused = true;
            state.PauseReason = "En attente de reconnexion : " + string.Join(", ", missingPlayers);
            return;
        }

        state.IsPaused = state.ManualPauseRequested;
        state.PauseReason = state.ManualPauseRequested ? "Partie mise en pause par l’hôte." : string.Empty;
    }

    private static bool CanWrite()
    {
        return PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.IsMasterClient;
    }

    private static bool CommitState(GameStateSnapshot state)
    {
        if (!CanWrite() || state == null)
            return false;

        GameStateSnapshot committed = Clone(state);
        int previousVersion = _state != null ? _state.Version : 0;
        committed.Version = Math.Max(previousVersion, committed.Version) + 1;

        string json = JsonUtility.ToJson(committed);
        Hashtable properties = new Hashtable
        {
            { StatePropertyKey, json }
        };

        bool queued = PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        if (!queued)
            return false;

        SetLocalState(committed);
        return true;
    }

    private static bool ApplyJson(string json, bool force)
    {
        if (string.IsNullOrEmpty(json))
            return false;

        GameStateSnapshot incoming = JsonUtility.FromJson<GameStateSnapshot>(json);
        if (incoming == null)
            return false;

        if (!force && _state != null && incoming.Version <= _state.Version)
            return false;

        SetLocalState(incoming);
        return true;
    }

    private static void SetLocalState(GameStateSnapshot state)
    {
        _state = Clone(state);
        StateChanged?.Invoke(_state);
    }

    private static GameStateSnapshot Clone(GameStateSnapshot state)
    {
        if (state == null)
            return null;

        return JsonUtility.FromJson<GameStateSnapshot>(JsonUtility.ToJson(state));
    }
}
