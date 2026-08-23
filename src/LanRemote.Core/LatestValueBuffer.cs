namespace LanRemote.Core;

public sealed class LatestValueBuffer<T>
    where T : class
{
    private T? _value;
    private long _droppedCount;

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public bool HasValue => Volatile.Read(ref _value) is not null;

    public void Offer(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.Exchange(ref _value, value) is not null)
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    public T? Take() => Interlocked.Exchange(ref _value, null);

    public void Clear() => Interlocked.Exchange(ref _value, null);

    public void Reset()
    {
        Clear();
        Interlocked.Exchange(ref _droppedCount, 0);
    }
}
