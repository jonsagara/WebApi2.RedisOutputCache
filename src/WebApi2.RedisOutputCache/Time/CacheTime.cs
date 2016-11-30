using System;

namespace WebApi2.RedisOutputCache.Time
{
    /// <summary>
    /// Represents various times associated with a cache entry.
    /// </summary>
    public class CacheTime
    {
        /// <summary>
        /// Client cache length in seconds
        /// </summary>
        public TimeSpan ClientTimeSpan { get; set; }

        /// <summary>
        /// Date/Time of a cache's absolute expiration.
        /// </summary>
        public DateTimeOffset AbsoluteExpiration { get; set; }
    }
}