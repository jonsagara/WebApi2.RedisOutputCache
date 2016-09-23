using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;

namespace WebApi2.RedisOutputCache.Core.Cache
{
    /// <summary>
    /// Redis-backed output cache for Web API.
    /// </summary>
    public class RedisApiOutputCache : IApiOutputCache
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IDatabase _redisDb;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisApiOutputCache" /> class.
        /// </summary>
        /// <param name="redisDb">The redis cache.</param>
        public RedisApiOutputCache(IDatabase redisDb)
        {
            _redisDb = redisDb;
        }


        /// <summary>
        /// Remove the set identifying the base key. This effectively invalidates the cache for all related URLs.
        /// </summary>
        /// <param name="key">The key identifying the redis SET that tracks dependent keys.</param>
        public async Task RemoveStartsWithAsync(string key)
        {
            try
            {
                // SE.Redis's implementation of KEYS uses the cursor-based SCAN, which is dreadfully slow. Instead, use
                //   a lua script to get the list of matching keys starting with the value of key.
                const string luaScript_GetKeysStartingWith = "return redis.call('KEYS', ARGV[1] .. '*')";

                var redisResult = await _redisDb.ScriptEvaluateAsync(luaScript_GetKeysStartingWith, null, new RedisValue[] { key });
                var matchingKeys = (RedisValue[])redisResult;

                if (matchingKeys.Length > 0)
                {
                    await _redisDb.KeyDeleteAsync(matchingKeys.Select(mk => (RedisKey)(string)mk).ToArray());
                }
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in RemoveStartsWithAsync(string) for key = {key}");
            }
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

        public async Task<string[]> GetSetMembersAsync(string key)
        {
            try
            {
                var result = await _redisDb.SetMembersAsync(key);
                return result.Select(m => (string)m).ToArray();
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in GetSetMembersAsync<T>(string) for key = {key}");
            }

            return new string[0];
        }

        public async Task<long> RemoveAsync(string[] keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            try
            {
                if (keys.Length > 0)
                {
                    return await _redisDb.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
                }
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in RemoveAsync<T>(string[]) for keys = '{string.Join(", ", keys)}'");
            }

            return 0L;
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
        /// <param name="dependsOnKey">The depends on key.</param>
        public async Task AddAsync(string key, object value, DateTimeOffset expiration, string dependsOnKey = null)
        {
            if (Equals(value, string.Empty))
            {
                // No reason to store an empty string.
                return;
            }

            try
            {
                var primaryAdded = await _redisDb.StringSetAsync(key, JsonConvert.SerializeObject(value), expiration.Subtract(DateTimeOffset.Now));

                if (dependsOnKey != null && primaryAdded)
                {
                    await _redisDb.SetAddAsync(dependsOnKey, key);
                }
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in AddAsync(string, object, DateTimeOffset, string) for key = {key} and dependsOnKey = {dependsOnKey}");
            }
        }

        public async Task<long> AddSetAsync(string key, string[] values)
        {
            try
            {
                return await _redisDb.SetAddAsync(key, values.Select(v => (RedisValue)v).ToArray());
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in AddSetAsync(string, string[]) for key = {key}");
            }

            return default(long);
        }


        #region Unsupported Properties and Methods

        /// <summary>
        /// Not supported. The redis KEYS command is expensive. Find another way to do what you're trying to do.
        /// </summary>
        public IEnumerable<string> AllKeys
        {
            get
            {
                throw new NotSupportedException("The redis KEYS command is expensive. Find another way to do what you're trying to do.");
            }
        }

        /// <summary>
        /// Synchronous method not supported. Use RemoveStartsWithAsync&lt;T&gt; instead.
        /// </summary>
        /// <param name="key">The key identifying the redis SET that tracks dependent keys.</param>
        public void RemoveStartsWith(string key)
        {
            throw new NotSupportedException("Synchronous method not supported. Use RemoveStartsWithAsync<T> instead.");
        }

        /// <summary>
        /// Synchronous method not supported. Use GetAsync&lt;T&gt; instead
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public T Get<T>(string key) where T : class
        {
            throw new NotSupportedException("Synchronous method not supported. Use GetAsync<T> instead.");
        }

        /// <summary>
        /// Removes the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        public void Remove(string key)
        {
            throw new NotImplementedException("Method not used by the redis provider.");
        }

        /// <summary>
        /// Synchronous method not supported. Use ContainsAsync&lt;T&gt; instead.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public bool Contains(string key)
        {
            throw new NotSupportedException("Synchronous method not supported. Use ContainsAsync<T> instead.");
        }

        /// <summary>
        /// Synchronous method not supported. Use AddAsync instead.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="expiration">The expiration.</param>
        /// <param name="dependsOnKey">The depends on key.</param>
        public void Add(string key, object value, DateTimeOffset expiration, string dependsOnKey = null)
        {
            throw new NotSupportedException("Synchronous method not supported. Use AddAsync instead.");
        }

        #endregion
    }
}
