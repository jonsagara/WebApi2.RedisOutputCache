using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache
{
    public interface ICacheKeyGenerator
    {
        Task<string> MakeCacheKeyAsync(IApiOutputCache cache, HttpActionContext actionContext, MediaTypeHeaderValue mediaType, string controllerLowered, string actionLowered);
    }
}
