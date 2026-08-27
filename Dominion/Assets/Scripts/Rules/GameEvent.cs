using System;

/// <summary>
/// Stable vocabulary emitted by the deterministic rules layer.
/// Extensions should react to these concepts through TriggerResolver rather than
/// subscribing directly to Photon, UI controllers or card-specific scripts.
/// </summary>
public enum GameEventType
{
    CardPlayed,
    CardGained,
    CardDiscarded,
    CardTrashed,
    TurnStarted,
    TurnEnded,
    BuyStarted,
    PileEmptied,
    ArtifactGained,
    DiseaseGained
}

public sealed class GameEvent
{
    public GameEventType Type { get; }
    public string PlayerId { get; }
    public int CardInstanceId { get; }
    public string CardDefinitionId { get; }
    public int SourceCardInstanceId { get; }
    public CardZone DestinationZone { get; }

    public GameEvent(
        GameEventType type,
        string playerId,
        int cardInstanceId = 0,
        string cardDefinitionId = null,
        int sourceCardInstanceId = 0,
        CardZone destinationZone = CardZone.None)
    {
        Type = type;
        PlayerId = playerId ?? string.Empty;
        CardInstanceId = cardInstanceId;
        CardDefinitionId = cardDefinitionId ?? string.Empty;
        SourceCardInstanceId = sourceCardInstanceId;
        DestinationZone = destinationZone;
    }

    public static GameEvent CardPlayed(string playerId, int cardInstanceId, string cardDefinitionId)
    {
        return new GameEvent(
            GameEventType.CardPlayed,
            playerId,
            cardInstanceId,
            cardDefinitionId,
            cardInstanceId);
    }

    public static GameEvent CardGained(
        string playerId,
        int cardInstanceId,
        string cardDefinitionId,
        CardZone destinationZone,
        int sourceCardInstanceId = 0)
    {
        return new GameEvent(
            GameEventType.CardGained,
            playerId,
            cardInstanceId,
            cardDefinitionId,
            sourceCardInstanceId,
            destinationZone);
    }

    public static GameEvent CardDiscarded(
        string playerId,
        int cardInstanceId,
        string cardDefinitionId,
        int sourceCardInstanceId = 0)
    {
        return new GameEvent(
            GameEventType.CardDiscarded,
            playerId,
            cardInstanceId,
            cardDefinitionId,
            sourceCardInstanceId,
            CardZone.Discard);
    }

    public static GameEvent CardTrashed(
        string playerId,
        int cardInstanceId,
        string cardDefinitionId,
        int sourceCardInstanceId = 0)
    {
        return new GameEvent(
            GameEventType.CardTrashed,
            playerId,
            cardInstanceId,
            cardDefinitionId,
            sourceCardInstanceId,
            CardZone.None);
    }

    public static GameEvent PileEmptied(string cardDefinitionId, int sourceCardInstanceId = 0)
    {
        return new GameEvent(
            GameEventType.PileEmptied,
            string.Empty,
            0,
            cardDefinitionId,
            sourceCardInstanceId);
    }

    public static GameEvent ArtifactGained(string playerId, int instanceId, string definitionId, int sourceCardInstanceId = 0)
    {
        return new GameEvent(GameEventType.ArtifactGained, playerId, instanceId, definitionId,
            sourceCardInstanceId, CardZone.None);
    }

    public static GameEvent DiseaseGained(string playerId, int instanceId, string definitionId,
        CardZone destinationZone, int sourceCardInstanceId = 0)
    {
        return new GameEvent(GameEventType.DiseaseGained, playerId, instanceId, definitionId,
            sourceCardInstanceId, destinationZone);
    }

    public static GameEvent TurnStarted(string playerId)
    {
        return new GameEvent(GameEventType.TurnStarted, playerId);
    }
}
