﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebApi2.RedisOutputCache.Core.Caching
{
    public interface IApiOutputCache
    {
        void RemoveStartsWith(string key);

        T Get<T>(string key) where T : class;
        Task<T> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// If the counter exists, get its current value. Otherwise, initialize to 1 and return it.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="localCacheEnabled"></param>
        /// <returns></returns>
        Task<long> GetOrIncrAsync(string key, bool localCacheEnabled);

        /// <summary>
        /// Increment by 1 the value associated with the specified key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<long> IncrAsync(string key);

        /// <summary>
        /// Use redis pub/sub to notify distributed nodes that they should remove this key from their local caches.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<long> NotifySubscribedNodesToInvalidateLocalCacheAsync(string channel, string key);

        void Remove(string key);

        bool Contains(string key);
        Task<bool> ContainsAsync(string key);

        void Add(string key, object o, DateTimeOffset expiration, string dependsOnKey = null);
        Task AddAsync(string key, object o, DateTimeOffset expiration, string dependsOnKey = null);

        IEnumerable<string> AllKeys { get; }
    }
}