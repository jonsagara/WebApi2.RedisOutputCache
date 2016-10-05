using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using WebApi2.RedisOutputCache;
using WebApi2.RedisOutputCache.Core;
using WebApi2.RedisOutputCache.Core.Time;

namespace WebApi2.RedisOutputCache
{
    /***
     * 
     * ATTENTION NUGET PACKAGE USERS:
     * 
     * DO NOT MODIFY THIS FILE. IT IS INCLUDED SOLELY FOR DEBUGGING PURPOSES WHILE WE WORK OUT THE KINKS.
     * 
     * 
     ***/

    /// <summary>
    /// <para>Cache output of GET requests. The controller/action and each of the action's arguments have a version
    /// number associated with them that can be independently INCRemented in redis to invalidate a different 
    /// levels. For example, if a GET method takes customerId and patientId as parameters, a POST/PUT/DELETE
    /// can invalidate just on patientId so that only that patient's data is invalidated, and not all patient
    /// data for that customer.</para>
    /// <para>Essentially, this is performant way of doing a &quot;Remove Keys Starting With&quot; cache invalidation
    /// strategy in redis. This is necessary because KEYs is O(N), and the official redis docs recommend you do NOT
    /// use it regularly in production.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class CatchallOutputCacheAttribute : BaseCacheAttribute
    {
        private const string CurrentRequestMediaType = nameof(CatchallOutputCacheAttribute) + ":CurrentRequestMediaType";

        /// <summary>
        /// Don't compute this twice if we don't have to. Create in OnActionExecuting, store in HttpContext.Items, 
        /// reference again in OnActionExecuted.
        /// </summary>
        private const string FullCacheKey = nameof(CatchallOutputCacheAttribute) + ":FullCacheKey";


        #region Properties and fields copied from CacheOutputAttribute

        private static readonly MediaTypeHeaderValue DefaultMediaType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.HeaderName };

        /// <summary>
        /// Cache enabled only for requests when Thread.CurrentPrincipal is not set
        /// </summary>
        public bool AnonymousOnly { get; set; }

        /// <summary>
        /// How long response should be cached on the server side (in seconds). Defaults to 3600 seconds.
        /// </summary>
        public int ServerTimeSpan { get; set; } = 3600;

        /// <summary>
        /// Corresponds to CacheControl MaxAge HTTP header (in seconds)
        /// </summary>
        public int ClientTimeSpan { get; set; }

        /// <summary>
        /// Corresponds to CacheControl NoCache HTTP header
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// Corresponds to CacheControl Private HTTP header. Response can be cached by browser but not by intermediary cache
        /// </summary>
        public bool Private { get; set; }

        /// <summary>
        /// Corresponds to MustRevalidate HTTP header - indicates whether the origin server requires revalidation of a cache entry on any subsequent use when the cache entry becomes stale
        /// </summary>
        public bool MustRevalidate { get; set; }


        internal IModelQuery<DateTime, CacheTime> CacheTimeQuery;

        #endregion


        /// <summary>
        /// Runs before the action executes. If we find a matching ETag, return 304 Not Modified. If we have the response
        /// bytes cached, return them. Otherwise, let the request continue.
        /// </summary>
        /// <param name="actionContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            if (actionContext == null)
            {
                throw new ArgumentNullException(nameof(actionContext));
            }

            // Make sure we obey anonymity if set, or attributes to ignore caching.
            if (!IsCachingAllowed(actionContext, AnonymousOnly))
            {
                return;
            }

            // If not set by the constructor, grab the controller and action names from the action context.
            var controllerLowered = actionContext.ControllerContext.Controller.GetType().FullName.ToLower();
            var actionLowered = actionContext.ActionDescriptor.ActionName.ToLower();

            var config = actionContext.Request.GetConfiguration();

            // Ensure that we have properly set certain properties.
            EnsureCacheTimeQuery();
            EnsureCache(config, actionContext.Request);

            // Get the media type that the client expects.
            var responseMediaType = GetExpectedMediaType(config, actionContext);
            actionContext.Request.Properties[CurrentRequestMediaType] = responseMediaType;


            //
            // Generate the cache key for this action. It will look something like this:
            //
            //   somenamespace.controllers.somecontroller-someaction_v1-customerid=3583_v1&userid=31eb2386-1b98-4a5d-bba0-a62d008ea976_v1&qsparam1=ohai:application/json; charset=utf-8"
            //

