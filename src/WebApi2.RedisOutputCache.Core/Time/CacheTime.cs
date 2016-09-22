using System;

namespace WebApi2.RedisOutputCache.Core.Time
{
    public class CacheTime
    {
        // client cache length in seconds
        public TimeSpan ClientTimeSpan { get; set; }

        public DateTimeOffset AbsoluteExpiration { get; set; }
    }
}