using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class ScoringPackageData
{
    public string extensionId;
    public List<CardScoringData> cards = new List<CardScoringData>();
}

[Serializable]
public sealed class CardScoringData
{
    public string cardId;
    public int fixedPoints;

    // Generic variable scoring: each copy scores pointsPerCards for every complete
    // group of cardsPerPoint cards owned by the player. Gardens is 1 per 10 cards.
    public int pointsPerCards;
    public int cardsPerPoint;

    // Generic cross-card scoring based on copies of another owned definition.
    public string ownedCardId;
    public int pointsPerOwnedCard;

    // Global Trash/Rebut scaling. Cimetière scores from the number of physical
    // cards in the match-wide trash, independently for each owned copy.
    public int pointsPerTrashedCards;
    public int trashedCardsPerPoint;
}

public sealed class CardScoreBreakdown
{
    public string DefinitionId { get; }
    public string CardName { get; }
    public int Copies { get; }
    public int PointsPerCopy { get; }
    public int TotalPoints { get; }

    public CardScoreBreakdown(string definitionId, string cardName, int copies, int pointsPerCopy, int totalPoints)
    {
        DefinitionId = definitionId ?? string.Empty;
        CardName = cardName ?? definitionId ?? string.Empty;
        Copies = copies;
        PointsPerCopy = pointsPerCopy;
        TotalPoints = totalPoints;
    }
}

public sealed class PlayerScoreResult
{
    public string PlayerId { get; }
    public string PlayerName { get; }
    public int TotalCards { get; }
    public int VictoryPoints { get; }
    public IReadOnlyList<CardScoreBreakdown> Breakdown { get; }

    public PlayerScoreResult(string playerId, string playerName, int totalCards, int victoryPoints, List<CardScoreBreakdown> breakdown)
    {
        PlayerId = playerId ?? string.Empty;
        PlayerName = playerName ?? string.Empty;
        TotalCards = totalCards;
        VictoryPoints = victoryPoints;
        Breakdown = breakdown ?? new List<CardScoreBreakdown>();
    }
}

public static class ScoringRules
{
    private const string ScoringFileName = "scoring.json";
    private static Dictionary<string, CardScoringData> _definitions;

    public static void Reload()
    {
        _definitions = LoadDefinitions();
    }

    public static PlayerScoreResult CalculatePlayerScore(GameStateSnapshot state, PlayerStateSnapshot player)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (player == null) throw new ArgumentNullException(nameof(player));

        EnsureLoaded();

        HashSet<int> ownedInstances = CollectOwnedCardInstances(player);
        Dictionary<string, int> copiesByDefinition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (int instanceId in ownedInstances)
        {
            CardInstance instance = FindCardInstance(state, instanceId);
            if (instance == null || string.IsNullOrWhiteSpace(instance.DefinitionId))
                continue;

            if (!copiesByDefinition.ContainsKey(instance.DefinitionId))
                copiesByDefinition[instance.DefinitionId] = 0;
            copiesByDefinition[instance.DefinitionId]++;
        }

        int totalCards = ownedInstances.Count;
        int totalPoints = 0;
        List<CardScoreBreakdown> breakdown = new List<CardScoreBreakdown>();

