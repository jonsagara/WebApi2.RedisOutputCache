using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace WebApi2.RedisOutputCache.Core.Cache
{
    /// <summary>
    /// In-memory cache. Does not support async operations. Calling them will result in a NotSupportedException.
    /// </summary>
    public class MemoryCacheDefault : IApiOutputCache
    {
        private static readonly MemoryCache Cache = MemoryCache.Default;

        public virtual void RemoveStartsWith(string key)
        {
            lock (Cache)
            {
                Cache.Remove(key);
            }
        }

        public Task RemoveStartsWithAsync(string key)
        {
            throw new NotSupportedException();
        }

        public virtual void RemoveStartsWith(string baseKey, string key)
        {
            // Doesn't make sense with the MemoryCacheDefault implementation. Just use the default behavior
            //   of using dependent keys and cache invalidation callbacks.
            throw new NotImplementedException("For MemoryCacheDefault, use RemoveStartsWith(string)");
        }

        public virtual T Get<T>(string key) where T : class
        {
            var o = Cache.Get(key) as T;
            return o;
        }

        public Task<T> GetAsync<T>(string key) where T : class
        {
            throw new NotSupportedException();
        }

        [Obsolete("Use Get<T> instead")]
        public virtual object Get(string key)
        {
            return Cache.Get(key);
        }

        public Task<object> GetAsync(string key)
        {
            throw new NotSupportedException();
        }

        public virtual void Remove(string key)
        {
            lock (Cache)
            {
                Cache.Remove(key);
            }
        }

        public virtual bool Contains(string key)
        {
            return Cache.Contains(key);
        }

        public Task<bool> ContainsAsync(string key)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ContainsAsync(string baseKey, string key)
        {
            throw new NotSupportedException();
        }

        public virtual void Add(string key, object o, DateTimeOffset expiration, string dependsOnKey = null)
        {
            var cachePolicy = new CacheItemPolicy
            {
                AbsoluteExpiration = expiration
            };

            if (!string.IsNullOrWhiteSpace(dependsOnKey))
            {
                cachePolicy.ChangeMonitors.Add(
                    Cache.CreateCacheEntryChangeMonitor(new[] { dependsOnKey })
                );
            }
            lock (Cache)
            {
                Cache.Add(key, o, cachePolicy);
            }
        }

        public Task AddAsync(string key, object o, DateTimeOffset expiration, string dependsOnKey = null)
        {
            throw new NotSupportedException();
        }        

        public virtual IEnumerable<string> AllKeys
        {
            get
            {
                return Cache.Select(x => x.Key);
            }
        }
    }
}
