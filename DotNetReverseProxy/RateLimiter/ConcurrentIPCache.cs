using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace DotNetReverseProxy.RateLimiter;

public class ConcurrentIPCache
{

    private readonly TimeSpan _slidingTime;
    private readonly ReaderWriterLockSlim _lockSlim = new();
    private readonly Timer _cleanupTimer;

    // Use standard dictionary but handle locks precisely
    private Dictionary<IPAddress, CacheItem> _dictionary = new();

    private bool isCleaningRunning = false;

    public ConcurrentIPCache(): this(TimeSpan.FromMinutes(5))
    {
    }

    public ConcurrentIPCache(TimeSpan slidingTimer)
    {
        _slidingTime = slidingTimer;

        // Fire the first check after the interval
        var checkInterval = (int)(_slidingTime.TotalMilliseconds / 2);

        _cleanupTimer = new Timer(OnTime, null, checkInterval, checkInterval);
    }

    // High-performance background cleanup without allocating a whole new dictionary
    private void OnTime(object? state)
    {
        try
        {

            var now = DateTime.UtcNow.Ticks;

            _lockSlim.EnterWriteLock();
            if(isCleaningRunning)
            {
                return;
            }
            isCleaningRunning = true;
            try
            {

                if (_dictionary.Count == 0)
                {
                    return;
                }

                var keep = new Dictionary<IPAddress, CacheItem>();

                foreach (var kv in _dictionary)
                {
                    lock (kv.Value)
                    {
                        if (now >= kv.Value.AbsoluteExpiry)
                        {
                            continue;
                        }
                    }
                    keep[kv.Key] = kv.Value;
                }

                _dictionary = keep;

            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }

        }
        catch
        {
            // ignore exceptions
        }
        finally
        {
            isCleaningRunning = false;
        }
    }

    private class CacheItem
    {
        // Must be long to allow Interlocked/Volatile memory operations
        internal long AbsoluteExpiry;
        internal int Value;

        public CacheItem(int value, long expiry)
        {
            Value = value;
            AbsoluteExpiry = expiry;
        }
    }

    public bool TryGetValue(IPAddress key, out int value)
    {
        _lockSlim.EnterReadLock();
        try
        {
            if (_dictionary.TryGetValue(key, out var v))
            {
                value = v.Value;
                // Update expiry safely using Volatile write without nested object locking
                var newExpiry = DateTime.UtcNow.Ticks + _slidingTime.Ticks;
                lock (v)
                {
                    v.AbsoluteExpiry = newExpiry;
                }
                return true;
            }
        }
        finally
        {
            _lockSlim.ExitReadLock();
        }

        value = 0;
        return false;
    }

    public bool ContainsKey(IPAddress a)
    {
        _lockSlim.EnterReadLock();
        try
        {
            return _dictionary.ContainsKey(a);
        } finally
        {
            _lockSlim.ExitReadLock();
        }
    }

    public int GetOrUpdate(IPAddress? key,
    Func<IPAddress, int> insertFactory,
    Func<IPAddress, int, int> updateFactory)
    {
        if(key == null)
        {
            return 0;
        }

        var now = DateTime.UtcNow.Ticks;
        bool needsRemoval = false;

        _lockSlim.EnterUpgradeableReadLock();
        try
        {
            if (_dictionary.TryGetValue(key, out var v))
            {
                int nv;
                // 1. Thread-safe mutation isolation block
                lock (v)
                {
                    nv = updateFactory(key, v.Value);
                    if (nv > 0)
                    {
                        v.Value = nv;
                        v.AbsoluteExpiry = now + _slidingTime.Ticks;
                        return nv;
                    }

                    // If the count dropped to 0 or less, mark for removal
                    needsRemoval = true;
                }

                // 2. SAFE ZONE: We have completely exited lock(v).
                // We can now upgrade to WriteLock cleanly without causing a cross-lock deadlock.
                if (needsRemoval)
                {
                    _lockSlim.EnterWriteLock();
                    try
                    {
                        _dictionary.Remove(key);
                    }
                    finally
                    {
                        _lockSlim.ExitWriteLock();
                    }
                    return 0;
                }
            }

            // ITEM MISSING PATH
            var iv = insertFactory(key);
            if (iv <= 0) return 0;

            _lockSlim.EnterWriteLock();
            try
            {
                if (_dictionary.TryGetValue(key, out var existing))
                {
                    lock (existing)
                    {
                        var nv = updateFactory(key, existing.Value);
                        if (nv <= 0)
                        {
                            _dictionary.Remove(key);
                            return 0;
                        }
                        existing.Value = nv;
                        existing.AbsoluteExpiry = now + _slidingTime.Ticks;
                        return nv;
                    }
                }

                var newItem = new CacheItem(iv, now + _slidingTime.Ticks);
                _dictionary[key] = newItem;
                return iv;
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }
        finally
        {
            _lockSlim.ExitUpgradeableReadLock();
        }
    }

    internal void RegisterSuccess(IPAddress? cacheKey)
    {
        if(cacheKey == null)
        {
            return;
        }
        this.GetOrUpdate(cacheKey, (x) => 0, (x, p) => p - 1);
    }
}
