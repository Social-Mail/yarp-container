using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace DotNetReverseProxy.RateLimiter;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

public class ConcurrentIPCache
{
    private readonly TimeSpan _slidingTime;
    private readonly ConcurrentDictionary<IPAddress, CacheItem> _dictionary = new();
    private readonly Timer _cleanupTimer;
    private int _isCleaningRunning = 0; // Atomic flag

    public ConcurrentIPCache() : this(TimeSpan.FromMinutes(5)) { }

    public ConcurrentIPCache(TimeSpan slidingTimer)
    {
        _slidingTime = slidingTimer;
        var checkInterval = (int)(_slidingTime.TotalMilliseconds / 2);
        _cleanupTimer = new Timer(OnTime, null, checkInterval, checkInterval);
    }

    private void OnTime(object? state)
    {
        // Thread-safe flag check without ReaderWriterLock Slim
        if (Interlocked.CompareExchange(ref _isCleaningRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            long now = DateTime.UtcNow.Ticks;

            // Allocation-free removal using ConcurrentDictionary's internal thread-safe iterator
            foreach (var kv in _dictionary)
            {
                if (now >= Volatile.Read(ref kv.Value.AbsoluteExpiry))
                {
                    _dictionary.TryRemove(kv.Key, out _);
                }
            }
        }
        catch
        {
            // Log exceptions if needed
        }
        finally
        {
            Volatile.Write(ref _isCleaningRunning, 0);
        }
    }

    private class CacheItem
    {
        public long AbsoluteExpiry; // Modified atomically via Volatile/Interlocked
        public int Value;

        public CacheItem(int value, long expiry)
        {
            Value = value;
            AbsoluteExpiry = expiry;
        }
    }

    public bool TryGetValue(IPAddress key, out int value)
    {
        if (_dictionary.TryGetValue(key, out var item))
        {
            // Thread-safe read of primitive types without a lock statement
            value = Volatile.Read(ref item.Value);

            // Atomically slide the expiry window
            long newExpiry = DateTime.UtcNow.Ticks + _slidingTime.Ticks;
            Volatile.Write(ref item.AbsoluteExpiry, newExpiry);
            return true;
        }

        value = 0;
        return false;
    }

    public int GetOrUpdate(IPAddress? key, Func<IPAddress, int> insertFactory, Func<IPAddress, int, int> updateFactory)
    {
        if (key == null) return 0;

        long expiry = DateTime.UtcNow.Ticks + _slidingTime.Ticks;

        while (true)
        {
            if (_dictionary.TryGetValue(key, out var existingItem))
            {
                // Thread-safe update inside a lock context bound strictly to the target item bucket
                lock (existingItem)
                {
                    // Ensure it wasn't removed right before we locked it
                    if (!_dictionary.ContainsKey(key)) continue;

                    int newValue = updateFactory(key, existingItem.Value);
                    if (newValue <= 0)
                    {
                        _dictionary.TryRemove(key, out _);
                        return 0;
                    }

                    existingItem.Value = newValue;
                    Volatile.Write(ref existingItem.AbsoluteExpiry, expiry);
                    return newValue;
                }
            }

            // Item does not exist path
            int insertedValue = insertFactory(key);
            if (insertedValue <= 0) return 0;

            var newItem = new CacheItem(insertedValue, expiry);
            if (_dictionary.TryAdd(key, newItem))
            {
                return insertedValue;
            }
            // If TryAdd fails, another thread inserted it first. Loop back to update it.
        }
    }

    public void RegisterSuccess(IPAddress? cacheKey)
    {
        if (cacheKey == null) return;
        GetOrUpdate(cacheKey, (x) => 0, (x, currentErrors) => currentErrors - 1);
    }

    public bool ContainsKey(IPAddress a) => _dictionary.ContainsKey(a);
}

