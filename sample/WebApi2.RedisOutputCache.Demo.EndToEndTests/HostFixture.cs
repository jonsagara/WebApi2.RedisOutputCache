using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using Autofac;
using Autofac.Integration.WebApi;
using StackExchange.Redis;
using WebApi2.RedisOutputCache.Core.Caching;
using WebApi2.RedisOutputCache.Demo.Caching;

namespace WebApi2.RedisOutputCache.Demo.EndToEndTests
{
    public class HostFixture
    {
        private static readonly HttpServer _server;
        private static readonly HttpClient _client;

        static HostFixture()
        {
            var config = new HttpConfiguration();

            // Initial configuration.
            WebApiConfig.Register(config);

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
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

            _server = new HttpServer(config);
            _client = new HttpClient(_server);
        }

        public HttpServer Server => _server;
        public HttpClient Client => _client;
    }
}
