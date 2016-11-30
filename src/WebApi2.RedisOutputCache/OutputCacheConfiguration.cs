using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using NLog;
using StackExchange.Redis;
using WebApi2.RedisOutputCache.Caching;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// A class that lets consuming code configure the behavior of output caching.
    /// </summary>
    public class OutputCacheConfiguration
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
        public bool IsLocalCachingEnabled
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
        public string ChannelForNotificationsToInvalidateLocalCache
        {
            get
            {
                if (!IsLocalCachingEnabled)
                {
                    return null;
                }

                object val;
                if (!_configuration.Properties.TryGetValue(ChannelPrefixForNotificationsToInvalidateLocalCacheKey, out val))
                {
                    throw new InvalidOperationException($"L1 local caching is enabled, but the user hasn't provided a pub/sub channel prefix for sending/receiving local cache invalidation notifications. The prefix is required for preventing collisions with other applications.");
                }
                
                return $"{(string)val}-WebApi2.RedisOutputCache-Channel-InvalidateLocalCache";
            }
        }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="configuration">The application's existing HttpConfiguration.</param>
        public OutputCacheConfiguration(HttpConfiguration configuration)
        {
            _configuration = configuration;
        }


        /// <summary>
        /// Register an output cache provider.
        /// </summary>
        /// <param name="provider"></param>
        public void RegisterOutputCacheProvider(Func<IApiOutputCache> provider)
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
        /// <param name="mux">Your application's ConnectionMultiplexer.</param>
        /// <param name="channelPrefix">Optional. A prefix for the redis pub/sub channel used to notify distributed nodes to invalidate a
        /// key in their local caches. Use this to constrain notifications only to those apps who need them. If null or white space, local caching
        /// will remain disabled.</param>
        public void EnableLocalCaching(ConnectionMultiplexer mux, string channelPrefix = null)
        {
            if (mux == null)
            {
                throw new ArgumentNullException(nameof(mux));
            }

            if (!string.IsNullOrWhiteSpace(channelPrefix))
            {
                _configuration.Properties.GetOrAdd(ChannelPrefixForNotificationsToInvalidateLocalCacheKey, channelPrefix);
                _configuration.Properties.GetOrAdd(IsLocalCachingEnabledKey, true);

                // Subscribe to the channel that will receive notifications from other application nodes to invalidate
                //   this instance's local cache.
                mux.GetSubscriber().Subscribe(ChannelForNotificationsToInvalidateLocalCache, (ch, msg) =>
                {
                    //Logger.Trace($"Received pub/sub message to evict key '{msg}' from local cache. Channel: {ch}");

                    // Message is the key of the item we need to evict from our local cache.
                    VersionLocalCache.Default.Remove(msg);
                });

                // Hook up connection failed/restored events. When these notifications are received, invalidate the local
                //   version id cache.
                mux.ConnectionFailed += (o, e) =>
                {
                    // If the connection to redis fails, we want to clear our local cache of version ids used in output caching. We
                    //   can no longer trust that they're fresh.
                    Logger.Trace($"redis ConnectionFailed event received. Clearing all items from VersionLocalCache.");
                    VersionLocalCache.Default.Clear();
                };

                mux.ConnectionRestored += (o, e) =>
                {
                    // When the connection to redis is restored, we don't want to trust any values in the local cache, either, just
                    //   in case some values snuck in while redis was down.
                    Logger.Trace($"redis ConnectionRestored event received. Clearing all items from VersionLocalCache just to be safe.");
                    VersionLocalCache.Default.Clear();
                };
            }
            else
            {
                _configuration.Properties.GetOrAdd(IsLocalCachingEnabledKey, false);
            }
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
                ?? new CatchallCacheKeyGenerator();
        }

        /// <summary>
        /// Return an instance of the cache output provider.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public IApiOutputCache GetOutputCacheProvider(HttpRequestMessage request)
        {
            object cache;
            _configuration.Properties.TryGetValue(typeof(IApiOutputCache), out cache);

            var cacheFunc = cache as Func<IApiOutputCache>;

            var outputCacheProvider = cacheFunc != null
                ? cacheFunc()
                : request.GetDependencyScope().GetService(typeof(IApiOutputCache)) as IApiOutputCache;

            if (outputCacheProvider == null)
            {
                throw new InvalidOperationException("Unable to obtain an IApiOutputCache instance");
            }

            return outputCacheProvider;
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