using System.Configuration;

namespace WebApi2.RedisOutputCache.Demo.Infrastructure
{
    public static class AppConfiguration
    {
        /// <summary>
        /// Prefix of the redis pub/sub channel used to notify remote nodes to invalidate their
        /// local version id caches.
        /// </summary>
        public static string LocalCacheInvalidationChannelPrefix
        {
            get
            {
                return ConfigurationManager.AppSettings["LocalCacheInvalidationChannelPrefix"];
            }
        }
    }
}