            var cacheKeyGenerator = config.GetOutputCacheConfiguration().GetCacheKeyGenerator(actionContext.Request, typeof(CatchallCacheKeyGenerator));
            var fullCacheKey = await cacheKeyGenerator.MakeCacheKeyAsync(WebApiCache, actionContext, responseMediaType, controllerLowered, actionLowered);
            actionContext.Request.Properties[FullCacheKey] = fullCacheKey;

            if (!(await WebApiCache.ContainsAsync(fullCacheKey)))
            {
                // Output for this action with these parameters is not in the cache, so we can't short circuit the request. Let it continue.
                return;
            }

            // Check to see if we have any cached requests that match by ETag.
            var etagCacheKey = fullCacheKey + Constants.EtagKey;

            if (actionContext.Request.Headers.IfNoneMatch != null)
            {
                // Try to get the ETag from cache.
                var etag = await WebApiCache.GetAsync<string>(etagCacheKey);
                if (etag != null)
                {
                    // There is an ETag in the cache for this request. Does it match the any of the ETags sent by the client?
                    if (actionContext.Request.Headers.IfNoneMatch.Any(x => x.Tag == etag))
                    {
                        // Yes! Send them a 304 Not Modified response.
                        var time = CacheTimeQuery.Execute(DateTime.Now);
                        var quickResponse = actionContext.Request.CreateResponse(HttpStatusCode.NotModified);
                        ApplyCacheHeaders(quickResponse, time);
                        actionContext.Response = quickResponse;

                        return;
                    }
                }
            }

            // No matching ETags. See if we have the actual response bytes cached.
            var val = await WebApiCache.GetAsync<byte[]>(fullCacheKey);
            if (val == null)
            {
                // No response bytes cached for this action/parameters. Let the request continue.
                return;
            }


            //
            // We have a cached response. Send it back to the caller instead of exeucting the full request.
            //

            // Get the content type for the request. MediaTypeHeaderValue is not serializable (it deserializes in a 
            //   very strange state with duplicate charset attributes), so we're going cache it as a string and
            //   then parse it here.
            var contentTypeCached = await WebApiCache.GetAsync<string>(fullCacheKey + Constants.ContentTypeKey);

            MediaTypeHeaderValue contentType;
            if (!MediaTypeHeaderValue.TryParse(contentTypeCached, out contentType))
            {
                // That didn't work. Extract it from the cache key.
                contentType = new MediaTypeHeaderValue(GetMediaTypeFromFullCacheKey(fullCacheKey));
            }

            // Create a new response and populated it with the cached bytes.
            actionContext.Response = actionContext.Request.CreateResponse();
            actionContext.Response.Content = new ByteArrayContent(val);
            actionContext.Response.Content.Headers.ContentType = contentType;

