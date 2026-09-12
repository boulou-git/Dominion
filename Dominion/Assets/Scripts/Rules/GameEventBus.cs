using System;
using System.Collections.Generic;

/// <summary>
/// Ordered event queue for one rules resolution. It can optionally be backed by the
/// serializable ResolutionQueueSnapshot stored in GameStateSnapshot, so unresolved events
/// survive reconnect/save/load/Master migration without becoming global/static state.
/// </summary>
public sealed class GameEventBus
{
    private readonly Queue<GameEvent> _pending = new Queue<GameEvent>();
    private readonly List<GameEvent> _history = new List<GameEvent>();
    private readonly ResolutionQueueSnapshot _backingSnapshot;

    public int PendingCount => _pending.Count;
    public int PublishedCount => _history.Count;

    public GameEventBus()
    {
    }

    public GameEventBus(ResolutionQueueSnapshot backingSnapshot)
    {
        _backingSnapshot = backingSnapshot;
        if (_backingSnapshot == null || _backingSnapshot.PendingEvents == null)
            return;

        foreach (GameEventSnapshot snapshot in _backingSnapshot.PendingEvents)
        {
            if (snapshot != null && snapshot.TryToRuntime(out GameEvent gameEvent))
                _pending.Enqueue(gameEvent);
        }
    }

    public void Publish(GameEvent gameEvent)
    {
        if (gameEvent == null)
            return;

        _pending.Enqueue(gameEvent);
        _history.Add(gameEvent);

        if (_backingSnapshot != null)
        {
            if (_backingSnapshot.PendingEvents == null)
                _backingSnapshot.PendingEvents = new List<GameEventSnapshot>();
            _backingSnapshot.PendingEvents.Add(GameEventSnapshot.FromRuntime(gameEvent));
        }
    }

    public void PublishRange(IEnumerable<GameEvent> gameEvents)
    {
        if (gameEvents == null)
            return;

        foreach (GameEvent gameEvent in gameEvents)
            Publish(gameEvent);
    }

    public bool TryTakeNext(out GameEvent gameEvent)
    {
        if (_pending.Count == 0)
        {
            gameEvent = null;
            return false;
        }

        gameEvent = _pending.Dequeue();

        if (_backingSnapshot != null &&
            _backingSnapshot.PendingEvents != null &&
            _backingSnapshot.PendingEvents.Count > 0)
        {
            _backingSnapshot.PendingEvents.RemoveAt(0);
        }

        return true;
    }

    /// <summary>
    /// Moves events published while a suspended ability was being resumed ahead of
    /// events that were already waiting. This preserves Dominion's nested resolution
    /// order: finish the card selected by the current effect before continuing an
    /// older repeated play (notably Throne Room played by Throne Room).
    /// </summary>
    public void PromoteEventsPublishedSince(int previouslyPendingCount)
    {
        int existingCount = Math.Max(0, Math.Min(previouslyPendingCount, _pending.Count));
        if (existingCount == 0 || existingCount == _pending.Count)
            return;

        List<GameEvent> ordered = new List<GameEvent>(_pending);
        _pending.Clear();
        for (int index = existingCount; index < ordered.Count; index++)
            _pending.Enqueue(ordered[index]);
        for (int index = 0; index < existingCount; index++)
            _pending.Enqueue(ordered[index]);

        if (_backingSnapshot == null)
            return;

        if (_backingSnapshot.PendingEvents == null)
            _backingSnapshot.PendingEvents = new List<GameEventSnapshot>();
        _backingSnapshot.PendingEvents.Clear();
        foreach (GameEvent pendingEvent in _pending)
            _backingSnapshot.PendingEvents.Add(GameEventSnapshot.FromRuntime(pendingEvent));
    }

    public List<GameEvent> SnapshotHistory()
    {
        return new List<GameEvent>(_history);
    }
}
