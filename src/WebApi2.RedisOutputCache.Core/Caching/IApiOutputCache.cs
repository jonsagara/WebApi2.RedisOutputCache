﻿using System;
using System.Threading.Tasks;

namespace WebApi2.RedisOutputCache.Core.Caching
{
    public interface IApiOutputCache
    {
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

        Task<bool> ContainsAsync(string key);

        Task<bool> AddAsync(string key, object o, DateTimeOffset expiration);
    }
}