using System;

namespace WebApi2.RedisOutputCache.Time
{
    /// <summary>
    /// Creates a <see cref="CacheTime"/> from the specified DateTime and the provided client and server times in seconds.
    /// </summary>
    public class ShortTime : IModelQuery<DateTime, CacheTime>
    {
        private readonly int _serverTimeInSeconds;
        private readonly int _clientTimeInSeconds;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="serverTimeInSeconds"></param>
        /// <param name="clientTimeInSeconds"></param>
        public ShortTime(int serverTimeInSeconds, int clientTimeInSeconds)
        {
            if (serverTimeInSeconds < 0)
                serverTimeInSeconds = 0;

            _serverTimeInSeconds = serverTimeInSeconds;

            if (clientTimeInSeconds < 0)
                clientTimeInSeconds = 0;

            _clientTimeInSeconds = clientTimeInSeconds;
        }

        /// <summary>
        /// Create the CacheTime object.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public CacheTime Execute(DateTime model)
        {
            var cacheTime = new CacheTime
                {
                    AbsoluteExpiration = model.AddSeconds(_serverTimeInSeconds),
                    ClientTimeSpan = TimeSpan.FromSeconds(_clientTimeInSeconds)
                };

            return cacheTime;
        }
    }
}