            // If there is a cached ETag, add it to the response.
            var responseEtag = await WebApiCache.GetAsync<string>(etagCacheKey);
            if (responseEtag != null)
            {
                SetEtag(actionContext.Response, responseEtag);
            }

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            ApplyCacheHeaders(actionContext.Response, cacheTime);
        }


        /// <summary>
        /// If allowed for this request, cache the rendered bytes, content type, and ETag.
        /// </summary>
        /// <param name="actionExecutedContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            // If the request failed, there is nothing to cache.
            if (actionExecutedContext.ActionContext.Response == null || !actionExecutedContext.ActionContext.Response.IsSuccessStatusCode)
            {
                return;
            }

            // Don't try to cache if we shouldn't.
            if (!IsCachingAllowed(actionExecutedContext.ActionContext, AnonymousOnly))
            {
                return;
            }

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            if (cacheTime.AbsoluteExpiration > DateTime.Now)
            {
                var httpConfig = actionExecutedContext.Request.GetConfiguration();
                var config = httpConfig.GetOutputCacheConfiguration();
                var responseMediaType = actionExecutedContext.Request.Properties[CurrentRequestMediaType] as MediaTypeHeaderValue ?? GetExpectedMediaType(httpConfig, actionExecutedContext.ActionContext);
                var fullCacheKey = actionExecutedContext.Request.Properties[FullCacheKey] as string;

                if (!string.IsNullOrWhiteSpace(fullCacheKey) && !(await WebApiCache.ContainsAsync(fullCacheKey)))
                {
                    // Add an ETag to the response.
                    SetEtag(actionExecutedContext.Response, CreateEtag());

                    var responseContent = actionExecutedContext.Response.Content;
                    if (responseContent != null)
                    {
                        var contentType = responseContent.Headers.ContentType.ToString();
                        var etag = actionExecutedContext.Response.Headers.ETag.Tag;
                        var contentBytes = await responseContent.ReadAsByteArrayAsync().ConfigureAwait(false);

                        responseContent.Headers.Remove("Content-Length");

                        // Cache the content bytes, content type, and ETag.
                        await WebApiCache.AddAsync(fullCacheKey, contentBytes, cacheTime.AbsoluteExpiration);
                        await WebApiCache.AddAsync(fullCacheKey + Constants.ContentTypeKey, contentType, cacheTime.AbsoluteExpiration);
                        await WebApiCache.AddAsync(fullCacheKey + Constants.EtagKey, etag, cacheTime.AbsoluteExpiration);
                    }
                }
            }

            ApplyCacheHeaders(actionExecutedContext.ActionContext.Response, cacheTime);
        }


        /// <summary>
        /// Obtains the media type from the full cachey key.
        /// </summary>
        /// <param name="fullCacheKey"></param>
        /// <returns></returns>
        private string GetMediaTypeFromFullCacheKey(string fullCacheKey)
        {
            var mediaTypeFull = fullCacheKey.Split(new[] { CatchallCacheKeyGenerator.MediaTypeSeparator }, StringSplitOptions.RemoveEmptyEntries)[1];
            return mediaTypeFull.Split(';')[0];
        }


        #region Methods copied from CacheOutputAttribute and made private

        private void EnsureCacheTimeQuery()
        {
            if (CacheTimeQuery == null) ResetCacheTimeQuery();
        }

        private void ResetCacheTimeQuery()
        {
            CacheTimeQuery = new ShortTime(ServerTimeSpan, ClientTimeSpan);
        }

        private bool IsCachingAllowed(HttpActionContext actionContext, bool anonymousOnly)
        {
            if (anonymousOnly)
            {
                if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                {
                    return false;
                }
            }

            if (actionContext.ActionDescriptor.GetCustomAttributes<IgnoreOutputCacheAttribute>().Any())
            {
                return false;
            }

            return actionContext.Request.Method == HttpMethod.Get;
        }

        private MediaTypeHeaderValue GetExpectedMediaType(HttpConfiguration config, HttpActionContext actionContext)
        {
            MediaTypeHeaderValue responseMediaType = null;

            var negotiator = config.Services.GetService(typeof(IContentNegotiator)) as IContentNegotiator;
            var returnType = actionContext.ActionDescriptor.ReturnType;

            if (negotiator != null && returnType != typeof(HttpResponseMessage) && (returnType != typeof(IHttpActionResult) || typeof(IHttpActionResult).IsAssignableFrom(returnType)))
            {
                var negotiatedResult = negotiator.Negotiate(returnType, actionContext.Request, config.Formatters);

                if (negotiatedResult == null)
                {
                    return DefaultMediaType;
                }

                responseMediaType = negotiatedResult.MediaType;
                if (string.IsNullOrWhiteSpace(responseMediaType.CharSet))
                {
                    responseMediaType.CharSet = Encoding.UTF8.HeaderName;
                }
            }
            else
            {
                if (actionContext.Request.Headers.Accept != null)
                {
                    responseMediaType = actionContext.Request.Headers.Accept.FirstOrDefault();
                    if (responseMediaType == null || !config.Formatters.Any(x => x.SupportedMediaTypes.Contains(responseMediaType)))
                    {
                        return DefaultMediaType;
                    }
                }
            }

            return responseMediaType;
        }

        private void ApplyCacheHeaders(HttpResponseMessage response, CacheTime cacheTime)
        {
            if (cacheTime.ClientTimeSpan > TimeSpan.Zero || MustRevalidate || Private)
            {
                var cachecontrol = new CacheControlHeaderValue
                {
                    MaxAge = cacheTime.ClientTimeSpan,
                    MustRevalidate = MustRevalidate,
                    Private = Private
                };

                response.Headers.CacheControl = cachecontrol;
            }
            else if (NoCache)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                response.Headers.Add("Pragma", "no-cache");
            }
        }

        private static void SetEtag(HttpResponseMessage message, string etag)
        {
            if (etag != null)
            {
                var eTag = new EntityTagHeaderValue(@"""" + etag.Replace("\"", string.Empty) + @"""");
                message.Headers.ETag = eTag;
            }
        }

        private string CreateEtag()
        {
            return Guid.NewGuid().ToString();
        }

        #endregion
    }
}
