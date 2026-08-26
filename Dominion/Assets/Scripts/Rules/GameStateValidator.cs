using System;
using System.Collections.Generic;

/// <summary>
/// Pure consistency checks for the authoritative snapshot.
/// Keep this validator free of Photon, Unity scene objects and extension loading so it can
/// be reused by editor tests, development builds, save/load and future replay tooling.
/// </summary>
public static class GameStateValidator
{
    public static bool TryValidate(GameStateSnapshot state, out string error)
    {
        List<string> errors = Validate(state);
        error = errors.Count == 0 ? string.Empty : string.Join("\n", errors);
        return errors.Count == 0;
    }

    public static List<string> Validate(GameStateSnapshot state)
    {
        List<string> errors = new List<string>();
        if (state == null)
        {
            errors.Add("Game state is null.");
            return errors;
        }

        ValidateHeader(state, errors);

        Dictionary<string, PlayerStateSnapshot> players = ValidatePlayers(state, errors);
        Dictionary<int, CardInstance> cards = ValidateCardRegistry(state, players, errors);
        ValidateCardLocations(state, players, cards, errors);
        ValidateSupply(state, errors);
        ValidateJournal(state, errors);
        ValidateResolution(state, players, cards, errors);
        ValidateAbilityUsages(state, cards, errors);

        return errors;
    }

    private static void ValidateHeader(GameStateSnapshot state, List<string> errors)
    {
        if (state.SchemaVersion < 1 || state.SchemaVersion > GameStateSnapshot.CurrentSchemaVersion)
            errors.Add("Unsupported game-state schema version: " + state.SchemaVersion + ".");
        if (state.Version < 0)
            errors.Add("Game-state version cannot be negative.");
        if (state.AuthorityEpoch < 0)
            errors.Add("Authority epoch cannot be negative.");
        if (state.NextCardInstanceId < 1)
            errors.Add("Next card instance id must be positive.");
        if (state.NextJournalSequence < 1)
            errors.Add("Next journal sequence must be positive.");
        if (state.IsStarted && string.IsNullOrEmpty(state.ActivePlayerId))
            errors.Add("A started match must have an active player.");
    }

    private static Dictionary<string, PlayerStateSnapshot> ValidatePlayers(GameStateSnapshot state, List<string> errors)
    {
        Dictionary<string, PlayerStateSnapshot> players = new Dictionary<string, PlayerStateSnapshot>(StringComparer.Ordinal);
        if (state.Players == null)
        {
            errors.Add("Player collection is null.");
            return players;
        }

        for (int i = 0; i < state.Players.Count; i++)
        {
            PlayerStateSnapshot player = state.Players[i];
            if (player == null)
            {
                errors.Add("Player collection contains a null entry at index " + i + ".");
                continue;
            }

            if (string.IsNullOrEmpty(player.PlayerId))
            {
                errors.Add("Player at index " + i + " has no stable id.");
                continue;
            }

            if (players.ContainsKey(player.PlayerId))
            {
                errors.Add("Duplicate player id: " + player.PlayerId + ".");
                continue;
            }

            players.Add(player.PlayerId, player);
            if (player.Deck == null || player.Hand == null || player.Discard == null || player.InPlay == null || player.Inspected == null)
                errors.Add("Player " + player.PlayerId + " has a null card zone.");
            if (player.CostReductionThisTurn < 0)
                errors.Add("Player " + player.PlayerId + " has a negative turn cost reduction.");
        }

        if (state.IsStarted && !string.IsNullOrEmpty(state.ActivePlayerId) && !players.ContainsKey(state.ActivePlayerId))
            errors.Add("Active player does not exist: " + state.ActivePlayerId + ".");

        return players;
    }

