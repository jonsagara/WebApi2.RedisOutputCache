using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using WebApi2.RedisOutputCache.Core.Caching;
using WebApi2.RedisOutputCache.Core.Extensions;

namespace WebApi2.RedisOutputCache
{
    public class CatchallCacheKeyGenerator : ICacheKeyGenerator
    {
        public string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Generates a cache key containing the namespace/controller/action, and name/value pairs for action arguments.
        /// Query string parameters that do not map to an action parameter are not included.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="actionContext"></param>
        /// <param name="mediaType"></param>
        /// <param name="controllerLowered"></param>
        /// <param name="actionLowered"></param>
        /// <returns></returns>
        public async Task<string> MakeCacheKeyAsync(IApiOutputCache cache, HttpActionContext actionContext, MediaTypeHeaderValue mediaType, string controllerLowered, string actionLowered)
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
                defaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(actionArg.Key, actionArg.Value.GetValueAsString()));
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
                    nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(actionArg.Key, actionArg.Value.GetValueAsString()));
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
                            nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(pubInstProp.Name, pubInstProp.GetValue(actionArg.Value).GetValueAsString()));
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
                                nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(pubInstProp.Name, pubInstProp.GetValue(objInstance).GetValueAsString()));
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
            var cacheConfig = actionContext.Request.GetConfiguration().CacheOutputConfiguration();
            var controllerActionVersionId = await GetControllerActionVersionIdAsync(cache, controllerLowered, actionLowered, cacheConfig.IsLocalCachingEnabled);

            var finalList = new List<string>();

            foreach (var argNameValue in allArgNameValues)
            {
                var argNameLowered = argNameValue.Key.ToLower();

                // Get or create the argument name/value version from redis. It is scoped at the namespace/controller/action level, so 
                //   it will be unique.
                var key = CacheKey.ControllerActionArgumentVersion(controllerLowered, actionLowered, argNameLowered, argNameValue.Value);
                var version = await cache.GetOrIncrAsync(key, cacheConfig.IsLocalCachingEnabled);

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

        private async Task<long> GetControllerActionVersionIdAsync(IApiOutputCache cache, string controllerLowered, string actionLowered, bool localCacheEnabled)
        {
            return await cache.GetOrIncrAsync(CacheKey.ControllerActionVersion(controllerLowered, actionLowered), localCacheEnabled);
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
    }
}
