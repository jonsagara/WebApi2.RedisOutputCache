using System.Reflection;
using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using StackExchange.Redis;
using WebApi2.RedisOutputCache.Caching;
using WebApi2.RedisOutputCache.Demo.Caching;

namespace WebApi2.RedisOutputCache.Demo
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            // Autofac
            var builder = new ContainerBuilder();

            // Register Web API controllers.
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly());


            //
            // Register services to support redis output caching.
            //

            // StackExchange.Redis
            builder.Register<IDatabase>(ctx => RedisCache.Multiplexer.GetDatabase());
            builder.Register<ISubscriber>(ctx => RedisCache.Multiplexer.GetSubscriber());

            // Output caching
            builder.RegisterType<RedisApiOutputCache>().As<IApiOutputCache>();

            // Set the dependency resolver to be Autofac.
            var container = builder.Build();
            GlobalConfiguration.Configuration.DependencyResolver = new AutofacWebApiDependencyResolver(container);
        }
    }
}