    private static Dictionary<int, CardInstance> ValidateCardRegistry(
        GameStateSnapshot state,
        Dictionary<string, PlayerStateSnapshot> players,
        List<string> errors)
    {
        Dictionary<int, CardInstance> cards = new Dictionary<int, CardInstance>();
        if (state.CardInstances == null)
        {
            errors.Add("Card instance registry is null.");
            return cards;
        }

        int maxInstanceId = 0;
        for (int i = 0; i < state.CardInstances.Count; i++)
        {
            CardInstance card = state.CardInstances[i];
            if (card == null)
            {
                errors.Add("Card instance registry contains a null entry at index " + i + ".");
                continue;
            }

            if (card.InstanceId <= 0)
            {
                errors.Add("Card instance has an invalid id: " + card.InstanceId + ".");
                continue;
            }

            if (cards.ContainsKey(card.InstanceId))
            {
                errors.Add("Duplicate card instance id: " + card.InstanceId + ".");
                continue;
            }

            cards.Add(card.InstanceId, card);
            maxInstanceId = Math.Max(maxInstanceId, card.InstanceId);

            if (string.IsNullOrEmpty(card.DefinitionId))
                errors.Add("Card instance " + card.InstanceId + " has no definition id.");
            if (string.IsNullOrEmpty(card.OwnerPlayerId) || !players.ContainsKey(card.OwnerPlayerId))
                errors.Add("Card instance " + card.InstanceId + " has an unknown owner: " + (card.OwnerPlayerId ?? "<null>") + ".");
        }

        if (state.NextCardInstanceId <= maxInstanceId)
            errors.Add("Next card instance id must be greater than every allocated instance id.");

        return cards;
    }

    private static void ValidateCardLocations(
        GameStateSnapshot state,
        Dictionary<string, PlayerStateSnapshot> players,
        Dictionary<int, CardInstance> cards,
        List<string> errors)
    {
        Dictionary<int, string> locations = new Dictionary<int, string>();

        foreach (KeyValuePair<string, PlayerStateSnapshot> pair in players)
        {
            PlayerStateSnapshot player = pair.Value;
            ValidateZone(player, player.Deck, "deck", cards, locations, errors);
            ValidateZone(player, player.Hand, "hand", cards, locations, errors);
            ValidateZone(player, player.Discard, "discard", cards, locations, errors);
            ValidateZone(player, player.InPlay, "in_play", cards, locations, errors);
            ValidateZone(player, player.Inspected, "inspected", cards, locations, errors);
        }

        if (state.TrashedCards == null)
        {
            errors.Add("Trashed-card collection is null.");
        }
        else
        {
            foreach (int instanceId in state.TrashedCards)
                RegisterLocation(instanceId, "trash", cards, locations, errors);
        }

        foreach (KeyValuePair<int, CardInstance> pair in cards)
        {
            if (!locations.ContainsKey(pair.Key))
                errors.Add("Card instance " + pair.Key + " is not present in any player zone or the trash.");
        }
    }

    private static void ValidateZone(
        PlayerStateSnapshot player,
        List<int> zone,
        string zoneName,
        Dictionary<int, CardInstance> cards,
        Dictionary<int, string> locations,
        List<string> errors)
    {
        if (player == null || zone == null) return;

        foreach (int instanceId in zone)
        {
            RegisterLocation(instanceId, player.PlayerId + "/" + zoneName, cards, locations, errors);
            if (!cards.TryGetValue(instanceId, out CardInstance card)) continue;
            if (!string.Equals(card.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal))
                errors.Add("Card instance " + instanceId + " is in player " + player.PlayerId + "'s " + zoneName +
                           " but belongs to " + card.OwnerPlayerId + ".");
        }
    }

    private static void RegisterLocation(
        int instanceId,
        string location,
        Dictionary<int, CardInstance> cards,
        Dictionary<int, string> locations,
        List<string> errors)
    {
        if (instanceId <= 0)
        {
            errors.Add("Invalid card instance id " + instanceId + " in " + location + ".");
            return;
        }

        if (!cards.ContainsKey(instanceId))
        {
            errors.Add("Unknown card instance " + instanceId + " referenced by " + location + ".");
            return;
        }

        if (locations.TryGetValue(instanceId, out string previousLocation))
        {
            errors.Add("Card instance " + instanceId + " appears in both " + previousLocation + " and " + location + ".");
            return;
        }

        locations.Add(instanceId, location);
    }

