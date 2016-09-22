using System.Net.Http.Headers;
using System.Web.Http.Controllers;

namespace WebApi2.RedisOutputCache
{
    public interface ICacheKeyGenerator
    {
        string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false);
    }
}
