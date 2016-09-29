using System;
using System.Collections.Concurrent;

namespace WebApi2.RedisOutputCache.Core.Caching
{
    public class VersionLocalCache
    {
        private static readonly VersionLocalCache _default = new VersionLocalCache();
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

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
