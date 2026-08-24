using System;

/// <summary>
/// Stable vocabulary emitted by the deterministic rules layer.
/// Extensions should react to these concepts through the future TriggerResolver rather
/// than subscribing directly to Photon, UI controllers or card-specific scripts.
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

/// <summary>
/// Immutable description of one gameplay event.
/// Keep this object free of Unity/Photon references so it can be consumed by rules,
/// tests, logs and eventually replay code.
/// </summary>
public sealed class GameEvent
{
    public GameEventType Type { get; }
    public string PlayerId { get; }
    public int CardInstanceId { get; }
    public string CardDefinitionId { get; }
    public int SourceCardInstanceId { get; }

    public GameEvent(
        GameEventType type,
        string playerId,
        int cardInstanceId = 0,
        string cardDefinitionId = null,
        int sourceCardInstanceId = 0)
    {
        Type = type;
        PlayerId = playerId ?? string.Empty;
        CardInstanceId = cardInstanceId;
        CardDefinitionId = cardDefinitionId ?? string.Empty;
        SourceCardInstanceId = sourceCardInstanceId;
    }

    public static GameEvent CardPlayed(
        string playerId,
        int cardInstanceId,
        string cardDefinitionId)
    {
        return new GameEvent(
            GameEventType.CardPlayed,
            playerId,
            cardInstanceId,
            cardDefinitionId,
            cardInstanceId);
    }
}