        foreach (KeyValuePair<string, int> pair in copiesByDefinition)
        {
            if (!_definitions.TryGetValue(pair.Key, out CardScoringData scoring) || scoring == null)
                continue;

            int pointsPerCopy = scoring.fixedPoints;
            if (scoring.pointsPerCards != 0 && scoring.cardsPerPoint > 0)
                pointsPerCopy += (totalCards / scoring.cardsPerPoint) * scoring.pointsPerCards;
            if (scoring.pointsPerOwnedCard != 0 && !string.IsNullOrWhiteSpace(scoring.ownedCardId) &&
                copiesByDefinition.TryGetValue(scoring.ownedCardId, out int ownedCardCopies))
                pointsPerCopy += ownedCardCopies * scoring.pointsPerOwnedCard;
            if (scoring.pointsPerTrashedCards != 0 && scoring.trashedCardsPerPoint > 0)
                pointsPerCopy += ((state.TrashedCards != null ? state.TrashedCards.Count : 0) /
                                  scoring.trashedCardsPerPoint) * scoring.pointsPerTrashedCards;

            int subtotal = pointsPerCopy * pair.Value;
            totalPoints += subtotal;

            ExtensionCardData definition = ResolveCardDefinition(pair.Key);
            breakdown.Add(new CardScoreBreakdown(
                pair.Key,
                definition != null && !string.IsNullOrWhiteSpace(definition.name) ? definition.name : pair.Key,
                pair.Value,
                pointsPerCopy,
                subtotal));
        }

        breakdown.Sort((a, b) => string.Compare(a.CardName, b.CardName, StringComparison.CurrentCultureIgnoreCase));
        return new PlayerScoreResult(player.PlayerId, player.NickName, totalCards, totalPoints, breakdown);
    }

    public static List<PlayerScoreResult> CalculateAll(GameStateSnapshot state)
    {
        List<PlayerScoreResult> results = new List<PlayerScoreResult>();
        if (state == null || state.Players == null) return results;

        foreach (PlayerStateSnapshot player in state.Players)
            if (player != null)
                results.Add(CalculatePlayerScore(state, player));

        return results;
    }

    private static HashSet<int> CollectOwnedCardInstances(PlayerStateSnapshot player)
    {
        HashSet<int> result = new HashSet<int>();
        AddZone(result, player.Deck);
        AddZone(result, player.Hand);
        AddZone(result, player.Discard);
        AddZone(result, player.InPlay);
        AddZone(result, player.Inspected);
        return result;
    }

    private static void AddZone(HashSet<int> target, List<int> zone)
    {
        if (target == null || zone == null) return;
        foreach (int instanceId in zone)
            if (instanceId > 0)
                target.Add(instanceId);
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        return state != null && state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
    }

    private static ExtensionCardData ResolveCardDefinition(string qualifiedId)
    {
        if (string.IsNullOrWhiteSpace(qualifiedId)) return null;
        int separator = qualifiedId.IndexOf(':');
        if (separator <= 0 || separator >= qualifiedId.Length - 1) return null;
        return ExtensionCatalog.FindCard(qualifiedId.Substring(0, separator), qualifiedId.Substring(separator + 1));
    }

    private static void EnsureLoaded()
    {
        if (_definitions == null)
            _definitions = LoadDefinitions();
    }

    private static Dictionary<string, CardScoringData> LoadDefinitions()
    {
        Dictionary<string, CardScoringData> result = new Dictionary<string, CardScoringData>(StringComparer.OrdinalIgnoreCase);

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null || string.IsNullOrWhiteSpace(extension.id) || string.IsNullOrWhiteSpace(extension.packageDirectory))
                continue;

            string path = Path.Combine(extension.packageDirectory, ScoringFileName);
            if (!File.Exists(path))
                continue;

            try
            {
                ScoringPackageData package = JsonUtility.FromJson<ScoringPackageData>(File.ReadAllText(path));
                if (package == null || package.cards == null)
                    continue;

                string extensionId = string.IsNullOrWhiteSpace(package.extensionId) ? extension.id : package.extensionId;
                foreach (CardScoringData card in package.cards)
                {
                    if (card == null || string.IsNullOrWhiteSpace(card.cardId))
                        continue;
                    card.cardId = card.cardId.Trim();
                    card.ownedCardId = (card.ownedCardId ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(card.ownedCardId) && card.ownedCardId.IndexOf(':') < 0)
                        card.ownedCardId = extensionId + ":" + card.ownedCardId;
                    result[extensionId + ":" + card.cardId] = card;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not load Dominion scoring file '" + path + "': " + exception.Message);
            }
        }

        return result;
    }
}
