using System.Web.Http;
using System.Web.Http.ExceptionHandling;
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
            // Web API routes
            //

            config.MapHttpAttributeRoutes();

            // Use explicit attribute-based routes.
            //config.Routes.MapHttpRoute(
            //    name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{id}",
            //    defaults: new { id = RouteParameter.Optional }
            //);
        }
    }
}
