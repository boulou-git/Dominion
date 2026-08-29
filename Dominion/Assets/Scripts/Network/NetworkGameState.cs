using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

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
    private const int KingdomPileCount = 10;

    public const string ActionPhase = GameRules.ActionPhase;
    public const string BuyPhase = GameRules.BuyPhase;
    public const string CleanupPhase = GameRules.CleanupPhase;

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
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return PhotonNetwork.AuthValues != null ? PhotonNetwork.AuthValues.UserId : string.Empty;
        }
    }

    public static string GetPlayerId(Player player)
    {
        if (player == null) return string.Empty;
        if (!string.IsNullOrEmpty(player.UserId)) return player.UserId;
        return "actor:" + player.ActorNumber;
    }

    public static CardInstance FindCardInstance(int instanceId) => FindCardInstance(_state, instanceId);
    public static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null) return null;
        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }

    public static SupplyPileSnapshot FindSupplyPile(string definitionId) => FindSupplyPile(_state, definitionId);
    public static SupplyPileSnapshot FindSupplyPile(GameStateSnapshot state, string definitionId)
    {
        if (state == null || state.SupplyPiles == null || string.IsNullOrEmpty(definitionId)) return null;
        return state.SupplyPiles.Find(pile => pile != null && string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HydrateFromRoom(bool force = false)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return false;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(StatePropertyKey)) return false;
        return ApplyJson(PhotonNetwork.CurrentRoom.CustomProperties[StatePropertyKey] as string, force);
    }

    public static bool ApplyRoomProperties(Hashtable changedProperties)
    {
        if (changedProperties == null || !changedProperties.ContainsKey(StatePropertyKey)) return false;
        return ApplyJson(changedProperties[StatePropertyKey] as string, false);
    }

    public static void ResetLocalState()
    {
        _state = null;
        StateChanged?.Invoke(null);
    }

    public static bool InitialiseAuthoritativeState()
    {
        if (!CanWrite()) return false;
        HydrateFromRoom(true);
        if (_state != null && (_state.IsStarted || _state.IsGameOver)) return false;

        List<Player> roomPlayers = PhotonNetwork.CurrentRoom.Players.Values.OrderBy(player => player.ActorNumber).ToList();
        if (roomPlayers.Count == 0) return false;

        GameSetupConfig setup = RoomGameSetup.ReadCurrent();
        if (setup == null || setup.kingdomCardIds == null ||
            setup.kingdomCardIds.Count != RoomGameSetup.KingdomCardCount)
        {
            Debug.LogError("Cannot initialise the match before the 10 Kingdom cards are published.");
            return false;
        }

        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = Guid.NewGuid().ToString("N"), AuthorityEpoch = 1, IsStarted = true, IsInitialised = true,
            IsPaused = roomPlayers.Any(player => player.IsInactive), ManualPauseRequested = false,
            TurnNumber = 1, Phase = ActionPhase, NextCardInstanceId = 1
        };

        foreach (Player player in roomPlayers)
        {
            state.Players.Add(new PlayerStateSnapshot
            {
                PlayerId = GetPlayerId(player), ActorNumber = player.ActorNumber, NickName = player.NickName,
                IsConnected = !player.IsInactive, Actions = 1, Buys = 1, Coins = 0
            });
        }

        CreateSupply(state, roomPlayers.Count);
        CreateExtensionComponents(state, setup, roomPlayers.Count, NewRandom());
        if (!CreateStarterDecksAndOpeningHands(state)) return false;
        UpdatePauseState(state);
        PlayerStateSnapshot firstConnectedPlayer = state.Players.Find(player => player.IsConnected);
        state.ActivePlayerId = firstConnectedPlayer != null ? firstConnectedPlayer.PlayerId : state.Players[0].PlayerId;
        return CommitState(state);
    }

    public static bool MarkInitialised()
    {
        if (!CanWrite() || _state == null) return false;
        if (_state.IsInitialised) return true;
        GameStateSnapshot next = Clone(_state); next.IsInitialised = true; return CommitState(next);
    }

    public static bool SetManualPause(bool paused)
    {
        if (!CanWrite() || _state == null || !_state.IsStarted) return false;
        if (_state.ManualPauseRequested == paused) return true;
        GameStateSnapshot next = Clone(_state); next.ManualPauseRequested = paused; UpdatePauseState(next); return CommitState(next);
    }

    public static bool SetPlayerConnectivity(Player photonPlayer, bool connected)
    {
        if (!CanWrite() || _state == null || photonPlayer == null) return false;
        GameStateSnapshot next = Clone(_state);
        string playerId = GetPlayerId(photonPlayer);
        PlayerStateSnapshot playerState = next.Players.Find(player => player.PlayerId == playerId);
        if (playerState == null) return false;
        bool changed = false;
        if (playerState.IsConnected != connected) { playerState.IsConnected = connected; changed = true; }
        if (playerState.ActorNumber != photonPlayer.ActorNumber) { playerState.ActorNumber = photonPlayer.ActorNumber; changed = true; }
        if (playerState.NickName != photonPlayer.NickName) { playerState.NickName = photonPlayer.NickName; changed = true; }
        bool previousPause = next.IsPaused; string previousReason = next.PauseReason;
        UpdatePauseState(next);
        if (previousPause != next.IsPaused || previousReason != next.PauseReason) changed = true;
        return changed && CommitState(next);
    }

    public static bool HandleMasterMigration()
    {
        if (!CanWrite()) return false;
        HydrateFromRoom(true);
        if (_state == null) return false;
        GameStateSnapshot next = Clone(_state); next.AuthorityEpoch++;
        foreach (PlayerStateSnapshot playerState in next.Players)
        {
            Player photonPlayer = PhotonNetwork.CurrentRoom.Players.Values.FirstOrDefault(player => GetPlayerId(player) == playerState.PlayerId);
            playerState.IsConnected = photonPlayer != null && !photonPlayer.IsInactive;
            if (photonPlayer != null) { playerState.ActorNumber = photonPlayer.ActorNumber; playerState.NickName = photonPlayer.NickName; }
        }
        UpdatePauseState(next); return CommitState(next);
    }

    public static bool TryAdvancePhase(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch) =>
        TryAdvancePhase(requesterPlayerId, expectedVersion, expectedAuthorityEpoch, null);

    public static bool TryAdvancePhase(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch, int[] visualHandOrder)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        switch (next.Phase)
        {
            case ActionPhase: next.Phase = BuyPhase; return CommitState(next);
            case BuyPhase:
            case CleanupPhase:
                if (!TryApplyRequestedHandOrder(next, requesterPlayerId, visualHandOrder)) return false;
                if (!PerformCleanupAndAdvance(next)) return false;
                return CommitState(next);
            default: return false;
        }
    }

    public static bool TryAdvanceTurn(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        if (!PerformCleanupAndAdvance(next)) return false;
        return CommitState(next);
    }

    public static bool TryPlayCard(string requesterPlayerId, int instanceId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        GameRuleResult result = GameRules.TryPlayCard(next, requesterPlayerId, instanceId, ResolveCardDefinition, NewRandom());
        if (result.Status == GameRuleStatus.Rejected) { Debug.LogWarning("Rejected PlayCard command: " + result.Error); return false; }
        return CommitState(next);
    }

    public static bool TryBuyCard(string requesterPlayerId, string definitionId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateActivePlayerCommand(requesterPlayerId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        GameRuleResult result = GameRules.TryBuyCard(next, requesterPlayerId, definitionId, ResolveCardDefinition, NewRandom());
        if (result.Status == GameRuleStatus.Rejected) { Debug.LogWarning("Rejected BuyCard command: " + result.Error); return false; }
        return CommitState(next);
    }

    public static bool TrySubmitDecision(string requesterPlayerId, string decisionId, int[] selectedInstanceIds,
        int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateDecisionCommand(requesterPlayerId, decisionId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        GameRuleResult result = GameRules.TrySubmitDecision(next, requesterPlayerId, decisionId, selectedInstanceIds, ResolveCardDefinition, NewRandom());
        if (result.Status == GameRuleStatus.Rejected) { Debug.LogWarning("Rejected SubmitDecision command: " + result.Error); return false; }
        return CompleteCleanupAfterDecision(next, result) && CommitState(next);
    }

    public static bool TrySubmitSupplyDecision(string requesterPlayerId, string decisionId, string[] selectedDefinitionIds,
        int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateDecisionCommand(requesterPlayerId, decisionId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        GameRuleResult result = GameRules.TrySubmitSupplyDecision(next, requesterPlayerId, decisionId, selectedDefinitionIds, ResolveCardDefinition, NewRandom());
        if (result.Status == GameRuleStatus.Rejected) { Debug.LogWarning("Rejected SubmitSupplyDecision command: " + result.Error); return false; }
        return CompleteCleanupAfterDecision(next, result) && CommitState(next);
    }

    public static bool TrySubmitOptionDecision(string requesterPlayerId, string decisionId, string[] selectedOptionIds,
        int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!ValidateDecisionCommand(requesterPlayerId, decisionId, expectedVersion, expectedAuthorityEpoch)) return false;
        GameStateSnapshot next = Clone(_state);
        GameRuleResult result = GameRules.TrySubmitOptionDecision(next, requesterPlayerId, decisionId, selectedOptionIds, ResolveCardDefinition, NewRandom());
        if (result.Status == GameRuleStatus.Rejected) { Debug.LogWarning("Rejected SubmitOptionDecision command: " + result.Error); return false; }
        return CompleteCleanupAfterDecision(next, result) && CommitState(next);
    }

    private static void CreateSupply(GameStateSnapshot state, int playerCount)
    {
        if (state == null) return;
        state.SupplyPiles = new List<SupplyPileSnapshot>();
        int players = Math.Max(1, playerCount); int victoryPileSize = GetVictoryPileSize(players);
        AddSupplyPile(state, CopperDefinitionId, Math.Max(0, TotalCopperCount - (StartingCopperCount * players)));
        AddSupplyPile(state, SilverDefinitionId, SilverSupplyCount); AddSupplyPile(state, GoldDefinitionId, GoldSupplyCount);
        AddSupplyPile(state, EstateDefinitionId, victoryPileSize); AddSupplyPile(state, DuchyDefinitionId, victoryPileSize);
        AddSupplyPile(state, ProvinceDefinitionId, victoryPileSize); AddSupplyPile(state, CurseDefinitionId, Math.Max(0, 10 * (players - 1)));
        GameSetupConfig setup = RoomGameSetup.ReadCurrent();
        if (setup != null && setup.kingdomCardIds != null)
            foreach (string definitionId in setup.kingdomCardIds)
                if (!string.IsNullOrEmpty(definitionId))
                {
                    ExtensionCardData definition = ResolveCardDefinition(definitionId);
                    int count = definition != null && definition.pileSize > 0 ? definition.pileSize : KingdomPileCount;
                    AddSupplyPile(state, definitionId, count, true);
                }
    }

    private static void CreateExtensionComponents(GameStateSnapshot state, GameSetupConfig setup, int playerCount, System.Random random)
    {
        if (state == null || setup == null || setup.kingdomCardIds == null) return;
        ExtensionComponentUsage usage = ExtensionComponentUsageResolver.Resolve(setup.kingdomCardIds);

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null) continue;

            if (extension.specialPiles != null)
                foreach (ExtensionSpecialPileData definition in extension.specialPiles)
                {
                    if (definition == null || definition.cardIds == null || definition.cardIds.Count == 0) continue;
                    string pileId = extension.id + ":" + definition.id;
                    if (!usage.UsesSpecialPile(pileId)) continue;
                    SpecialPileSnapshot pile = new SpecialPileSnapshot(pileId,
                        string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name);
                    int count = definition.fixedCount > 0
                        ? definition.fixedCount
                        : Math.Max(0, definition.cardsPerPlayer * Math.Max(1, playerCount));
                    for (int index = 0; index < count; index++)
                    {
                        string cardId = definition.cardIds[index % definition.cardIds.Count];
                        CardInstance instance = new CardInstance(state.NextCardInstanceId++, extension.id + ":" + cardId, string.Empty);
                        state.CardInstances.Add(instance);
                        pile.CardInstanceIds.Add(instance.InstanceId);
                    }
                    if (definition.shuffle && random != null)
                        CardZoneRules.Shuffle(pile.CardInstanceIds, random);
                    state.SpecialPiles.Add(pile);
                }

            if (extension.artifacts != null)
                foreach (ExtensionCardData artifact in extension.artifacts)
                {
                    if (artifact == null || string.IsNullOrWhiteSpace(artifact.id)) continue;
                    string artifactId = extension.id + ":" + artifact.id;
                    if (!usage.UsesArtifact(artifactId)) continue;
                    CardInstance instance = new CardInstance(state.NextCardInstanceId++, artifactId, string.Empty);
                    state.CardInstances.Add(instance);
                    state.UnownedArtifacts.Add(instance.InstanceId);
                }
        }
    }

    private static int GetVictoryPileSize(int playerCount)
    {
        if (playerCount <= 2) return 8;
        if (playerCount <= 4) return 12;
        return playerCount * 3;
    }

    private static void AddSupplyPile(GameStateSnapshot state, string definitionId, int remainingCount, bool isKingdom = false)
    {
        if (state.SupplyPiles.Any(pile => pile != null && string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))) return;
        state.SupplyPiles.Add(new SupplyPileSnapshot(definitionId, Math.Max(0, remainingCount), isKingdom));
    }

    private static bool CreateStarterDecksAndOpeningHands(GameStateSnapshot state)
    {
        if (state == null || state.Players == null) return false;
        System.Random random = NewRandom();
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null) continue;
            player.Deck.Clear(); player.Hand.Clear(); player.Discard.Clear(); player.InPlay.Clear(); player.Inspected.Clear();
            for (int i = 0; i < StartingCopperCount; i++)
                if (!CardInstanceRules.TryCreateOwnedCard(state, player, CopperDefinitionId, CardZone.Deck, out _, out string error))
                { Debug.LogError("Could not create starter Copper: " + error); return false; }
            for (int i = 0; i < StartingEstateCount; i++)
                if (!CardInstanceRules.TryCreateOwnedCard(state, player, EstateDefinitionId, CardZone.Deck, out _, out string error))
                { Debug.LogError("Could not create starter Estate: " + error); return false; }
            if (!CardZoneRules.Shuffle(player.Deck, random)) return false;
            if (!CardZoneRules.DrawCards(player, StartingHandSize, random, out string drawError))
            { Debug.LogError("Could not draw opening hand: " + drawError); return false; }
        }
        return true;
    }

    private static bool TryApplyRequestedHandOrder(GameStateSnapshot state, string playerId, int[] requestedOrder)
    {
        if (requestedOrder == null) return true;
        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (player == null || player.Hand == null || requestedOrder.Length != player.Hand.Count) return false;
        HashSet<int> expected = new HashSet<int>(player.Hand);
        if (expected.Count != player.Hand.Count) return false;
        HashSet<int> received = new HashSet<int>();
        foreach (int instanceId in requestedOrder) if (!expected.Contains(instanceId) || !received.Add(instanceId)) return false;
        player.Hand.Clear(); player.Hand.AddRange(requestedOrder); return true;
    }

    private static bool PerformCleanupAndAdvance(GameStateSnapshot state)
    {
        if (state == null || state.Players == null || state.Players.Count == 0) return false;
        PlayerStateSnapshot current = FindPlayer(state, state.ActivePlayerId);
        if (current == null) return false;
        state.Phase = CleanupPhase;
        GameRuleResult ended = TurnLifecycleRules.TryResolveTurnEnded(state, current, ResolveCardDefinition, NewRandom());
        if (ended.Status == GameRuleStatus.Rejected)
        { Debug.LogError("Could not resolve end-of-turn effects: " + ended.Error); return false; }
        if (ended.Status == GameRuleStatus.WaitingForChoice) return true;
        if (!SetAsideRules.TryResolveTurnEnd(state, current, out string setAsideError))
        { Debug.LogError("Could not resolve end-of-turn set-aside cards: " + setAsideError); return false; }
        DurationRules.MoveCleanupInPlayCards(state, current, ResolveCardDefinition);
        CardZoneRules.MoveAll(current, CardZone.Hand, CardZone.Discard, true);
        current.Actions = 1; current.Buys = 1; current.Coins = 0; current.ActionsPlayedThisTurn = 0; CostRules.ResetForTurn(current);
        int cleanupDraw = StartingHandSize + current.NextCleanupDrawModifier;
        current.NextCleanupDrawModifier = 0;
        CardZoneRules.DrawCards(current, Math.Max(0, cleanupDraw), NewRandom(), out _);
        current.CardsDiscardedThisTurn = 0;
        current.CardsTrashedThisTurn = 0;
        current.CardsGainedThisTurn = 0;

        // Dominion end conditions never interrupt the current turn. Finalise only
        // after cleanup, before rotating the active player or incrementing TurnNumber.
        if (GameEndRules.TryFinaliseAtTurnBoundary(state)) return true;

        int currentIndex = state.Players.FindIndex(player => player != null && player.PlayerId == state.ActivePlayerId);
        if (currentIndex < 0) currentIndex = 0;
        int nextIndex = (currentIndex + 1) % state.Players.Count;
        PlayerStateSnapshot nextPlayer = state.Players[nextIndex];
        state.ActivePlayerId = nextPlayer.PlayerId; state.TurnNumber++; state.Phase = ActionPhase;
        nextPlayer.Actions = 1; nextPlayer.Buys = 1; nextPlayer.Coins = 0; nextPlayer.ActionsPlayedThisTurn = 0; CostRules.ResetForTurn(nextPlayer);
        GameRuleResult start = TurnLifecycleRules.TryResolveTurnStarted(state, nextPlayer, ResolveCardDefinition, NewRandom());
        if (start.Status == GameRuleStatus.Rejected)
        {
            Debug.LogError("Could not resolve start-of-turn effects: " + start.Error);
            return false;
        }
        return true;
    }

    private static bool CompleteCleanupAfterDecision(GameStateSnapshot state, GameRuleResult result)
    {
        if (state == null || result == null) return false;
        if (result.Status != GameRuleStatus.Applied || !string.Equals(state.Phase, CleanupPhase, StringComparison.Ordinal) ||
            (state.Resolution != null && state.Resolution.IsActive)) return true;
        return PerformCleanupAndAdvance(state);
    }

    private static System.Random NewRandom() => new System.Random(Guid.NewGuid().GetHashCode());

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId)
    {
        if (state == null || state.Players == null) return null;
        return state.Players.Find(player => player != null && player.PlayerId == playerId);
    }

    private static ExtensionCardData ResolveCardDefinition(string definitionId)
    {
        ExtensionPackageData extension; ExtensionCardData definition;
        return RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) ? definition : null;
    }

    private static bool ValidateActivePlayerCommand(string requesterPlayerId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!CanWrite() || _state == null || !_state.IsStarted || _state.IsPaused) return false;
        if (_state.Version != expectedVersion || _state.AuthorityEpoch != expectedAuthorityEpoch) return false;
        if (_state.Resolution != null && _state.Resolution.IsActive) return false;
        return _state.ActivePlayerId == requesterPlayerId && _state.Players.Count > 0;
    }

    private static bool ValidateDecisionCommand(string requesterPlayerId, string decisionId, int expectedVersion, int expectedAuthorityEpoch)
    {
        if (!CanWrite() || _state == null || !_state.IsStarted || _state.IsPaused) return false;
        if (_state.Version != expectedVersion || _state.AuthorityEpoch != expectedAuthorityEpoch) return false;
        ResolutionQueue.EnsureSnapshot(_state);
        PendingDecisionSnapshot decision = _state.Resolution.PendingDecision;
        return _state.Resolution.IsActive && decision != null && decision.IsPending &&
               string.Equals(decision.PlayerId, requesterPlayerId, StringComparison.Ordinal) &&
               string.Equals(decision.DecisionId, decisionId, StringComparison.Ordinal);
    }

    private static void UpdatePauseState(GameStateSnapshot state)
    {
        if (state == null) return;
        List<string> missingPlayers = state.Players.Where(player => !player.IsConnected)
            .Select(player => string.IsNullOrEmpty(player.NickName) ? "Joueur" : player.NickName).ToList();
        if (missingPlayers.Count > 0)
        {
            state.IsPaused = true; state.PauseReason = "En attente de reconnexion : " + string.Join(", ", missingPlayers); return;
        }
        state.IsPaused = state.ManualPauseRequested;
        state.PauseReason = state.ManualPauseRequested ? "Partie mise en pause par l’hôte." : string.Empty;
    }

    private static bool CanWrite() => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.IsMasterClient;

    private static bool CommitState(GameStateSnapshot state)
    {
        if (!CanWrite() || state == null) return false;
        NormaliseCollections(state);
        if (!GameStateSnapshotMigration.TryUpgradeToCurrent(state, out string migrationError))
        {
            Debug.LogError("Cannot commit Dominion game state: " + migrationError);
            return false;
        }

        GameStateSnapshot committed = Clone(state);
        int previousVersion = _state != null ? _state.Version : 0;
        committed.Version = Math.Max(previousVersion, committed.Version) + 1;
        if (!GameStateValidator.TryValidate(committed, out string validationError))
        {
            Debug.LogError("Refusing to commit invalid Dominion game state:\n" + validationError);
            return false;
        }

        string json = JsonUtility.ToJson(committed);
        Hashtable properties = new Hashtable { { StatePropertyKey, json } };
        bool queued = PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        if (!queued) return false;
        SetLocalState(committed); return true;
    }

    private static bool ApplyJson(string json, bool force)
    {
        if (string.IsNullOrEmpty(json)) return false;
        GameStateSnapshot incoming = JsonUtility.FromJson<GameStateSnapshot>(json);
        if (incoming == null) return false;
        if (!GameStateSnapshotMigration.TryUpgradeToCurrent(incoming, out string migrationError))
        {
            Debug.LogError("Cannot hydrate Dominion game state: " + migrationError);
            return false;
        }

        NormaliseCollections(incoming);
        if (!GameStateValidator.TryValidate(incoming, out string validationError))
        {
            Debug.LogError("Ignoring invalid replicated Dominion game state:\n" + validationError);
            return false;
        }

        if (!force && _state != null && incoming.Version <= _state.Version) return false;
        SetLocalState(incoming); return true;
    }

    private static void SetLocalState(GameStateSnapshot state)
    {
        _state = Clone(state); StateChanged?.Invoke(_state);
    }

    private static GameStateSnapshot Clone(GameStateSnapshot state)
    {
        if (state == null) return null;
        GameStateSnapshot clone = JsonUtility.FromJson<GameStateSnapshot>(JsonUtility.ToJson(state));
        NormaliseCollections(clone); return clone;
    }

    private static void NormaliseCollections(GameStateSnapshot state)
    {
        if (state == null) return;
        if (state.CardInstances == null) state.CardInstances = new List<CardInstance>();
        if (state.SupplyPiles == null) state.SupplyPiles = new List<SupplyPileSnapshot>();
        if (state.SpecialPiles == null) state.SpecialPiles = new List<SpecialPileSnapshot>();
        foreach (SpecialPileSnapshot pile in state.SpecialPiles)
            if (pile != null && pile.CardInstanceIds == null) pile.CardInstanceIds = new List<int>();
        if (state.UnownedArtifacts == null) state.UnownedArtifacts = new List<int>();
        if (state.SetAsideCards == null) state.SetAsideCards = new List<SetAsideCardSnapshot>();
        if (state.Journal == null) state.Journal = new List<GameJournalEntrySnapshot>();
        if (state.NextJournalSequence < 1)
            state.NextJournalSequence = state.Journal.Count > 0 ? state.Journal.Max(entry => entry != null ? entry.Sequence : 0) + 1 : 1;
        if (state.NextCardInstanceId < 1)
            state.NextCardInstanceId = state.CardInstances.Count > 0 ? state.CardInstances.Max(card => card != null ? card.InstanceId : 0) + 1 : 1;
        if (state.TrashedCards == null) state.TrashedCards = new List<int>();
        if (state.AbilityUsages == null) state.AbilityUsages = new List<AbilityUsageSnapshot>();
        if (state.Players == null) state.Players = new List<PlayerStateSnapshot>();
        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null) continue;
            if (player.Deck == null) player.Deck = new List<int>();
            if (player.Hand == null) player.Hand = new List<int>();
            if (player.Discard == null) player.Discard = new List<int>();
            if (player.InPlay == null) player.InPlay = new List<int>();
            if (player.Inspected == null) player.Inspected = new List<int>();
            if (player.Artifacts == null) player.Artifacts = new List<int>();
            if (player.ResolvedDurationCards == null) player.ResolvedDurationCards = new List<int>();
        }
        ResolutionQueue.EnsureSnapshot(state);
    }
}
