using Microsoft.Extensions.Caching.Memory;
using System;

namespace DotNetReverseProxy;

public class StripedCacheService
{
    private readonly IMemoryCache _cache;
    private readonly object[] _locks;
    private const int StripeCount = 32; // Tweak based on your expected concurrency

    public StripedCacheService(IMemoryCache cache)
    {
        _cache = cache;
        _locks = new object[StripeCount];
        
        // Initialize the lock pool
        for (int i = 0; i < StripeCount; i++)
        {
            _locks[i] = new object();
        }
    }

    private object GetLockObject(string cacheKey)
    {
        if (cacheKey == null) throw new ArgumentNullException(nameof(cacheKey));
        
        // Map the key's hash code to a specific stripe index safely
        int hash = cacheKey.GetHashCode();
        int index = Math.Abs(hash % StripeCount);
        
        return _locks[index];
    }

    public T? Get<T>(string cacheKey)
    {
        object lockObject = GetLockObject(cacheKey);

        lock (lockObject) {
            _cache.TryGetValue(cacheKey, out var value);
            return (T?)value;
        }
    }

    public void Remove(string cacheKey)
    {
        object lockObject = GetLockObject(cacheKey);

        lock (lockObject) {
            _cache.Remove(cacheKey);
        }
        
    }

    public T Update<T>(string cacheKey, Func<T, T> updateFunction, T initial, TimeSpan slidingExpiration)
    {
        // Get the dedicated lock for this specific key's stripe
        object lockObject = GetLockObject(cacheKey);

        lock (lockObject)
        {
            if (!_cache.TryGetValue(cacheKey, out T? cachedValue))
            {
                _cache.Set(cacheKey, initial, slidingExpiration);
                return initial;
            }

            T updatedValue = updateFunction(cachedValue!);
            _cache.Set(cacheKey, updatedValue, slidingExpiration);

            return updatedValue;
        }
    }
}
