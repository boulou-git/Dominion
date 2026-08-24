using System.Collections.Generic;

/// <summary>
/// Per-resolution ordered event queue. This is deliberately not a global/static pub-sub bus:
/// events belong to one authoritative working-copy transaction and disappear if that
/// transaction is rejected instead of leaking side effects into the live game.
/// </summary>
public sealed class GameEventBus
{
    private readonly Queue<GameEvent> _pending = new Queue<GameEvent>();
    private readonly List<GameEvent> _history = new List<GameEvent>();

    public int PendingCount => _pending.Count;
    public int PublishedCount => _history.Count;

    public void Publish(GameEvent gameEvent)
    {
        if (gameEvent == null)
            return;

        _pending.Enqueue(gameEvent);
        _history.Add(gameEvent);
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
        return true;
    }

    public List<GameEvent> SnapshotHistory()
    {
        return new List<GameEvent>(_history);
    }
}
