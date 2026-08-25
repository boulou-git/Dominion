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

    public List<GameEvent> SnapshotHistory()
    {
        return new List<GameEvent>(_history);
    }
}
