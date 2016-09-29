using System;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache
{
    public class CacheOutputConfiguration
    {
        private const string RedisInvalidateLocalCacheChannelPrefixKey = "WebApi2.RedisOutputCache-InvalidateLocalCacheChannelPrefix";
        private const string RedisInvalidateLocalCacheChannelPrefixDefault = "WebApi2.RedisOutputCache-InvalidateLocalCacheChannelPrefix-Default";

        private readonly HttpConfiguration _configuration;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="configuration">The application's existing HttpConfiguration.</param>
        public CacheOutputConfiguration(HttpConfiguration configuration)
        {
            _configuration = configuration;
        }


        /// <summary>
        /// Register a cache output provider.
        /// </summary>
        /// <param name="provider"></param>
        public void RegisterCacheOutputProvider(Func<IApiOutputCache> provider)
        {
            _configuration.Properties.GetOrAdd(typeof(IApiOutputCache), x => provider);
        }

        /// <summary>
        /// Register a key generator provider.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="provider"></param>
        public void RegisterCacheKeyGeneratorProvider<T>(Func<T> provider)
            where T: ICacheKeyGenerator
        {
            _configuration.Properties.GetOrAdd(typeof (T), x => provider);
        }

        /// <summary>
        /// Register the default key generator provider.
        /// </summary>
        /// <param name="provider"></param>
        public void RegisterDefaultCacheKeyGeneratorProvider(Func<ICacheKeyGenerator> provider)
        {
            RegisterCacheKeyGeneratorProvider(provider);
        }

        /// <summary>
        /// Register a prefix for the redis pub/sub channel used to notify distributed nodes to invalidate a
        /// key in their local caches. Use this to constrain notifications only to those apps who need them.
        /// </summary>
        /// <param name="prefix"></param>
        public void RegisterRedisInvalidateLocalCacheChannelPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException($"{nameof(prefix)} cannot be null or white space", nameof(prefix));
            }

            _configuration.Properties.GetOrAdd(RedisInvalidateLocalCacheChannelPrefixKey, prefix);
        }

        /// <summary>
        /// Get the prefix of the redis pub/sub channel used to notify distributed web nodes that a cache key should
        /// be invalidated in their local caches.
        /// </summary>
        /// <returns></returns>
        public string GetRedisInvalidateLocalCacheChannel()
        {
            string prefix = null;

            object val;
            if (_configuration.Properties.TryGetValue(RedisInvalidateLocalCacheChannelPrefixKey, out val))
            {
                prefix = (string)val;
            }
            else
            {
                prefix = RedisInvalidateLocalCacheChannelPrefixDefault;
            }

            return $"{prefix}-RedisOutputCache-Channel-InvalidateLocalCache";
        }

        public string MakeBaseCachekey(string controller, string action)
        {
            return string.Format("{0}-{1}", controller.ToLower(), action.ToLower());
        }

        public string MakeBaseCachekey<T, U>(Expression<Func<T, U>> expression)
        {
            var method = expression.Body as MethodCallExpression;
            if (method == null) throw new ArgumentException("Expression is wrong");

            var methodName = method.Method.Name;
            var nameAttribs = method.Method.GetCustomAttributes(typeof(ActionNameAttribute), false);
            if (nameAttribs.Any())
            {
                var actionNameAttrib = (ActionNameAttribute) nameAttribs.FirstOrDefault();
                if (actionNameAttrib != null)
                {
                    methodName = actionNameAttrib.Name;
                }
            }

            return string.Format("{0}-{1}", typeof(T).FullName.ToLower(), methodName.ToLower());
        }

        /// <summary>
        /// Get an instance of the requested type of cache key generator.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="generatorType"></param>
        /// <returns></returns>
        public ICacheKeyGenerator GetCacheKeyGenerator(HttpRequestMessage request, Type generatorType)
        {
            generatorType = generatorType ?? typeof (ICacheKeyGenerator);

            object cache;
            _configuration.Properties.TryGetValue(generatorType, out cache);

            // If it's registered, then it's a function that returns an instance of the cache key generator.
            var cacheFunc = cache as Func<ICacheKeyGenerator>;

            // Either invoke the function to create the instance, or try to load an instance from the DI container.
            var generator = cacheFunc != null
                ? cacheFunc()
                : request.GetDependencyScope().GetService(generatorType) as ICacheKeyGenerator;

            // If we have an instance return it. If not, try to instantiate the type via reflection. If that fails,
            //   return an instance of the default cache key generator.
            return generator 
                ?? TryActivateCacheKeyGenerator(generatorType) 
                ?? new DefaultCacheKeyGenerator();
        }

        /// <summary>
        /// Return an instance of the cache output provider.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public IApiOutputCache GetCacheOutputProvider(HttpRequestMessage request)
        {
            object cache;
            _configuration.Properties.TryGetValue(typeof(IApiOutputCache), out cache);

            var cacheFunc = cache as Func<IApiOutputCache>;

            var cacheOutputProvider = cacheFunc != null 
                ? cacheFunc() 
                : request.GetDependencyScope().GetService(typeof(IApiOutputCache)) as IApiOutputCache ?? new MemoryCacheDefault();

            return cacheOutputProvider;
        }


        /// <summary>
        /// Try to instantiate an instance of ICacheKeyGenerator based on the passed in type.
        /// </summary>
        /// <param name="generatorType"></param>
        /// <returns></returns>
        private static ICacheKeyGenerator TryActivateCacheKeyGenerator(Type generatorType)
        {
            // Try to locate a parameterless constructor, or one where all parameters are optional.
            var hasEmptyOrDefaultConstructor =
                generatorType.GetConstructor(Type.EmptyTypes) != null
                || generatorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Any(x => x.GetParameters().All(p => p.IsOptional));

            return hasEmptyOrDefaultConstructor
                ? Activator.CreateInstance(generatorType) as ICacheKeyGenerator
                : null;
        }
    }
}