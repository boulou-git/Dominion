using System;
using System.Collections.Generic;

[Serializable]
public sealed class ResolutionQueueSnapshot
{
    public bool IsActive;
    public string OwnerPlayerId;
    public List<GameEventSnapshot> PendingEvents = new List<GameEventSnapshot>();
    public PendingDecisionSnapshot PendingDecision = new PendingDecisionSnapshot();
    public List<int> SelectedInstanceIds = new List<int>();
    public int LastSelectionCount;
}

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
        if (gameEvent == null) return null;
        return new GameEventSnapshot
        {
            Type = gameEvent.Type.ToString(), PlayerId = gameEvent.PlayerId,
            CardInstanceId = gameEvent.CardInstanceId, CardDefinitionId = gameEvent.CardDefinitionId,
            SourceCardInstanceId = gameEvent.SourceCardInstanceId, DestinationZone = (int)gameEvent.DestinationZone
        };
    }

    public bool TryToRuntime(out GameEvent gameEvent)
    {
        gameEvent = null;
        if (!Enum.TryParse(Type, true, out GameEventType eventType)) return false;
        gameEvent = new GameEvent(eventType, PlayerId, CardInstanceId, CardDefinitionId, SourceCardInstanceId, (CardZone)DestinationZone);
        return true;
    }
}

[Serializable]
public sealed class PendingDecisionSnapshot
{
    public bool IsPending;
    public string DecisionId;
    public string PlayerId;
    public string Operation;
    public string Zone;
    public string Prompt;
    public int SourceCardInstanceId;
    public int MinSelections;
    public int MaxSelections;
    public List<int> CandidateInstanceIds = new List<int>();
    public GameEventSnapshot TriggerEvent;
    public string Timing;
    public int ListenerCardInstanceId;
    public int AbilityIndex = -1;
    public int EffectIndex = -1;

    public void Clear()
    {
        IsPending = false; DecisionId = string.Empty; PlayerId = string.Empty; Operation = string.Empty;
        Zone = string.Empty; Prompt = string.Empty; SourceCardInstanceId = 0; MinSelections = 0; MaxSelections = 0;
        CandidateInstanceIds.Clear(); TriggerEvent = null; Timing = string.Empty; ListenerCardInstanceId = 0;
        AbilityIndex = -1; EffectIndex = -1;
    }
}

public sealed class ResolutionQueue
{
    private readonly ResolutionQueueSnapshot _snapshot;
    public GameEventBus Events { get; }
    public PendingDecisionSnapshot PendingDecision => _snapshot.PendingDecision;
    public IReadOnlyList<int> SelectedInstanceIds => _snapshot.SelectedInstanceIds;
    public int LastSelectionCount => _snapshot.LastSelectionCount;
    public bool IsWaitingForDecision => _snapshot.PendingDecision != null && _snapshot.PendingDecision.IsPending;

    private ResolutionQueue(ResolutionQueueSnapshot snapshot) { _snapshot = snapshot; Events = new GameEventBus(snapshot); }

    public static bool TryBegin(GameStateSnapshot state, string ownerPlayerId, out ResolutionQueue queue, out string error)
    {
        queue = null; error = string.Empty;
        if (state == null) { error = "Game state is null."; return false; }
        if (string.IsNullOrEmpty(ownerPlayerId)) { error = "Resolution owner is missing."; return false; }
        EnsureSnapshot(state);
        if (state.Resolution.IsActive) { error = "Another rules resolution is already active."; return false; }
        state.Resolution.IsActive = true; state.Resolution.OwnerPlayerId = ownerPlayerId;
        state.Resolution.PendingEvents.Clear(); state.Resolution.PendingDecision.Clear();
        state.Resolution.SelectedInstanceIds.Clear(); state.Resolution.LastSelectionCount = 0;
        queue = new ResolutionQueue(state.Resolution); return true;
    }

    public static bool TryResume(GameStateSnapshot state, out ResolutionQueue queue, out string error)
    {
        queue = null; error = string.Empty;
        if (state == null) { error = "Game state is null."; return false; }
        EnsureSnapshot(state);
        if (!state.Resolution.IsActive) { error = "There is no active rules resolution to resume."; return false; }
        queue = new ResolutionQueue(state.Resolution); return true;
    }

