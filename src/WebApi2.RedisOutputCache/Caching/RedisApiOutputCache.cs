﻿using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;

namespace WebApi2.RedisOutputCache.Caching
{
    /// <summary>
    /// Redis-backed output cache for Web API.
    /// </summary>
    public class RedisApiOutputCache : IApiOutputCache
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IDatabase _redisDb;
        private readonly ISubscriber _redisPubSub;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisApiOutputCache" /> class.
        /// </summary>
        /// <param name="redisDb">The redis cache.</param>
        /// <param name="redisPubSub"></param>
        public RedisApiOutputCache(IDatabase redisDb, ISubscriber redisPubSub)
        {
            _redisDb = redisDb;
            _redisPubSub = redisPubSub;
        }

        /// <summary>
        /// Gets the specified key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public async Task<T> GetAsync<T>(string key) where T : class
        {
            try
            {
                var result = await _redisDb.StringGetAsync(key);
                if (!result.IsNull)
                {
                    return JsonConvert.DeserializeObject<T>(result.ToString());
                }
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in GetAsync<T>(string) for key = {key} and typeof(T) = {typeof(T).FullName}");
            }

            return default(T);
        }

        /// <summary>
        /// If the counter exists, get its current value. Otherwise, initialize to 1 and return it.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="localCacheEnabled"></param>
        /// <returns></returns>
        public async Task<long> GetOrIncrAsync(string key, bool localCacheEnabled)
        {
            try
            {
                if (localCacheEnabled)
                {
                    // First check to see if we have the version in our local cache.
                    var value = VersionLocalCache.Default.Get<long?>(key);
                    if (value != null)
                    {
                        // Great! We just avoided a network call.
                        return value.Value;
                    }
                }


                // We dont' have the version cached. Get it from redis (or create it if it doesn't exist).

                #region Lua script

                // This script returns the value if it exists. If it does not, it initializes the value to 1, and 
                //   returns that.
                //
                // SE.Redis helpfully notices that we're repeatedly using the same script, loads it into
                //   redis, and thereafter references it by its SHA1, saving us bandwidth.
                const string luaScript = @"
local genId = redis.call('GET', KEYS[1])
if genId ~= false then
    return genId
end

return redis.call('INCR', KEYS[1])
";
                #endregion
                
                var version = (long)await _redisDb.ScriptEvaluateAsync(luaScript, new RedisKey[] { key });
                
                // Add it to the local cache so that we can avoid a network call next time.
                VersionLocalCache.Default.Add(key, version);

                return version;
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in GetOrIncrAsync<long>(string) for key = {key}");
            }

            return default(long);
        }

        /// <summary>
        /// Increment by 1 the value associated with the specified key. If localCacheNotificationChannel is not null or whitespace,
        /// publish the key to that channel to notify any subscribers to evict that key from their local caches.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="localCacheNotificationChannel">If not null or whitespace, will publish the key to this channel to notify
        /// remote subscribes to remove key from their local caches.</param>
        /// <returns></returns>
        public async Task<long> IncrAsync(string key, string localCacheNotificationChannel = null)
        {
            try
            {
                var newId = await _redisDb.StringIncrementAsync(key);

                if (!string.IsNullOrWhiteSpace(localCacheNotificationChannel))
                {
                    // Publish a notification to the specified channel that all subscribers should evict the
                    //   key from their local caches.
                    await NotifySubscribedNodesToInvalidateLocalCacheAsync(localCacheNotificationChannel, key);
                }

                return newId;
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in IncrAsync<long>(string) for key = {key}");
            }

            return default(long);
        }

        /// <summary>
        /// Determines whether redis contains an element with the given key name.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public async Task<bool> ContainsAsync(string key)
        {
            try
            {
                return await _redisDb.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in ContainsAsync(string) for key = {key}");
            }

            return false;
        }



        /// <summary>
        /// Adds the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="expiration">The expiration.</param>
        public async Task<bool> AddAsync(string key, object value, DateTimeOffset expiration)
        {
            if (Equals(value, string.Empty))
            {
                // No reason to store an empty string.
                return false;
            }

            try
            {
                return await _redisDb.StringSetAsync(key, JsonConvert.SerializeObject(value), expiration.Subtract(DateTimeOffset.Now));
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in AddAsync(string, object, DateTimeOffset, string) for key = {key}");
            }

            return false;
        }


        /// <summary>
        /// Use redis pub/sub to notify distributed nodes that they should remove this key from their local caches.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private async Task<long> NotifySubscribedNodesToInvalidateLocalCacheAsync(string channel, string key)
        {
            try
            {
                // Optimization: immediately remove it from our local cache without waiting for a pub/sub notification to come in.
                VersionLocalCache.Default.Remove(key);

                // Notify any subscribers that they should evict this item from their local caches.
                return await _redisPubSub.PublishAsync(channel, key);
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                //TODO: retry?
                Logger.Error(ex, $"Unhandled exception in NotifyInvalidateLocalCacheAsync(string, string) for channel = {channel}, key = {key}");
            }

            return default(long);
        }
    }
}
