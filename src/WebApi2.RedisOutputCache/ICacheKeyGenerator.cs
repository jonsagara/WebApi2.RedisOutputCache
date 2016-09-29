using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache
{
    public interface ICacheKeyGenerator
    {
        string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false);

        Task<string> MakeCacheKeyAsync(IApiOutputCache cache, HttpActionContext actionContext, MediaTypeHeaderValue mediaType, string controllerLowered, string actionLowered);
    }
}
