using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using WebApi2.RedisOutputCache.Core;
using WebApi2.RedisOutputCache.Core.Time;

namespace WebApi2.RedisOutputCache
{
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
    public class CatchallOutputCacheAttribute : BaseOutputCacheAttribute
    {
        private const string CurrentRequestMediaType = nameof(CatchallOutputCacheAttribute) + ":CurrentRequestMediaType";

        /// <summary>
        /// Don't compute this twice if we don't have to. Create in OnActionExecuting, reference in OnActionExecuted.
        /// </summary>
        private string _fullCacheKey;


        #region Properties and fields copied from CacheOutputAttribute

        private static readonly MediaTypeHeaderValue DefaultMediaType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.HeaderName };

        /// <summary>
        /// Cache enabled only for requests when Thread.CurrentPrincipal is not set
        /// </summary>
        public bool AnonymousOnly { get; set; }

        /// <summary>
        /// Class used to generate caching keys
        /// </summary>
        public Type CacheKeyGenerator { get; set; }

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

            _fullCacheKey = await MakeFullCacheKeyAsync(actionContext, responseMediaType, controllerLowered, actionLowered);
            if (!(await WebApiCache.ContainsAsync(_fullCacheKey)))
            {
                // Output for this action with these parameters is not in the cache, so we can't short circuit the request. Let it continue.
                return;
            }

            // Check to see if we have any cached requests that match by ETag.
            var etagCacheKey = _fullCacheKey + Constants.EtagKey;

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
            var val = await WebApiCache.GetAsync<byte[]>(_fullCacheKey);
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
            var contentTypeCached = await WebApiCache.GetAsync<string>(_fullCacheKey + Constants.ContentTypeKey);

