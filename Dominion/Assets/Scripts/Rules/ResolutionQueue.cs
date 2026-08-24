using System;
using System.Collections.Generic;

/// <summary>
/// Serializable state for one suspended rules resolution. It belongs to GameStateSnapshot,
/// so a reconnect, save/load or Master Client migration cannot lose pending rules work.
/// </summary>
[Serializable]
public sealed class ResolutionQueueSnapshot
{
    public bool IsActive;
    public string OwnerPlayerId;
    public List<GameEventSnapshot> PendingEvents = new List<GameEventSnapshot>();
    public PendingDecisionSnapshot PendingDecision = new PendingDecisionSnapshot();
}

/// <summary>
/// Serializable representation of a GameEvent. Runtime GameEvent remains immutable while
/// the snapshot stays compatible with Unity JsonUtility.
/// </summary>
[Serializable]
public sealed class GameEventSnapshot
{
    public string Type;
    public string PlayerId;
    public int CardInstanceId;
    public string CardDefinitionId;
    public int SourceCardInstanceId;
    public int DestinationZone;

    public static GameEventSnapshot FromRuntime(GameEvent gameEvent)
    {
        if (gameEvent == null)
            return null;

        return new GameEventSnapshot
        {
            Type = gameEvent.Type.ToString(),
            PlayerId = gameEvent.PlayerId,
            CardInstanceId = gameEvent.CardInstanceId,
            CardDefinitionId = gameEvent.CardDefinitionId,
            SourceCardInstanceId = gameEvent.SourceCardInstanceId,
            DestinationZone = (int)gameEvent.DestinationZone
        };
    }

    public bool TryToRuntime(out GameEvent gameEvent)
    {
        gameEvent = null;

        if (!Enum.TryParse(Type, true, out GameEventType eventType))
            return false;

        gameEvent = new GameEvent(
            eventType,
            PlayerId,
            CardInstanceId,
            CardDefinitionId,
            SourceCardInstanceId,
            (CardZone)DestinationZone);
        return true;
    }
}

/// <summary>
/// Durable decision placeholder. A concrete choose/discard/trash operation will populate
/// these fields and its operation-specific payload before returning WaitingForChoice.
/// Keeping the generic identity/cursor data here avoids UI- or Photon-owned decision state.
/// </summary>
[Serializable]
public sealed class PendingDecisionSnapshot
{
    public bool IsPending;
    public string DecisionId;
    public string PlayerId;
    public string Operation;
    public string Prompt;
    public int SourceCardInstanceId;
    public int MinSelections;
    public int MaxSelections;
    public List<int> CandidateInstanceIds = new List<int>();

    // Continuation identity. Exact effect semantics remain owned by the operation resolver.
    public string TriggerEventType;
    public string Timing;
    public int ListenerCardInstanceId;
    public int AbilityIndex = -1;
    public int EffectIndex = -1;

    public void Clear()
    {
        IsPending = false;
        DecisionId = string.Empty;
        PlayerId = string.Empty;
        Operation = string.Empty;
        Prompt = string.Empty;
        SourceCardInstanceId = 0;
        MinSelections = 0;
        MaxSelections = 0;
        CandidateInstanceIds.Clear();
        TriggerEventType = string.Empty;
        Timing = string.Empty;
        ListenerCardInstanceId = 0;
        AbilityIndex = -1;
        EffectIndex = -1;
    }
}

/// <summary>
/// Runtime façade over the serializable resolution snapshot. There is never a global queue:
/// every command resolves against the queue stored on its authoritative working-copy state.
/// </summary>
public sealed class ResolutionQueue
{
    private readonly ResolutionQueueSnapshot _snapshot;

    public GameEventBus Events { get; }
    public bool IsWaitingForDecision =>
        _snapshot.PendingDecision != null && _snapshot.PendingDecision.IsPending;

    private ResolutionQueue(ResolutionQueueSnapshot snapshot)
    {
        _snapshot = snapshot;
        Events = new GameEventBus(snapshot);
    }

    public static bool TryBegin(
        GameStateSnapshot state,
        string ownerPlayerId,
        out ResolutionQueue queue,
        out string error)
    {
        queue = null;
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (string.IsNullOrEmpty(ownerPlayerId))
        {
            error = "Resolution owner is missing.";
            return false;
        }

        EnsureSnapshot(state);
        if (state.Resolution.IsActive)
        {
            error = "Another rules resolution is already active.";
            return false;
        }

        state.Resolution.IsActive = true;
        state.Resolution.OwnerPlayerId = ownerPlayerId;
        state.Resolution.PendingEvents.Clear();
        state.Resolution.PendingDecision.Clear();
        queue = new ResolutionQueue(state.Resolution);
        return true;
    }

    public static bool TryResume(
        GameStateSnapshot state,
        out ResolutionQueue queue,
        out string error)
    {
        queue = null;
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        EnsureSnapshot(state);
        if (!state.Resolution.IsActive)
        {
            error = "There is no active rules resolution to resume.";
            return false;
        }

        queue = new ResolutionQueue(state.Resolution);
        return true;
    }

    public void CompleteIfIdle()
    {
        if (Events.PendingCount > 0 || IsWaitingForDecision)
            return;

        _snapshot.IsActive = false;
        _snapshot.OwnerPlayerId = string.Empty;
        _snapshot.PendingEvents.Clear();
        _snapshot.PendingDecision.Clear();
    }

    public static void EnsureSnapshot(GameStateSnapshot state)
    {
        if (state == null)
            return;

        if (state.Resolution == null)
            state.Resolution = new ResolutionQueueSnapshot();
        if (state.Resolution.PendingEvents == null)
            state.Resolution.PendingEvents = new List<GameEventSnapshot>();
        if (state.Resolution.PendingDecision == null)
            state.Resolution.PendingDecision = new PendingDecisionSnapshot();
        if (state.Resolution.PendingDecision.CandidateInstanceIds == null)
            state.Resolution.PendingDecision.CandidateInstanceIds = new List<int>();
    }
}
