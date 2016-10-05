using System.Web.Http.Filters;

namespace WebApi2.RedisOutputCache.Demo
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(HttpFilterCollection filters)
        {
            //filters.Add(new HandleErrorAttribute());
        }
    }
}
