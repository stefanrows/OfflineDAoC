using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// One long-lived background thread for cache refreshes that read the
/// database or prove navigation (bug 56, round 2). Once a first value exists,
/// a brain turn or a player request never waits for such a refresh: it keeps
/// the previous value while this worker builds the next one. One thread keeps
/// refreshes serial, runs below normal priority, and owns a single per-thread
/// navmesh query set.
/// </summary>
public static class BackgroundCacheRefresh
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly object StartGate = new();
    private static Thread _worker;

    /// <summary>Hands work to the background thread. Tests replace it with a
    /// deterministic queue and restore <see cref="DefaultExecutor"/>.</summary>
    public static Action<Action> Executor { get; set; } = DefaultExecutor;

    public static void DefaultExecutor(Action work)
    {
        EnsureStarted();
        Queue.Add(work);
    }

    public static void Run(Action work) => Executor(work);

    private static void EnsureStarted()
    {
        if (Volatile.Read(ref _worker) != null)
            return;
        lock (StartGate)
        {
            if (_worker != null)
                return;
            var thread = new Thread(Work)
            {
                IsBackground = true,
                Name = "BackgroundCacheRefresh",
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
            Volatile.Write(ref _worker, thread);
        }
    }

    private static void Work()
    {
        foreach (Action work in Queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn("BACKGROUND_CACHE_REFRESH_FAILED keeping the previous value", exception);
            }
        }
    }
}

/// <summary>
/// A slow-changing value (a merchant list from SQLite, a navigation network)
/// that is loaded on first use and afterwards refreshed on
/// <see cref="BackgroundCacheRefresh"/>. Callers keep the previous value while
/// the refresh runs, and a failed refresh keeps it too and retries later. Only
/// the very first load blocks, and only callers of this one value.
/// </summary>
public sealed class BackgroundRefreshedValue<T> where T : class
{
    private readonly Func<T> _load;
    private readonly long _lifetimeMilliseconds;
    private readonly long _retryMilliseconds;
    private readonly object _firstLoadGate = new();
    private T _value;
    private long _expiresMilliseconds;
    private int _refreshing;

    public BackgroundRefreshedValue(Func<T> load, long lifetimeMilliseconds, long retryMilliseconds)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _lifetimeMilliseconds = Math.Max(1, lifetimeMilliseconds);
        _retryMilliseconds = Math.Clamp(retryMilliseconds, 1, _lifetimeMilliseconds);
    }

    public bool HasValue => Volatile.Read(ref _value) != null;
    public bool IsRefreshing => Volatile.Read(ref _refreshing) != 0;

    /// <summary>The current value; an expired value starts one background
    /// refresh and is still returned. A first load that throws propagates to
    /// the caller and is retried on the next call, as before.</summary>
    public T Get(long nowMilliseconds)
    {
        T value = Volatile.Read(ref _value);
        if (value == null)
        {
            lock (_firstLoadGate)
            {
                value = _value;
                if (value != null)
                    return value;
                value = _load();
                if (value == null)
                    return null;
                Interlocked.Exchange(ref _expiresMilliseconds, nowMilliseconds + _lifetimeMilliseconds);
                Volatile.Write(ref _value, value);
                return value;
            }
        }

        if (nowMilliseconds >= Interlocked.Read(ref _expiresMilliseconds) &&
            Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
        {
            try
            {
                BackgroundCacheRefresh.Run(() => Refresh(nowMilliseconds));
            }
            catch
            {
                Volatile.Write(ref _refreshing, 0);
                throw;
            }
        }
        return value;
    }

    private void Refresh(long requestedMilliseconds)
    {
        try
        {
            T fresh = _load();
            if (fresh == null)
            {
                Interlocked.Exchange(ref _expiresMilliseconds, requestedMilliseconds + _retryMilliseconds);
                return;
            }
            Volatile.Write(ref _value, fresh);
            Interlocked.Exchange(ref _expiresMilliseconds, requestedMilliseconds + _lifetimeMilliseconds);
        }
        catch
        {
            Interlocked.Exchange(ref _expiresMilliseconds, requestedMilliseconds + _retryMilliseconds);
            throw;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }
}
