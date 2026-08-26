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
    public List<string> SelectedDefinitionIds = new List<string>();
    public List<string> SelectedOptionIds = new List<string>();
    public List<string> StagedSelectionPlayerIds = new List<string>();
    public List<int> StagedSelectedInstanceIds = new List<int>();
    public List<string> AttackProtectedPlayerIds = new List<string>();
    public int LastSelectionCount;
    public int LastSelectedCardCost = -1;
    public int LastMovedCardInstanceId;
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
    public List<string> CandidateDefinitionIds = new List<string>();
    public List<string> CandidateOptionLabels = new List<string>();
    public bool AllowPass;
    public List<string> RemainingPlayerIds = new List<string>();
    public int TargetHandSize;
    public GameEventSnapshot TriggerEvent;
    public string Timing;
    public int ListenerCardInstanceId;
    public int AbilityIndex = -1;
    public int EffectIndex = -1;

    public void Clear()
    {
        IsPending = false; DecisionId = string.Empty; PlayerId = string.Empty; Operation = string.Empty;
        Zone = string.Empty; Prompt = string.Empty; SourceCardInstanceId = 0; MinSelections = 0; MaxSelections = 0;
        CandidateInstanceIds.Clear(); CandidateDefinitionIds.Clear(); CandidateOptionLabels.Clear(); RemainingPlayerIds.Clear(); TargetHandSize = 0; AllowPass = false;
        TriggerEvent = null; Timing = string.Empty; ListenerCardInstanceId = 0; AbilityIndex = -1; EffectIndex = -1;
    }
}