    private static void ValidateSupply(GameStateSnapshot state, List<string> errors)
    {
        if (state.SupplyPiles == null)
        {
            errors.Add("Supply collection is null.");
            return;
        }

        HashSet<string> definitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < state.SupplyPiles.Count; i++)
        {
            SupplyPileSnapshot pile = state.SupplyPiles[i];
            if (pile == null)
            {
                errors.Add("Supply contains a null pile at index " + i + ".");
                continue;
            }

            if (string.IsNullOrEmpty(pile.DefinitionId))
                errors.Add("Supply pile at index " + i + " has no definition id.");
            else if (!definitions.Add(pile.DefinitionId))
                errors.Add("Duplicate supply pile: " + pile.DefinitionId + ".");

            if (pile.RemainingCount < 0)
                errors.Add("Supply pile " + pile.DefinitionId + " has a negative count.");
        }
    }

    private static void ValidateJournal(GameStateSnapshot state, List<string> errors)
    {
        if (state.Journal == null)
        {
            errors.Add("Journal collection is null.");
            return;
        }

        HashSet<int> sequences = new HashSet<int>();
        int maxSequence = 0;
        foreach (GameJournalEntrySnapshot entry in state.Journal)
        {
            if (entry == null)
            {
                errors.Add("Journal contains a null entry.");
                continue;
            }

            if (entry.Sequence <= 0)
                errors.Add("Journal entry has an invalid sequence: " + entry.Sequence + ".");
            else if (!sequences.Add(entry.Sequence))
                errors.Add("Duplicate journal sequence: " + entry.Sequence + ".");

            maxSequence = Math.Max(maxSequence, entry.Sequence);
        }

        if (state.NextJournalSequence <= maxSequence)
            errors.Add("Next journal sequence must be greater than every existing journal sequence.");
    }

    private static void ValidateResolution(
        GameStateSnapshot state,
        Dictionary<string, PlayerStateSnapshot> players,
        Dictionary<int, CardInstance> cards,
        List<string> errors)
    {
        if (state.Resolution == null)
        {
            errors.Add("Resolution snapshot is null.");
            return;
        }

        ResolutionQueueSnapshot resolution = state.Resolution;
        PendingDecisionSnapshot decision = resolution.PendingDecision;

        if (decision == null)
        {
            errors.Add("Pending decision snapshot is null.");
            return;
        }

        if (decision.IsPending && !resolution.IsActive)
            errors.Add("A pending decision requires an active resolution.");

        if (resolution.IsActive && string.IsNullOrEmpty(resolution.OwnerPlayerId))
            errors.Add("An active resolution must have an owner player id.");
        else if (resolution.IsActive && !players.ContainsKey(resolution.OwnerPlayerId))
            errors.Add("Resolution owner does not exist: " + resolution.OwnerPlayerId + ".");

        ValidateKnownUniquePlayerIds(resolution.AttackProtectedPlayerIds, "attack-protected player", players, errors);
        ValidateKnownUniqueCardIds(resolution.SelectedInstanceIds, "selected card", cards, errors);
        ValidateUniqueDefinitionIds(resolution.SelectedDefinitionIds, "selected definition", errors);
        ValidateUniqueDefinitionIds(resolution.SelectedOptionIds, "selected option", errors);
        ValidateKnownUniquePlayerIds(resolution.StagedSelectionPlayerIds, "staged-selection player", players, errors);
        ValidateKnownUniqueCardIds(resolution.StagedSelectedInstanceIds, "staged selected card", cards, errors);
        if (resolution.StagedSelectionPlayerIds != null && resolution.StagedSelectedInstanceIds != null &&
            resolution.StagedSelectionPlayerIds.Count != resolution.StagedSelectedInstanceIds.Count)
            errors.Add("Staged card selections must have matching player and card counts.");
        else if (resolution.StagedSelectionPlayerIds != null && resolution.StagedSelectedInstanceIds != null)
        {
            for (int index = 0; index < resolution.StagedSelectionPlayerIds.Count; index++)
            {
                string playerId = resolution.StagedSelectionPlayerIds[index];
                int instanceId = resolution.StagedSelectedInstanceIds[index];
                if (!players.TryGetValue(playerId, out PlayerStateSnapshot stagedPlayer) ||
                    !cards.TryGetValue(instanceId, out CardInstance stagedCard)) continue;
                if (stagedPlayer.Hand == null || !stagedPlayer.Hand.Contains(instanceId) ||
                    !string.Equals(stagedCard.OwnerPlayerId, playerId, StringComparison.Ordinal))
                    errors.Add("Staged selected card " + instanceId + " is not owned in player " + playerId + "'s hand.");
            }
        }

        if (!decision.IsPending) return;

        if (string.IsNullOrEmpty(decision.DecisionId))
            errors.Add("Pending decision has no decision id.");
        if (string.IsNullOrEmpty(decision.PlayerId) || !players.ContainsKey(decision.PlayerId))
            errors.Add("Pending decision belongs to an unknown player: " + (decision.PlayerId ?? "<null>") + ".");
        if (string.IsNullOrWhiteSpace(decision.Operation))
            errors.Add("Pending decision has no operation.");
        if (decision.MinSelections < 0 || decision.MaxSelections < decision.MinSelections)
            errors.Add("Pending decision has invalid selection bounds.");

        ValidateKnownUniqueCardIds(decision.CandidateInstanceIds, "decision candidate card", cards, errors);
        ValidateUniqueDefinitionIds(decision.CandidateDefinitionIds, "decision candidate definition", errors);
        ValidateUniqueDefinitionIds(decision.CandidateOptionLabels, "decision candidate option label", errors);
        if (string.Equals(decision.Zone, "options", StringComparison.OrdinalIgnoreCase) &&
            decision.CandidateDefinitionIds != null && decision.CandidateOptionLabels != null &&
            decision.CandidateDefinitionIds.Count != decision.CandidateOptionLabels.Count)
            errors.Add("Option decision candidate ids and labels must have matching counts.");
        ValidateKnownUniquePlayerIds(decision.RemainingPlayerIds, "remaining decision player", players, errors);
    }

    private static void ValidateAbilityUsages(GameStateSnapshot state, Dictionary<int, CardInstance> cards, List<string> errors)
    {
        if (state.AbilityUsages == null)
        {
            errors.Add("Ability usage collection is null.");
            return;
        }

        foreach (AbilityUsageSnapshot usage in state.AbilityUsages)
        {
            if (usage == null)
            {
                errors.Add("Ability usage collection contains a null entry.");
                continue;
            }

            if (!cards.ContainsKey(usage.CardInstanceId))
                errors.Add("Ability usage references unknown card instance " + usage.CardInstanceId + ".");
            if (usage.AbilityIndex < 0)
                errors.Add("Ability usage has a negative ability index.");
            if (usage.TurnNumber < 0)
                errors.Add("Ability usage has a negative turn number.");
        }
    }

    private static void ValidateKnownUniqueCardIds(
        List<int> ids,
        string label,
        Dictionary<int, CardInstance> cards,
        List<string> errors)
    {
        if (ids == null)
        {
            errors.Add(label + " collection is null.");
            return;
        }

        HashSet<int> unique = new HashSet<int>();
        foreach (int id in ids)
        {
            if (!unique.Add(id)) errors.Add("Duplicate " + label + ": " + id + ".");
            if (!cards.ContainsKey(id)) errors.Add("Unknown " + label + ": " + id + ".");
        }
    }

    private static void ValidateUniqueDefinitionIds(List<string> ids, string label, List<string> errors)
    {
        if (ids == null)
        {
            errors.Add(label + " collection is null.");
            return;
        }

        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id)) errors.Add("Empty " + label + ".");
            else if (!unique.Add(id)) errors.Add("Duplicate " + label + ": " + id + ".");
        }
    }

    private static void ValidateKnownUniquePlayerIds(
        List<string> ids,
        string label,
        Dictionary<string, PlayerStateSnapshot> players,
        List<string> errors)
    {
        if (ids == null)
        {
            errors.Add(label + " collection is null.");
            return;
        }

        HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id))
            {
                errors.Add("Empty " + label + ".");
                continue;
            }

            if (!unique.Add(id)) errors.Add("Duplicate " + label + ": " + id + ".");
            if (!players.ContainsKey(id)) errors.Add("Unknown " + label + ": " + id + ".");
        }
    }
}