            MediaTypeHeaderValue contentType;
            if (!MediaTypeHeaderValue.TryParse(contentTypeCached, out contentType))
            {
                // That didn't work. Extract it from the cache key.
                contentType = new MediaTypeHeaderValue(_fullCacheKey.Split(new[] { ':' }, 2)[1].Split(';')[0]);
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
                var config = httpConfig.CacheOutputConfiguration();
                var responseMediaType = actionExecutedContext.Request.Properties[CurrentRequestMediaType] as MediaTypeHeaderValue ?? GetExpectedMediaType(httpConfig, actionExecutedContext.ActionContext);

                if (!string.IsNullOrWhiteSpace(_fullCacheKey) && !(await WebApiCache.ContainsAsync(_fullCacheKey)))
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
                        await WebApiCache.AddAsync(_fullCacheKey, contentBytes, cacheTime.AbsoluteExpiration);
                        await WebApiCache.AddAsync(_fullCacheKey + Constants.ContentTypeKey, contentType, cacheTime.AbsoluteExpiration);
                        await WebApiCache.AddAsync(_fullCacheKey + Constants.EtagKey, etag, cacheTime.AbsoluteExpiration);
                    }
                }
            }

            ApplyCacheHeaders(actionExecutedContext.ActionContext.Response, cacheTime);
        }

        /// <summary>
        /// Generates a cache key containing the namespace/controller/action, and name/value pairs for action arguments.
        /// Query string parameters that do not map to an action parameter are not included.
        /// </summary>
        /// <param name="actionContext"></param>
        /// <param name="mediaType"></param>
        /// <param name="controllerLowered"></param>
        /// <param name="actionLowered"></param>
        /// <returns></returns>
        private async Task<string> MakeFullCacheKeyAsync(HttpActionContext actionContext, MediaTypeHeaderValue mediaType, string controllerLowered, string actionLowered)
        {
            // The default set of action argument names/values that make up the cache key:
            //   * name=value of all default URI-bindlable action parameters.
            //   * name=val1;val2;val3 of all URI-bindable IEnumerable action parameters.
            //   * prop1name=prop1val, etc. of all public instance properties of URI-bindable, non-IEnumerable action parameters (aka, view models or DTOs).
            //
            // The key point is that they must match up to a named parameter so that the invalidation logic can
            //   access the value and increment its counter.

            var allActionParameters = actionContext.ActionDescriptor.GetParameters();


            //
            // Get name=value pairs from "simple" Web API action parameters (i.e., URI-bound by default).
            //

            var defaultUriBindableArgNamesValues = new List<KeyValuePair<string, string>>();
            var defaultUriBindableActionParams = allActionParameters.Where(ap => IsDefaultUriBindableType(ap.ParameterType)).ToList();

            foreach (var defaultUriBindableActionParam in defaultUriBindableActionParams)
            {
                var actionArg = actionContext.ActionArguments.Single(kvp => kvp.Key == defaultUriBindableActionParam.ParameterName);
                defaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(actionArg.Key, GetValueAsString(actionArg.Value)));
            }


            //
            // Get name=value pairs from complex types that are explicitly URI-bound via FromUriAttribute.
            //

            var nonDefaultUriBindableArgNamesValues = new List<KeyValuePair<string, string>>();
            var nonDefaultUriBindableActionParams = allActionParameters.Where(ap => !IsDefaultUriBindableType(ap.ParameterType) && IsUriBindableParameter(ap)).ToList();

            foreach (var nonDefaultUriBindableActionParam in nonDefaultUriBindableActionParams)
            {
                // Get the corresponding action argument matching the parameter name.
                var actionArg = actionContext.ActionArguments.Single(kvp => kvp.Key == nonDefaultUriBindableActionParam.ParameterName);

                if (typeof(IEnumerable).IsAssignableFrom(nonDefaultUriBindableActionParam.ParameterType))
                {
                    // It's an array or list of some type. Join its values as a semicolon-separated string.
                    nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(actionArg.Key, GetValueAsString(actionArg.Value)));
                }
                else
                {
                    // It's a view model/dto. We need its public instance property names and values.
                    if (actionArg.Value != null)
                    {
                        // Get the names/values of its public instance properties.
                        var pubInstProps = actionArg.Value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var pubInstProp in pubInstProps)
                        {
                            nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(pubInstProp.Name, GetValueAsString(pubInstProp.GetValue(actionArg.Value))));
                        }
                    }
                    else
                    {
                        // The object is null. We still need its public instance property names. If a query string parameter of the 
                        //   same name exists, we'll use its value. Otherwise, we'll use the value from a new instance of the parameter
                        //   type.
                        var objInstance = Activator.CreateInstance(nonDefaultUriBindableActionParam.ParameterType);
                        var pubInstProps = nonDefaultUriBindableActionParam.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                        // Exclude jsonp callback parameters, if any.
                        var qsParams = actionContext.Request.GetQueryNameValuePairs()
                            .Where(x => x.Key.ToLower() != "callback")
                            .ToArray();

                        foreach (var pubInstProp in pubInstProps)
                        {
                            // Case insenstitive compare because query string parameter names can come in as any case.
                            var matchingQsParams = qsParams.Where(kvp => kvp.Key.Equals(pubInstProp.Name, StringComparison.OrdinalIgnoreCase)).ToArray();

                            if (matchingQsParams.Length > 0)
                            {
                                // When the target is a non-collection, scalar value, the default model binder only selects the first value if 
                                //   there are multiple query string parameters with the same name. Mimic that behavior here.
                                // For consistency, use the casing of the property name.
                                nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(pubInstProp.Name, matchingQsParams[0].Value));
                            }
                            else
                            {
                                // Punt. We don't have anywhere in the current request from which to grab a value, so take the default value 
                                //   of the matching property on the instance we created above.
                                nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(pubInstProp.Name, GetValueAsString(pubInstProp.GetValue(objInstance))));
                            }
                        }
                    }
                }
            }

            // Combine default URI-bindable arg names/values with non-default URI-bindable arg names/values.
            //TODO: look for and combine args with same name from default and non-default?
            var allArgNameValues = defaultUriBindableArgNamesValues
                .Concat(nonDefaultUriBindableArgNamesValues)
                .OrderBy(kvp => kvp.Key)
                .ToList();

            // Get the versions for the controller/action, and for each argument name/value.
            var controllerActionVersionId = await GetControllerActionVersionIdAsync(controllerLowered, actionLowered);

            var finalList = new List<string>();

            foreach (var argNameValue in allArgNameValues)
            {
                var argNameLowered = argNameValue.Key.ToLower();

                // Get or create the argument name/value version from redis. It is scoped at the namespace/controller/action level, so 
                //   it will be unique.
                var key = CacheKey.ControllerActionArgumentVersion(controllerLowered, actionLowered, argNameLowered, argNameValue.Value);
                var version = await WebApiCache.GetOrIncrAsync(key);

                finalList.Add(CacheKey.VersionedArgumentNameAndValue(argNameLowered, argNameValue.Value?.Trim(), version));
            }

            var parameters = $"-{string.Join("&", finalList)}";
            if (parameters == "-")
            {
                parameters = string.Empty;
            }

            return $"{controllerLowered}-{actionLowered}_v{controllerActionVersionId}{parameters}:{mediaType}";
        }

        /// <summary>
        /// <para>Returns true if t represents one of the &quot;simple&quot; Web API types that are automatically URI-bound; false otherwise.</para>
        /// <para>See: http://www.asp.net/web-api/overview/formats-and-model-binding/parameter-binding-in-aspnet-web-api</para>
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private bool IsDefaultUriBindableType(Type t)
        {
            if (t == null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            if (t.IsPrimitive)
            {
                // The primitive types are Boolean, Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, IntPtr, UIntPtr, Char, Double, and Single.
                //   Web API will URI-bind primitive types by default.
                //   See: https://msdn.microsoft.com/en-us/library/system.type.isprimitive(v=vs.110).aspx
                return true;
            }

            if (t == typeof(TimeSpan) || t == typeof(DateTime) || t == typeof(decimal) || t == typeof(Guid) || t == typeof(string))
            {
                // Web API will also URI-bind these "simple" types.
                //   See: http://www.asp.net/web-api/overview/formats-and-model-binding/parameter-binding-in-aspnet-web-api
                return true;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                // It's a nullable value type (e.g., int?, decimal?, DateTime?). Web API will URI-bind these.
                return true;
            }

            // We'll ignore custom type converters for now.

            return false;
        }

        /// <summary>
        /// Returns true if the parameter has the FromUriAttribute applied in the Web API action method.
        /// </summary>
        /// <param name="parameterDescriptor"></param>
        /// <returns></returns>
        private bool IsUriBindableParameter(HttpParameterDescriptor parameterDescriptor)
        {
            if (parameterDescriptor == null)
            {
                throw new ArgumentNullException(nameof(parameterDescriptor));
            }

            // Ignoring custom type converters, any other action parameter we want URI-bound must have the FromUriAttribute applied.
            return parameterDescriptor?.ParameterBinderAttribute?.GetType() == typeof(FromUriAttribute);
        }

        private async Task<long> GetControllerActionVersionIdAsync(string controllerLowered, string actionLowered)
        {
            return await WebApiCache.GetOrIncrAsync(CacheKey.ControllerActionVersion(controllerLowered, actionLowered));
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

            if (actionContext.ActionDescriptor.GetCustomAttributes<IgnoreCacheOutputAttribute>().Any())
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