public sealed class ResolutionQueue
{
    private readonly ResolutionQueueSnapshot _snapshot;
    public GameEventBus Events { get; }
    public PendingDecisionSnapshot PendingDecision => _snapshot.PendingDecision;
    public IReadOnlyList<int> SelectedInstanceIds => _snapshot.SelectedInstanceIds;
    public IReadOnlyList<string> SelectedDefinitionIds => _snapshot.SelectedDefinitionIds;
    public IReadOnlyList<string> SelectedOptionIds => _snapshot.SelectedOptionIds;
    public IReadOnlyList<string> StagedSelectionPlayerIds => _snapshot.StagedSelectionPlayerIds;
    public IReadOnlyList<int> StagedSelectedInstanceIds => _snapshot.StagedSelectedInstanceIds;
    public int LastSelectionCount => _snapshot.LastSelectionCount;
    public int LastSelectedCardCost => _snapshot.LastSelectedCardCost;
    public int LastMovedCardInstanceId => _snapshot.LastMovedCardInstanceId;
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
        state.Resolution.SelectedInstanceIds.Clear(); state.Resolution.SelectedDefinitionIds.Clear(); state.Resolution.SelectedOptionIds.Clear();
        state.Resolution.StagedSelectionPlayerIds.Clear(); state.Resolution.StagedSelectedInstanceIds.Clear(); state.Resolution.AttackProtectedPlayerIds.Clear();
        state.Resolution.LastSelectionCount = 0; state.Resolution.LastSelectedCardCost = -1; state.Resolution.LastMovedCardInstanceId = 0;
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
        return TrySuspendForDecision(playerId, operation, zone, prompt, sourceCardInstanceId, minSelections, maxSelections,
            candidateInstanceIds, triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex, false, out error);
    }

    public bool TrySuspendForDecision(string playerId, string operation, string zone, string prompt, int sourceCardInstanceId,
        int minSelections, int maxSelections, IEnumerable<int> candidateInstanceIds, GameEvent gameEvent, string timing,
        int listenerCardInstanceId, int abilityIndex, int effectIndex, bool allowPass, out string error)
    {
        if (!PrepareDecision(playerId, operation, zone, prompt, sourceCardInstanceId, minSelections, maxSelections, gameEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out PendingDecisionSnapshot decision, out error)) return false;
        decision.AllowPass = allowPass;
        if (candidateInstanceIds != null) decision.CandidateInstanceIds.AddRange(candidateInstanceIds); return true;
    }

    public bool TrySuspendForSupplyDecision(string playerId, string operation, string prompt, int sourceCardInstanceId,
        int minSelections, int maxSelections, IEnumerable<string> candidateDefinitionIds, GameEvent triggerEvent, string timing,
        int listenerCardInstanceId, int abilityIndex, int effectIndex, out string error)
    {
        if (!PrepareDecision(playerId, operation, "supply", prompt, sourceCardInstanceId, minSelections, maxSelections, triggerEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out PendingDecisionSnapshot decision, out error)) return false;
        if (candidateDefinitionIds != null) decision.CandidateDefinitionIds.AddRange(candidateDefinitionIds); return true;
    }

    public bool TrySuspendForOptionDecision(string playerId, string operation, string prompt, int sourceCardInstanceId,
        int minSelections, int maxSelections, IEnumerable<string> candidateOptionIds, IEnumerable<string> candidateOptionLabels,
        GameEvent triggerEvent, string timing, int listenerCardInstanceId, int abilityIndex, int effectIndex, out string error)
    {
        List<string> ids = candidateOptionIds != null ? new List<string>(candidateOptionIds) : new List<string>();
        List<string> labels = candidateOptionLabels != null ? new List<string>(candidateOptionLabels) : new List<string>();
        if (ids.Count == 0 || ids.Count != labels.Count)
        {
            error = "Option decision requires matching option ids and labels.";
            return false;
        }
        if (!PrepareDecision(playerId, operation, "options", prompt, sourceCardInstanceId, minSelections, maxSelections, triggerEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out PendingDecisionSnapshot decision, out error)) return false;
        _snapshot.SelectedOptionIds.Clear();
        decision.CandidateDefinitionIds.AddRange(ids);
        decision.CandidateOptionLabels.AddRange(labels);
        return true;
    }

    public bool TrySuspendForDiscardDownDecision(string playerId, string prompt, int sourceCardInstanceId, int targetHandSize,
        IEnumerable<int> candidateInstanceIds, IEnumerable<string> remainingPlayerIds, GameEvent triggerEvent, string timing,
        int listenerCardInstanceId, int abilityIndex, int effectIndex, out string error)
    {
        List<int> candidates = candidateInstanceIds != null ? new List<int>(candidateInstanceIds) : new List<int>();
        int required = Math.Max(0, candidates.Count - Math.Max(0, targetHandSize));
        if (!PrepareDecision(playerId, "discard_down_to", "hand", prompt, sourceCardInstanceId, required, required, triggerEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out PendingDecisionSnapshot decision, out error)) return false;
        decision.TargetHandSize = Math.Max(0, targetHandSize); decision.CandidateInstanceIds.AddRange(candidates);
        if (remainingPlayerIds != null) decision.RemainingPlayerIds.AddRange(remainingPlayerIds); return true;
    }

    public bool TrySuspendForAttackReaction(string playerId, string prompt, int attackCardInstanceId,
        IEnumerable<int> candidateInstanceIds, IEnumerable<string> remainingPlayerIds, out string error) =>
        TrySuspendForAttackReaction(playerId, prompt, attackCardInstanceId, candidateInstanceIds, remainingPlayerIds,
            null, string.Empty, attackCardInstanceId, -1, -1, out error);

    public bool TrySuspendForAttackReaction(string playerId, string prompt, int attackCardInstanceId,
        IEnumerable<int> candidateInstanceIds, IEnumerable<string> remainingPlayerIds, GameEvent triggerEvent, string timing,
        int listenerCardInstanceId, int abilityIndex, int effectIndex, out string error)
    {
        List<int> candidates = candidateInstanceIds != null ? new List<int>(candidateInstanceIds) : new List<int>();
        if (candidates.Count == 0) { error = "Attack reaction decision has no candidates."; return false; }
        if (!PrepareDecision(playerId, "block_attack_reaction", "hand", prompt, attackCardInstanceId, 0, 1, triggerEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out PendingDecisionSnapshot decision, out error)) return false;
        decision.CandidateInstanceIds.AddRange(candidates); if (remainingPlayerIds != null) decision.RemainingPlayerIds.AddRange(remainingPlayerIds); return true;
    }

    private bool PrepareDecision(string playerId, string operation, string zone, string prompt, int sourceCardInstanceId,
        int minSelections, int maxSelections, GameEvent triggerEvent, string timing, int listenerCardInstanceId,
        int abilityIndex, int effectIndex, out PendingDecisionSnapshot decision, out string error)
    {
        decision = null; error = string.Empty;
        if (IsWaitingForDecision) { error = "A decision is already pending for this resolution."; return false; }
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrWhiteSpace(operation) || minSelections < 0 || maxSelections < minSelections)
        { error = "Decision data is invalid."; return false; }
        decision = _snapshot.PendingDecision; decision.Clear(); decision.IsPending = true; decision.DecisionId = Guid.NewGuid().ToString("N");
        decision.PlayerId = playerId; decision.Operation = operation; decision.Zone = zone ?? string.Empty; decision.Prompt = prompt ?? string.Empty;
        decision.SourceCardInstanceId = sourceCardInstanceId; decision.MinSelections = minSelections; decision.MaxSelections = maxSelections;
        decision.TriggerEvent = GameEventSnapshot.FromRuntime(triggerEvent); decision.Timing = timing ?? string.Empty;
        decision.ListenerCardInstanceId = listenerCardInstanceId; decision.AbilityIndex = abilityIndex; decision.EffectIndex = effectIndex;
        _snapshot.SelectedInstanceIds.Clear(); _snapshot.SelectedDefinitionIds.Clear(); _snapshot.LastSelectionCount = 0; return true;
    }

    public bool TrySubmitDecision(string playerId, string decisionId, IEnumerable<int> selectedIds, out PendingDecisionSnapshot continuation, out string error)
    {
        continuation = null; if (!ValidateDecisionIdentity(playerId, decisionId, out PendingDecisionSnapshot decision, out error)) return false;
        List<int> selected = selectedIds != null ? new List<int>(selectedIds) : new List<int>();
        if (!ValidateSelectionCount(selected.Count, decision, out error)) return false;
        HashSet<int> candidates = new HashSet<int>(decision.CandidateInstanceIds), unique = new HashSet<int>();
        foreach (int id in selected) if (!candidates.Contains(id) || !unique.Add(id)) { error = "Selection contains an invalid or duplicate card."; return false; }
        continuation = CloneDecision(decision); _snapshot.SelectedInstanceIds.Clear(); _snapshot.SelectedInstanceIds.AddRange(selected);
        _snapshot.SelectedDefinitionIds.Clear(); _snapshot.LastSelectionCount = selected.Count; decision.Clear(); return true;
    }

    public bool TrySubmitSupplyDecision(string playerId, string decisionId, IEnumerable<string> selectedIds, out PendingDecisionSnapshot continuation, out string error)
    {
        continuation = null; if (!ValidateDecisionIdentity(playerId, decisionId, out PendingDecisionSnapshot decision, out error)) return false;
        List<string> selected = selectedIds != null ? new List<string>(selectedIds) : new List<string>();
        if (!ValidateSelectionCount(selected.Count, decision, out error)) return false;
        HashSet<string> candidates = new HashSet<string>(decision.CandidateDefinitionIds, StringComparer.OrdinalIgnoreCase), unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in selected) if (string.IsNullOrEmpty(id) || !candidates.Contains(id) || !unique.Add(id)) { error = "Selection contains an invalid or duplicate supply pile."; return false; }
        continuation = CloneDecision(decision); _snapshot.SelectedDefinitionIds.Clear(); _snapshot.SelectedDefinitionIds.AddRange(selected);
        _snapshot.SelectedInstanceIds.Clear(); _snapshot.LastSelectionCount = selected.Count; decision.Clear(); return true;
    }

    public bool TrySubmitOptionDecision(string playerId, string decisionId, IEnumerable<string> selectedIds, out PendingDecisionSnapshot continuation, out string error)
    {
        continuation = null; if (!ValidateDecisionIdentity(playerId, decisionId, out PendingDecisionSnapshot decision, out error)) return false;
        if (!string.Equals(decision.Zone, "options", StringComparison.OrdinalIgnoreCase)) { error = "Decision is not an option choice."; return false; }
        List<string> selected = selectedIds != null ? new List<string>(selectedIds) : new List<string>();
        if (!ValidateSelectionCount(selected.Count, decision, out error)) return false;
        HashSet<string> candidates = new HashSet<string>(decision.CandidateDefinitionIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in selected)
            if (string.IsNullOrEmpty(id) || !candidates.Contains(id) || !unique.Add(id)) { error = "Selection contains an invalid or duplicate option."; return false; }
        continuation = CloneDecision(decision); _snapshot.SelectedOptionIds.Clear(); _snapshot.SelectedOptionIds.AddRange(selected);
        _snapshot.SelectedDefinitionIds.Clear();
        _snapshot.SelectedInstanceIds.Clear(); _snapshot.LastSelectionCount = selected.Count; decision.Clear(); return true;
    }

    private bool ValidateDecisionIdentity(string playerId, string decisionId, out PendingDecisionSnapshot decision, out string error)
    {
        decision = _snapshot.PendingDecision; error = string.Empty;
        if (!IsWaitingForDecision) { error = "There is no pending decision."; return false; }
        if (!string.Equals(decision.PlayerId, playerId, StringComparison.Ordinal)) { error = "Decision belongs to another player."; return false; }
        if (!string.Equals(decision.DecisionId, decisionId, StringComparison.Ordinal)) { error = "Decision id is stale or invalid."; return false; }
        return true;
    }

    private static bool ValidateSelectionCount(int count, PendingDecisionSnapshot decision, out string error)
    { error = string.Empty; if (count == 0 && decision.AllowPass) return true; if (count < decision.MinSelections || count > decision.MaxSelections) { error = "Selection count is outside the allowed bounds."; return false; } return true; }

    public void SetLastSelectedCardCost(int cost) => _snapshot.LastSelectedCardCost = cost;
    public void SetLastMovedCardInstanceId(int instanceId) => _snapshot.LastMovedCardInstanceId = Math.Max(0, instanceId);
    public void ClearSelection() { _snapshot.SelectedInstanceIds.Clear(); _snapshot.SelectedDefinitionIds.Clear(); _snapshot.LastSelectionCount = 0; }
    public void ClearAttackProtection() => _snapshot.AttackProtectedPlayerIds.Clear();
    public bool TryStageCardSelection(string playerId, int instanceId, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(playerId) || instanceId <= 0) { error = "Staged card selection is invalid."; return false; }
        if (_snapshot.StagedSelectionPlayerIds.Contains(playerId)) { error = "Player already has a staged card selection."; return false; }
        if (_snapshot.StagedSelectedInstanceIds.Contains(instanceId)) { error = "Card is already staged by another player."; return false; }
        _snapshot.StagedSelectionPlayerIds.Add(playerId); _snapshot.StagedSelectedInstanceIds.Add(instanceId); return true;
    }
    public void ClearStagedCardSelections() { _snapshot.StagedSelectionPlayerIds.Clear(); _snapshot.StagedSelectedInstanceIds.Clear(); }
    public void MarkAttackProtected(string playerId) { if (!string.IsNullOrEmpty(playerId) && !_snapshot.AttackProtectedPlayerIds.Contains(playerId)) _snapshot.AttackProtectedPlayerIds.Add(playerId); }
    public bool IsAttackProtected(string playerId) => !string.IsNullOrEmpty(playerId) && _snapshot.AttackProtectedPlayerIds.Contains(playerId);
    public List<int> TakeSelectedInstanceIds() { List<int> x = new List<int>(_snapshot.SelectedInstanceIds); _snapshot.SelectedInstanceIds.Clear(); return x; }
    public List<string> TakeSelectedDefinitionIds() { List<string> x = new List<string>(_snapshot.SelectedDefinitionIds); _snapshot.SelectedDefinitionIds.Clear(); return x; }

    public void CompleteIfIdle()
    {
        if (Events.PendingCount > 0 || IsWaitingForDecision) return;
        _snapshot.IsActive = false; _snapshot.OwnerPlayerId = string.Empty; _snapshot.PendingEvents.Clear(); _snapshot.PendingDecision.Clear();
        _snapshot.SelectedInstanceIds.Clear(); _snapshot.SelectedDefinitionIds.Clear(); _snapshot.SelectedOptionIds.Clear();
        _snapshot.StagedSelectionPlayerIds.Clear(); _snapshot.StagedSelectedInstanceIds.Clear(); _snapshot.AttackProtectedPlayerIds.Clear();
        _snapshot.LastSelectionCount = 0; _snapshot.LastSelectedCardCost = -1; _snapshot.LastMovedCardInstanceId = 0;
    }

    private static PendingDecisionSnapshot CloneDecision(PendingDecisionSnapshot source)
    {
        PendingDecisionSnapshot clone = new PendingDecisionSnapshot { IsPending = source.IsPending, DecisionId = source.DecisionId, PlayerId = source.PlayerId,
            Operation = source.Operation, Zone = source.Zone, Prompt = source.Prompt, SourceCardInstanceId = source.SourceCardInstanceId,
            MinSelections = source.MinSelections, MaxSelections = source.MaxSelections, TargetHandSize = source.TargetHandSize, AllowPass = source.AllowPass,
            TriggerEvent = source.TriggerEvent, Timing = source.Timing, ListenerCardInstanceId = source.ListenerCardInstanceId,
            AbilityIndex = source.AbilityIndex, EffectIndex = source.EffectIndex };
        clone.CandidateInstanceIds.AddRange(source.CandidateInstanceIds); clone.CandidateDefinitionIds.AddRange(source.CandidateDefinitionIds);
        clone.CandidateOptionLabels.AddRange(source.CandidateOptionLabels);
        clone.RemainingPlayerIds.AddRange(source.RemainingPlayerIds); return clone;
    }

    public static void EnsureSnapshot(GameStateSnapshot state)
    {
        if (state == null) return;
        if (state.Resolution == null) state.Resolution = new ResolutionQueueSnapshot();
        if (state.Resolution.PendingEvents == null) state.Resolution.PendingEvents = new List<GameEventSnapshot>();
        if (state.Resolution.PendingDecision == null) state.Resolution.PendingDecision = new PendingDecisionSnapshot();
        if (state.Resolution.PendingDecision.CandidateInstanceIds == null) state.Resolution.PendingDecision.CandidateInstanceIds = new List<int>();
        if (state.Resolution.PendingDecision.CandidateDefinitionIds == null) state.Resolution.PendingDecision.CandidateDefinitionIds = new List<string>();
        if (state.Resolution.PendingDecision.CandidateOptionLabels == null) state.Resolution.PendingDecision.CandidateOptionLabels = new List<string>();
        if (state.Resolution.PendingDecision.RemainingPlayerIds == null) state.Resolution.PendingDecision.RemainingPlayerIds = new List<string>();
        if (state.Resolution.SelectedInstanceIds == null) state.Resolution.SelectedInstanceIds = new List<int>();
        if (state.Resolution.SelectedDefinitionIds == null) state.Resolution.SelectedDefinitionIds = new List<string>();
        if (state.Resolution.SelectedOptionIds == null) state.Resolution.SelectedOptionIds = new List<string>();
        if (state.Resolution.StagedSelectionPlayerIds == null) state.Resolution.StagedSelectionPlayerIds = new List<string>();
        if (state.Resolution.StagedSelectedInstanceIds == null) state.Resolution.StagedSelectedInstanceIds = new List<int>();
        if (state.Resolution.AttackProtectedPlayerIds == null) state.Resolution.AttackProtectedPlayerIds = new List<string>();
    }
}
