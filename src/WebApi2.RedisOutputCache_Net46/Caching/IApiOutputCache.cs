using System;
using System.Threading.Tasks;

namespace WebApi2.RedisOutputCache.Caching
{
    /// <summary>
    /// Public interface for output caching.
    /// </summary>
    public interface IApiOutputCache
    {
        /// <summary>
        /// Get the specified item from the cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<T> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// If the counter exists, get its current value. Otherwise, initialize to 1 and return it.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="localCacheEnabled"></param>
        /// <returns></returns>
        Task<long> GetOrIncrAsync(string key, bool localCacheEnabled);

        /// <summary>
        /// Increment by 1 the value associated with the specified key. If localCacheNotificationChannel is not null or whitespace,
        /// publish the key to that channel to notify any subscribers to evict that key from their local caches.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="localCacheNotificationChannel">If not null or whitespace, will publish the key to this channel to notify
        /// remote subscribes to remove key from their local caches.</param>
        /// <returns></returns>
        Task<long> IncrAsync(string key, string localCacheNotificationChannel = null);

        /// <summary>
        /// Check to see whether the specified key exists in the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<bool> ContainsAsync(string key);

        /// <summary>
        /// Add the object to the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="o"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        Task<bool> AddAsync(string key, object o, DateTimeOffset expiration);
    }
}