    public bool TrySuspendForDecision(string playerId, string operation, string zone, string prompt, int sourceCardInstanceId,
        int minSelections, int maxSelections, IEnumerable<int> candidateInstanceIds, GameEvent triggerEvent, string timing,
        int listenerCardInstanceId, int abilityIndex, int effectIndex, out string error)
    {
        error = string.Empty;
        if (IsWaitingForDecision) { error = "A decision is already pending for this resolution."; return false; }
        if (string.IsNullOrEmpty(playerId)) { error = "Decision player is missing."; return false; }
        if (string.IsNullOrWhiteSpace(operation)) { error = "Decision operation is missing."; return false; }
        if (minSelections < 0 || maxSelections < minSelections) { error = "Decision selection bounds are invalid."; return false; }
        PendingDecisionSnapshot decision = _snapshot.PendingDecision;
        decision.Clear(); decision.IsPending = true; decision.DecisionId = Guid.NewGuid().ToString("N");
        decision.PlayerId = playerId; decision.Operation = operation; decision.Zone = zone ?? string.Empty;
        decision.Prompt = prompt ?? string.Empty; decision.SourceCardInstanceId = sourceCardInstanceId;
        decision.MinSelections = minSelections; decision.MaxSelections = maxSelections;
        if (candidateInstanceIds != null) decision.CandidateInstanceIds.AddRange(candidateInstanceIds);
        decision.TriggerEvent = GameEventSnapshot.FromRuntime(triggerEvent); decision.Timing = timing ?? string.Empty;
        decision.ListenerCardInstanceId = listenerCardInstanceId; decision.AbilityIndex = abilityIndex; decision.EffectIndex = effectIndex;
        _snapshot.SelectedInstanceIds.Clear(); _snapshot.LastSelectionCount = 0; return true;
    }

    public bool TrySubmitDecision(string playerId, string decisionId, IEnumerable<int> selectedIds, out PendingDecisionSnapshot continuation, out string error)
    {
        continuation = null; error = string.Empty;
        if (!IsWaitingForDecision) { error = "There is no pending decision."; return false; }
        PendingDecisionSnapshot decision = _snapshot.PendingDecision;
        if (!string.Equals(decision.PlayerId, playerId, StringComparison.Ordinal)) { error = "Decision belongs to another player."; return false; }
        if (!string.Equals(decision.DecisionId, decisionId, StringComparison.Ordinal)) { error = "Decision id is stale or invalid."; return false; }
        List<int> selected = selectedIds != null ? new List<int>(selectedIds) : new List<int>();
        if (selected.Count < decision.MinSelections || selected.Count > decision.MaxSelections) { error = "Selection count is outside the allowed bounds."; return false; }
        HashSet<int> candidates = new HashSet<int>(decision.CandidateInstanceIds); HashSet<int> unique = new HashSet<int>();
        foreach (int id in selected) if (!candidates.Contains(id) || !unique.Add(id)) { error = "Selection contains an invalid or duplicate card."; return false; }
        continuation = CloneDecision(decision); _snapshot.SelectedInstanceIds.Clear(); _snapshot.SelectedInstanceIds.AddRange(selected);
        _snapshot.LastSelectionCount = selected.Count; decision.Clear(); return true;
    }

    public List<int> TakeSelectedInstanceIds()
    {
        List<int> selected = new List<int>(_snapshot.SelectedInstanceIds); _snapshot.SelectedInstanceIds.Clear(); return selected;
    }

    public void CompleteIfIdle()
    {
        if (Events.PendingCount > 0 || IsWaitingForDecision) return;
        _snapshot.IsActive = false; _snapshot.OwnerPlayerId = string.Empty; _snapshot.PendingEvents.Clear();
        _snapshot.PendingDecision.Clear(); _snapshot.SelectedInstanceIds.Clear(); _snapshot.LastSelectionCount = 0;
    }

    private static PendingDecisionSnapshot CloneDecision(PendingDecisionSnapshot source)
    {
        PendingDecisionSnapshot clone = new PendingDecisionSnapshot
        {
            IsPending = source.IsPending, DecisionId = source.DecisionId, PlayerId = source.PlayerId,
            Operation = source.Operation, Zone = source.Zone, Prompt = source.Prompt,
            SourceCardInstanceId = source.SourceCardInstanceId, MinSelections = source.MinSelections,
            MaxSelections = source.MaxSelections, TriggerEvent = source.TriggerEvent, Timing = source.Timing,
            ListenerCardInstanceId = source.ListenerCardInstanceId, AbilityIndex = source.AbilityIndex, EffectIndex = source.EffectIndex
        };
        clone.CandidateInstanceIds.AddRange(source.CandidateInstanceIds); return clone;
    }

    public static void EnsureSnapshot(GameStateSnapshot state)
    {
        if (state == null) return;
        if (state.Resolution == null) state.Resolution = new ResolutionQueueSnapshot();
        if (state.Resolution.PendingEvents == null) state.Resolution.PendingEvents = new List<GameEventSnapshot>();
        if (state.Resolution.PendingDecision == null) state.Resolution.PendingDecision = new PendingDecisionSnapshot();
        if (state.Resolution.PendingDecision.CandidateInstanceIds == null) state.Resolution.PendingDecision.CandidateInstanceIds = new List<int>();
        if (state.Resolution.SelectedInstanceIds == null) state.Resolution.SelectedInstanceIds = new List<int>();
    }
}
