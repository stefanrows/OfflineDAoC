using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// A keyed cache for slow-changing world data that brains read in parallel.
/// A build never runs under the shared map lock: one caller refreshes an
/// expired entry while every other caller keeps the previous value, and a
/// first build blocks only callers of the same key (bug 56: one stable-network
/// rebuild with database reads used to block every bot of every region).
/// </summary>
public sealed class AutonomousRefreshingCache<TKey, TValue> where TKey : notnull
{
    private sealed class Entry
    {
        public readonly object BuildGate = new();
        public TValue Value;
        public long ExpiresMilliseconds;
        public bool HasValue;
        public int Refreshing;
    }

    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly long _lifetimeMilliseconds;

    public AutonomousRefreshingCache(TimeSpan lifetime)
    {
        _lifetimeMilliseconds = Math.Max(1, (long)lifetime.TotalMilliseconds);
    }

    public TValue Get(TKey key, long nowMilliseconds, Func<TValue> build)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry))
                _entries[key] = entry = new Entry();
        }

        if (Volatile.Read(ref entry.HasValue))
        {
            if (nowMilliseconds < Interlocked.Read(ref entry.ExpiresMilliseconds) ||
                Interlocked.CompareExchange(ref entry.Refreshing, 1, 0) != 0)
                return entry.Value;
            try
            {
                TValue refreshed = build();
                entry.Value = refreshed;
                Interlocked.Exchange(ref entry.ExpiresMilliseconds, nowMilliseconds + _lifetimeMilliseconds);
                return refreshed;
            }
            catch
            {
                // Keep serving the previous value; retry after a short pause.
                Interlocked.Exchange(ref entry.ExpiresMilliseconds, nowMilliseconds + Math.Min(_lifetimeMilliseconds, 30_000));
                return entry.Value;
            }
            finally
            {
                Volatile.Write(ref entry.Refreshing, 0);
            }
        }

        lock (entry.BuildGate)
        {
            if (Volatile.Read(ref entry.HasValue))
                return entry.Value;
            TValue value = build();
            entry.Value = value;
            Interlocked.Exchange(ref entry.ExpiresMilliseconds, nowMilliseconds + _lifetimeMilliseconds);
            Volatile.Write(ref entry.HasValue, true);
            return value;
        }
    }

    public void Clear()
    {
        lock (_sync)
            _entries.Clear();
    }
}
