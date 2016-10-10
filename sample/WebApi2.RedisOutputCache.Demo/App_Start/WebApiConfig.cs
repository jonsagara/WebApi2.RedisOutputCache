using System;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using WebApi2.RedisOutputCache;
using WebApi2.RedisOutputCache.Demo.Caching;
using WebApi2.RedisOutputCache.Demo.Infrastructure;

namespace WebApi2.RedisOutputCache.Demo
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            //
            // Web API configuration and services
            //

            // Global exception handler.
            config.Services.Add(typeof(IExceptionLogger), new GlobalExceptionLogger());


            //
            // Filters
            //

            // Cache all eligible GET requests (the requests succeed, and they're not explicitly ignored).
            config.Filters.Add(new CatchallOutputCacheAttribute
            {
                ServerTimeSpan = 3600
            });


            //
            // Web API routes
            //

            config.MapHttpAttributeRoutes();

            // Use explicit attribute-based routes.
            //config.Routes.MapHttpRoute(
            //    name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{id}",
            //    defaults: new { id = RouteParameter.Optional }
            //);


            //
            // Initialize redis output caching
            //

            var outputCacheConfig = config.GetOutputCacheConfiguration();

            // Register the class that creates the full cache keys for each action.
            outputCacheConfig.RegisterDefaultCacheKeyGeneratorProvider(() => new CatchallCacheKeyGenerator());

            // Enable local caching of controller-action / argument name-value version ids that are used
            //   for invalidation. This saves multiple redis calls on GETs by reading from memory instead.
            // Local caches on distributed nodes are kept in sync via redis pub/sub.
            outputCacheConfig.EnableLocalCaching(RedisCache.Multiplexer, AppConfiguration.LocalCacheInvalidationChannelPrefix);

        }
    }
}
