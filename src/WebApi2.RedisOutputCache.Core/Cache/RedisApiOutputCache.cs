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

        public async Task<long> GetOrIncrAsync(string key)
        {
            try
            {
                #region Lua script

                // This script returns the value if it exists. If it does not, it initializes the value to 1, and 
                //   returns that.
                const string luaScript = @"
local genId = redis.call('GET', KEYS[1])
if genId ~= false then
    return genId
end

return redis.call('INCR', KEYS[1])
";
                #endregion

                // SE.Redis helpfully notices that we're repeatedly using the same script, loads it into
                //   redis, and thereafter references it by its SHA1, saving us bandwidth.
                var result = await _redisDb.ScriptEvaluateAsync(luaScript, new RedisKey[] { key });
                return (long)result;
            }
            catch (Exception ex)
            {
                // Don't let cache server unavailability bring down the app.
                Logger.Error(ex, $"Unhandled exception in GetOrIncrAsync<long>(string) for key = {key}");
            }

            return default(long);
        }

        public async Task<long> IncrAsync(string key)
        {
            try
            {
                return await _redisDb.StringIncrementAsync(key);
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
        /// Method not supported. You should not use the KEYS command in production application code.
        /// </summary>
        /// <param name="key">The key identifying the redis SET that tracks dependent keys.</param>
        public void RemoveStartsWith(string key)
        {
            throw new NotSupportedException("Method not supported. You should not use the KEYS command in production application code.");
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
