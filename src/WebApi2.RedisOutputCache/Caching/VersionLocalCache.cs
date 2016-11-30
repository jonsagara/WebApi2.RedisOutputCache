using System;
using System.Collections.Concurrent;

namespace WebApi2.RedisOutputCache.Caching
{
    /// <summary>
    /// An in-memory cache for storing parameter version numbers so that we don't have to make a 
    /// network call every time we want to retrieve one.
    /// </summary>
    public class VersionLocalCache
    {
        private static readonly VersionLocalCache _default = new VersionLocalCache();

        /// <summary>
        /// The default instance. A singleton.
        /// </summary>
        public static VersionLocalCache Default => _default;


        private readonly ConcurrentDictionary<string, object> _cache = new ConcurrentDictionary<string, object>();

        /// <summary>
        /// Add the value. If the key already exists, overwrite the existing value with the new value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public T Add<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"{nameof(key)} cannot be null or white space", nameof(key));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), "Null values are not allowed");
            }

            return (T)_cache.AddOrUpdate(key, value, (k, v) => value);
        }

        /// <summary>
        /// Get the specified value from the cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public T Get<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"{nameof(key)} cannot be null or white space", nameof(key));
            }

            object val;
            if (_cache.TryGetValue(key, out val))
            {
                return (T)val;
            }

            return default(T);
        }

        /// <summary>
        /// Remove the item from the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"{nameof(key)} cannot be null or white space", nameof(key));
            }

            object val;
            if (_cache.TryRemove(key, out val))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Completely clear the cache.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }
    }
}
