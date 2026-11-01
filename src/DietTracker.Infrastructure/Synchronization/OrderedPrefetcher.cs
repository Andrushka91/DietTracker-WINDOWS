namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Sliding window of asynchronous work whose results are consumed IN ORDER.
/// </summary>
/// <remarks>
/// <para>
/// This is the Windows counterpart of the mobile pull pipeline. A synchronization pull is latency
/// bound: one document costs one HTTP round trip, and applying it costs a few milliseconds of SQLite.
/// Starting the next N-1 downloads while the current document is applied keeps the connection busy
/// instead of waiting, which is what the desktop sync clients (and the mobile app) do.
/// </para>
/// <para>
/// The window is bounded on purpose: an unbounded <c>Task.WhenAll</c> over a few hundred photographs
/// would open one connection per document, blow the provider's per-user quota and hold every payload
/// in memory. Order is preserved because the apply side owns the invariants - a photograph can only be
/// written after its meal slot, and the cursor may not pass a document that still has to be retried.
/// </para>
/// </remarks>
internal sealed class OrderedPrefetcher<TItem, TResult> : IAsyncDisposable
{
    private readonly IReadOnlyList<TItem> _items;
    private readonly Func<TItem, int, CancellationToken, Task<TResult>> _work;
    private readonly CancellationToken _cancellationToken;
    private readonly int _window;
    private readonly Queue<Task<TResult>> _pending = new();

    private int _started;
    private int _consumed;

    /// <param name="items">Queue, in the order the consumer has to see it.</param>
    /// <param name="window">How many items may be in flight at the same time (at least one).</param>
    /// <param name="work">The (I/O bound) work of one item.</param>
    /// <param name="cancellationToken">Cancels the work that has not been started yet.</param>
    public OrderedPrefetcher(
        IReadOnlyList<TItem> items,
        int window,
        Func<TItem, int, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        _items = items;
        _window = Math.Max(1, window);
        _work = work;
        _cancellationToken = cancellationToken;
        Fill();
    }

    /// <summary>Number of items already handed to the caller.</summary>
    public int Consumed => _consumed;

    /// <summary>Resolves the next item of the queue, waiting only for THAT item.</summary>
    public async Task<TResult> NextAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.Count == 0)
        {
            throw new InvalidOperationException("The prefetch window is exhausted.");
        }

        var task = _pending.Dequeue();
        _consumed++;
        Fill();
        Observe(task);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Keeps the window full without running ahead of the consumer.</summary>
    private void Fill()
    {
        while (!_cancellationToken.IsCancellationRequested
               && _started < _items.Count
               && _pending.Count < _window)
        {
            var index = _started++;
            Task<TResult> task;
            try
            {
                task = _work(_items[index], index, _cancellationToken);
            }
            catch (Exception ex)
            {
                task = Task.FromException<TResult>(ex);
            }

            // A prefetched item the consumer never reaches (a cancelled pass) must not surface as an
            // unobserved task exception and take the process down.
            Observe(task);
            _pending.Enqueue(task);
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        while (_pending.Count > 0)
        {
            Observe(_pending.Dequeue());
        }

        return ValueTask.CompletedTask;
    }
}


/// <summary>
/// Photograph binaries that are already travelling when the apply loop reaches them.
/// </summary>
/// <remarks>
/// The bytes of a picture are the expensive part of a photograph document, and the mobile app pulls
/// them the same way: the metadata download starts the binary download, the apply step only awaits it.
/// The cap keeps one or two pictures in memory, never the whole gallery.
/// </remarks>
internal sealed class PhotoBytePrefetcher
{
    private readonly int _limit;
    private readonly Dictionary<Guid, Task<byte[]?>> _inFlight = new();
    private readonly object _gate = new();

    public PhotoBytePrefetcher(int limit)
    {
        _limit = Math.Max(0, limit);
    }

    /// <summary>Starts the download of one photograph unless the window is full.</summary>
    public void Prefetch(Guid photoId, Func<Task<byte[]?>> download)
    {
        lock (_gate)
        {
            if (_limit == 0 || _inFlight.ContainsKey(photoId) || _inFlight.Count >= _limit)
            {
                return;
            }

            // The map owns the task from now on: whoever takes it awaits it, and the fault is
            // observed here so a failed prefetch never becomes an unobserved task exception.
            var task = download();
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _inFlight[photoId] = task;
        }
    }

    /// <summary>Hands the prefetched bytes to the apply step, or null when none were started.</summary>
    public Task<byte[]?>? Take(Guid photoId)
    {
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(photoId, out var task))
            {
                return null;
            }

            _inFlight.Remove(photoId);
            return task;
        }
    }
}
