using System;
using System.Web.Http;
using System.Web.Http.SelfHost;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
#warning TODO: redis
            //var config = new HttpSelfHostConfiguration("http://localhost:999");
            //config.MapHttpAttributeRoutes();
            //config.Routes.MapHttpRoute(
            //      name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{id}",
            //    defaults: new { id = RouteParameter.Optional }
            //);
            //var server = new HttpSelfHostServer(config);

            //config.CacheOutputConfiguration().RegisterCacheOutputProvider(() => new MemoryCacheDefault());

            //server.OpenAsync().Wait();

            //Console.ReadKey();

            //server.CloseAsync().Wait();
        }
    }
}
