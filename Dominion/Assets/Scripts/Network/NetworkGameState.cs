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

    private const string CopperDefinitionId = "base:cuivre";
    private const string SilverDefinitionId = "base:argent";
    private const string GoldDefinitionId = "base:or";
    private const string EstateDefinitionId = "base:domaine";
    private const string DuchyDefinitionId = "base:duche";
    private const string ProvinceDefinitionId = "base:province";
    private const string CurseDefinitionId = "base:malediction";

    private const int StartingCopperCount = 7;
    private const int StartingEstateCount = 3;
    private const int StartingHandSize = 5;

    private const int TotalCopperCount = 60;
    private const int SilverSupplyCount = 40;
    private const int GoldSupplyCount = 30;

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

    public static CardInstance FindCardInstance(int instanceId)
    {
        return FindCardInstance(_state, instanceId);
    }

    public static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null)
            return null;

        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }

    public static SupplyPileSnapshot FindSupplyPile(string definitionId)
    {
        return FindSupplyPile(_state, definitionId);
    }

    public static SupplyPileSnapshot FindSupplyPile(GameStateSnapshot state, string definitionId)
    {
        if (state == null || state.SupplyPiles == null || string.IsNullOrEmpty(definitionId))
            return null;

        return state.SupplyPiles.Find(pile =>
            pile != null &&
            string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Creates the first authoritative snapshot for a match. Starter decks, the base
    /// Reserve and opening hands are generated here once, before the Game scene opens,
    /// so reconnecting never recreates or redraws them.
    /// </summary>
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
            IsInitialised = true,
            IsPaused = roomPlayers.Any(player => player.IsInactive),
            ManualPauseRequested = false,
            TurnNumber = 1,
            Phase = ActionPhase,
            NextCardInstanceId = 1
        };

        foreach (Player player in roomPlayers)
        {
            state.Players.Add(new PlayerStateSnapshot
            {
                PlayerId = GetPlayerId(player),
                ActorNumber = player.ActorNumber,
                NickName = player.NickName,
                IsConnected = !player.IsInactive,
                Actions = 1,
                Buys = 1,
                Coins = 0
            });
        }

        CreateBaseSupply(state, roomPlayers.Count);
        CreateStarterDecksAndOpeningHands(state);
        UpdatePauseState(state);

        PlayerStateSnapshot firstConnectedPlayer = state.Players.Find(player => player.IsConnected);
        state.ActivePlayerId = firstConnectedPlayer != null ? firstConnectedPlayer.PlayerId : state.Players[0].PlayerId;

        return CommitState(state);
    }

    public static bool MarkInitialised()
    {
        if (!CanWrite() || _state == null)
            return false;

        if (_state.IsInitialised)
            return true;

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

    private static void CreateBaseSupply(GameStateSnapshot state, int playerCount)
    {
        if (state == null)
            return;

        if (state.SupplyPiles == null)
            state.SupplyPiles = new List<SupplyPileSnapshot>();
        else
            state.SupplyPiles.Clear();

        int players = Math.Max(1, playerCount);
        int victoryPileSize = GetVictoryPileSize(players);

        AddSupplyPile(state, CopperDefinitionId, Math.Max(0, TotalCopperCount - (StartingCopperCount * players)));
        AddSupplyPile(state, SilverDefinitionId, SilverSupplyCount);
        AddSupplyPile(state, GoldDefinitionId, GoldSupplyCount);
        AddSupplyPile(state, EstateDefinitionId, victoryPileSize);
        AddSupplyPile(state, DuchyDefinitionId, victoryPileSize);
        AddSupplyPile(state, ProvinceDefinitionId, victoryPileSize);
        AddSupplyPile(state, CurseDefinitionId, Math.Max(0, 10 * (players - 1)));
    }

    private static int GetVictoryPileSize(int playerCount)
    {
        if (playerCount <= 2)
            return 8;
        if (playerCount <= 4)
            return 12;

        // Supports the project's larger Photon rooms while preserving the standard
        // 5-player/6-player progression of three Victory cards per player.
        return playerCount * 3;
    }

    private static void AddSupplyPile(GameStateSnapshot state, string definitionId, int remainingCount)
    {
        state.SupplyPiles.Add(new SupplyPileSnapshot(definitionId, Math.Max(0, remainingCount)));
    }

    private static void CreateStarterDecksAndOpeningHands(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return;

        System.Random random = new System.Random(Guid.NewGuid().GetHashCode());

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null)
                continue;

            player.Deck.Clear();
            player.Hand.Clear();
            player.Discard.Clear();
            player.InPlay.Clear();

            for (int i = 0; i < StartingCopperCount; i++)
                CreateOwnedCard(state, player, CopperDefinitionId);

            for (int i = 0; i < StartingEstateCount; i++)
                CreateOwnedCard(state, player, EstateDefinitionId);

            Shuffle(player.Deck, random);
            DrawCards(player, StartingHandSize);
        }
    }

    private static void CreateOwnedCard(GameStateSnapshot state, PlayerStateSnapshot owner, string definitionId)
    {
        int instanceId = state.NextCardInstanceId++;
        state.CardInstances.Add(new CardInstance(instanceId, definitionId, owner.PlayerId));
        owner.Deck.Add(instanceId);
    }

    private static void Shuffle(List<int> cards, System.Random random)
    {
        if (cards == null || random == null)
            return;

        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = cards[i];
            cards[i] = cards[j];
            cards[j] = temp;
        }
    }

    /// <summary>
    /// The top of a deck is the end of the list. Removing from the end avoids shifting
    /// the whole list and gives us one convention for every future draw operation.
    /// </summary>
    private static void DrawCards(PlayerStateSnapshot player, int count)
    {
        if (player == null || player.Deck == null || player.Hand == null)
            return;

        for (int i = 0; i < count && player.Deck.Count > 0; i++)
        {
            int topIndex = player.Deck.Count - 1;
            int instanceId = player.Deck[topIndex];
            player.Deck.RemoveAt(topIndex);
            player.Hand.Add(instanceId);
        }
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
        PlayerStateSnapshot nextPlayer = next.Players[nextIndex];
        next.ActivePlayerId = nextPlayer.PlayerId;
        next.TurnNumber++;
        next.Phase = ActionPhase;
        nextPlayer.Actions = 1;
        nextPlayer.Buys = 1;
        nextPlayer.Coins = 0;

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

        NormaliseCollections(state);
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

        NormaliseCollections(incoming);

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

        GameStateSnapshot clone = JsonUtility.FromJson<GameStateSnapshot>(JsonUtility.ToJson(state));
        NormaliseCollections(clone);
        return clone;
    }

    private static void NormaliseCollections(GameStateSnapshot state)
    {
        if (state == null)
            return;

        if (state.CardInstances == null)
            state.CardInstances = new List<CardInstance>();
        if (state.SupplyPiles == null)
            state.SupplyPiles = new List<SupplyPileSnapshot>();
        if (state.NextCardInstanceId < 1)
            state.NextCardInstanceId = state.CardInstances.Count > 0
                ? state.CardInstances.Max(card => card != null ? card.InstanceId : 0) + 1
                : 1;
        if (state.Players == null)
            state.Players = new List<PlayerStateSnapshot>();

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null)
                continue;

            if (player.Deck == null) player.Deck = new List<int>();
            if (player.Hand == null) player.Hand = new List<int>();
            if (player.Discard == null) player.Discard = new List<int>();
            if (player.InPlay == null) player.InPlay = new List<int>();
        }
    }
}
