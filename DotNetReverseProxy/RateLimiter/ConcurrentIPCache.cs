using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace DotNetReverseProxy.RateLimiter;

using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;

public class ConcurrentIPCache
{
    private readonly TimeSpan _slidingTime;
    private readonly ConcurrentDictionary<IPAddress, CacheItem> _dictionary = new();
    private readonly Timer _cleanupTimer;
    public readonly int MaxPenalty;
    private readonly BannedIPs? bannedIPs;
    private int _isCleaningRunning = 0; // Atomic flag
    private IPAddressRange allowedIPs;

    public ConcurrentIPCache(BannedIPs bannedIPs) : this(TimeSpan.FromMinutes(5))
    {
        this.bannedIPs = bannedIPs;
    }

    public ConcurrentIPCache(TimeSpan slidingTimer)
    {
        _slidingTime = slidingTimer;
        var checkInterval = (int)(_slidingTime.TotalMilliseconds / 2);
        _cleanupTimer = new Timer(OnTime, null, checkInterval, checkInterval);
        this.MaxPenalty = int.TryParse(System.Environment.GetEnvironmentVariable("FORWARD_MAX_ERROR_PENALTY") ?? "60", out var n) ? n : 60;

        var selfIPs = (System.Environment.GetEnvironmentVariable("SELF_IPs") ?? "0.0.0.0")
               .Split(",", StringSplitOptions.RemoveEmptyEntries);


        var skipIPs = (System.Environment.GetEnvironmentVariable("FORWARD_NO_RATE_LIMIT_IP_ADDRESSES") ?? "")
            .Split(",", StringSplitOptions.RemoveEmptyEntries);

        this.allowedIPs = new IPAddressRange(skipIPs.Concat(selfIPs));
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

    public bool IsSafe(IPAddress? key)
    {
        if (key == null
            || key.IsLocalOrDocker()
            || this.allowedIPs.Contains(key))
        {
            return true;
        }
        return false;
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

    public int GetOrUpdate(IPAddress? key, int add, int update)
    {
        if (key == null
            || key.IsLocalOrDocker()
            || this.allowedIPs.Contains(key))
        {
            return 0;
        }

        if(add == 0)
        {
            if(!_dictionary.ContainsKey(key))
            {
                return 0;
            }
        }

        long expiry = DateTime.UtcNow.Ticks + _slidingTime.Ticks;

        var finalItem = _dictionary.AddOrUpdate(
            key,
            // Insert factory if it doesn't exist
            (key) => new CacheItem(add, expiry),
            // Update factory if it already exists
            (key, existingItem) =>
            {
                // Safely modify the properties inside the atomic delegate
                Interlocked.Add(ref existingItem.Value, update);
                Volatile.Write(ref existingItem.AbsoluteExpiry, expiry);
                return existingItem;
            }
        );

        if(finalItem.Value <= 0)
        {
            _dictionary.Remove(key, out var _);
            return 0;
        }

        return finalItem.Value;


        //while (true)
        //{
        //    if (_dictionary.TryGetValue(key, out var existingItem))
        //    {
        //        // Thread-safe update inside a lock context bound strictly to the target item bucket
        //        lock (existingItem)
        //        {
        //            // Ensure it wasn't removed right before we locked it
        //            if (!_dictionary.ContainsKey(key)) continue;

        //            int newValue = updateFactory(key, existingItem.Value);
        //            if (newValue <= 0)
        //            {
        //                _dictionary.TryRemove(key, out _);
        //                return 0;
        //            }

        //            existingItem.Value = newValue;
        //            Volatile.Write(ref existingItem.AbsoluteExpiry, expiry);
        //            return newValue;
        //        }
        //    }

        //    // Item does not exist path
        //    int insertedValue = insertFactory(key);
        //    if (insertedValue <= 0) return 0;

        //    var newItem = new CacheItem(insertedValue, expiry);
        //    if (_dictionary.TryAdd(key, newItem))
        //    {
        //        return insertedValue;
        //    }
        //    // If TryAdd fails, another thread inserted it first. Loop back to update it.
        //}
    }

    public void RegisterSuccess(IPAddress? cacheKey)
    {
        GetOrUpdate(cacheKey, 0, - 1);
    }

    public bool ContainsKey(IPAddress a) => _dictionary.ContainsKey(a);

    internal bool RegisterPenalty(IPAddress cacheKey, int penalty)
    {
        var n = GetOrUpdate(cacheKey, penalty, penalty);
        if(n > MaxPenalty)
        {
            bannedIPs?.Add(cacheKey);
            return true;
        }
        return false;
    }
}

