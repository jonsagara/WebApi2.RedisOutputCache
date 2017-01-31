using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using WebApi2.RedisOutputCache.Caching;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Interface for cache key generators.
    /// </summary>
    public interface ICacheKeyGenerator
    {
        /// <summary>
        /// Create a cache to for storing content.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="actionContext"></param>
        /// <param name="mediaType"></param>
        /// <param name="controllerLowered"></param>
        /// <param name="actionLowered"></param>
        /// <param name="varyByUserAgent"></param>
        /// <returns></returns>
        Task<string> MakeCacheKeyAsync(IApiOutputCache cache, HttpActionContext actionContext, MediaTypeHeaderValue mediaType, string controllerLowered, string actionLowered, bool varyByUserAgent);
    }
}
