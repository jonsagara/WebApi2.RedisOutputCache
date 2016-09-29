using System;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using NLog;
using StackExchange.Redis;
using WebApi2.RedisOutputCache.Core.Caching;

namespace WebApi2.RedisOutputCache
{
    public class CacheOutputConfiguration
    {
        private const string IsLocalCachingEnabledKey = "NotificationsToInvalidateLocalCacheEnabled";
        private const string ChannelPrefixForNotificationsToInvalidateLocalCacheKey = "WebApi2.RedisOutputCache-InvalidateLocalCacheChannelPrefix";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly HttpConfiguration _configuration;


        /// <summary>
        /// Returns true if the user set a pub/sub channel prefix, thus enabling local caching as an L1 cache, and also
        /// enabling pub/sub communication with other web nodes for sending/receiving notifications to invalidate 
        /// local caches; false otherwise.
        /// </summary>
        internal bool IsLocalCachingEnabled
        {
            get
            {
                object enabled;
                if (_configuration.Properties.TryGetValue(IsLocalCachingEnabledKey, out enabled))
                {
                    return (bool)enabled;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the name of the redis pub/sub channel used to send/receive notifications to invalidate local L1 caches.
        /// </summary>
        /// <returns></returns>
        internal string ChannelForNotificationsToInvalidateLocalCache
        {
            get
            {
                if (!IsLocalCachingEnabled)
                {
                    throw new InvalidOperationException("Subscription to notifications to invalidate the local version cache are not enabled. Unable to retrieve the channel prefix.");
                }

                object val;
                if (!_configuration.Properties.TryGetValue(ChannelPrefixForNotificationsToInvalidateLocalCacheKey, out val))
                {
                    throw new InvalidOperationException($"L1 local caching is enabled, but the user hasn't provided a pub/sub channel prefix for sending/receiving local cache invalidation notifications. The prefix is required for preventing collisions with other applications.");
                }
                
                return $"{(string)val}-RedisOutputCache-Channel-InvalidateLocalCache";
            }
        }

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
            where T : ICacheKeyGenerator
        {
            _configuration.Properties.GetOrAdd(typeof(T), x => provider);
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
        /// Cache versions in memory, and subscribe to notifications from other nodes of this application. Those nodes will instruct us
        /// to evict a version from local cache, if present.
        /// </summary>
        /// <param name="channelPrefix">a prefix for the redis pub/sub channel used to notify distributed nodes to invalidate a
        /// key in their local caches. Use this to constrain notifications only to those apps who need them.</param>
        /// <param name="mux">Your application's ConnectionMultiplexer.</param>
        public void EnableLocalCaching(string channelPrefix, ConnectionMultiplexer mux)
        {
            if (string.IsNullOrWhiteSpace(channelPrefix))
            {
                throw new ArgumentException($"{nameof(channelPrefix)} cannot be null or white space", nameof(channelPrefix));
            }

            if (mux == null)
            {
                throw new ArgumentNullException(nameof(mux));
            }

            _configuration.Properties.GetOrAdd(ChannelPrefixForNotificationsToInvalidateLocalCacheKey, channelPrefix);
            _configuration.Properties.GetOrAdd(IsLocalCachingEnabledKey, true);

            mux.GetSubscriber().Subscribe(ChannelForNotificationsToInvalidateLocalCache, (ch, msg) =>
            {
                Logger.Trace($"Received pub/sub message to evict key '{msg}' from local cache. Channel: {ch}");

                // Message is the key of the item we need to evict from our local cache.
                VersionLocalCache.Default.Remove(msg);
            });
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
                var actionNameAttrib = (ActionNameAttribute)nameAttribs.FirstOrDefault();
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
            generatorType = generatorType ?? typeof(ICacheKeyGenerator);

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