using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Filters;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Provides basic functionality for consumers who wish to create their own caching attributes.
    /// </summary>
    public abstract class BaseCacheAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Interface to the underlying cache. Interacts with both the in-memory cache and redis.
        /// </summary>
        protected IApiOutputCache WebApiCache { get; private set; }

        /// <summary>
        /// Get a reference to the redis cache provider.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="req"></param>
        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            WebApiCache = config.GetOutputCacheConfiguration().GetCacheOutputProvider(req);
        }
    }
}