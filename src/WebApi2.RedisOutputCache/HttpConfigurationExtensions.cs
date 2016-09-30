using System.Web.Http;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Helper class for interacting with Web API HttpConfiguration.
    /// </summary>
    public static class HttpConfigurationExtensions
    {
        /// <summary>
        /// Wrap HttpConfiguration in our cache configuration object, and return the cache configuration object.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static CacheOutputConfiguration CacheOutputConfiguration(this HttpConfiguration config)
        {
            return new CacheOutputConfiguration(config);
        }
    }
}