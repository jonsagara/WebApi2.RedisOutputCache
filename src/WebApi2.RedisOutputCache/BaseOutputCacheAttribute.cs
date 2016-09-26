using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http.Controllers;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Common functionality for output caching-related attributes.
    /// </summary>
    public abstract class BaseOutputCacheAttribute : BaseCacheAttribute
    {
        /// <summary>
        /// If the value is an IEnumerable, but not a string, flatten it into a string. Otherwise, convert it
        /// into a string.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        protected string GetValueAsString(object val)
        {
            if (val == null)
            {
                return null;
            }

            if (val is IEnumerable && !(val is string))
            {
                // It's an IEnumerable, but not a string. Convert each value to a string, and join them with semicolons.
                var concatValue = string.Empty;
                var paramArray = val as IEnumerable;

                return paramArray
                    .Cast<object>()
                    .Aggregate(concatValue, (current, paramValue) => current + (paramValue + ";"));
            }

            // It's a single value. Convert it to a string.
            return val.ToString();
        }


        #region Obsolete methods marked for removal

        /// <summary>
        /// Generate a full cache key, which includes all non-null action action arguments, query string parameter names/values
        /// (if requested), and the media type.
        /// </summary>
        /// <param name="actionContext"></param>
        /// <param name="mediaType"></param>
        /// <param name="excludeQueryString"></param>
        /// <returns></returns>
        [Obsolete("Marked for removal.")]
        protected string MakeFullCacheKey(HttpActionContext actionContext, MediaTypeHeaderValue mediaType, bool excludeQueryString = false)
        {
            var controller = actionContext.ControllerContext.ControllerDescriptor.ControllerType.FullName;
            var action = actionContext.ActionDescriptor.ActionName;
            var controllerAction = MakeControllerActionString(controller, action);

            // For the full cache key, we want all of the parameter names/values.
            var nonNullActionArgs = GetActionArgumentNameValuePairs(actionContext, justTheseActionParameters: null);

            string parameters;

            if (!excludeQueryString)
            {
                // Get a list of query string key/value pairs, excluding any jsonp callback variable.
                var queryStringParameters = actionContext.Request
                    .GetQueryNameValuePairs()
                    .Where(x => x.Key.ToLower() != "callback")
                    .Select(x => $"{x.Key}={x.Value}");

                // Union the action parameters and query string parameters, and then join them, separate by '&'.
                var parametersCollections = nonNullActionArgs.Union(queryStringParameters);
                parameters = "-" + string.Join("&", parametersCollections);

                // If there is a jsonp callback value in the query string, grab it, check to see if it exists in the parameters
                //   string; if so, remove it. Finally, remove any trailing '&'.
                var callbackValue = GetJsonpCallbackValue(actionContext.Request);
                if (!string.IsNullOrWhiteSpace(callbackValue))
                {
                    parameters = RemoveJsonpCallback(parameters, "callback=" + callbackValue);
                }
            }
            else
            {
                parameters = $"-{string.Join("&", nonNullActionArgs)}";
            }

            // There were no non-null action arguments or query string parameters.
            if (parameters == "-")
            {
                parameters = string.Empty;
            }

            return $"{controllerAction}{parameters}:{mediaType}";
        }

        /// <summary>
        /// Create a cache key that can be used for cache invalidation. It will be used to remove keys that start with the 
        /// same sequence of characters.
        /// </summary>
        /// <param name="actionContext"></param>
        /// <param name="justTheseActionParameters">If populated, we'll only include the action parameters in the array. Otherwise, we'll include all action parameters present.</param>
        /// <returns></returns>
        [Obsolete("Marked for removal.")]
        protected string MakeBaseCacheKey(HttpActionContext actionContext, string[] justTheseActionParameters = null)
        {
            var controller = actionContext.ControllerContext.ControllerDescriptor.ControllerType.FullName;
            var action = actionContext.ActionDescriptor.ActionName;

            return MakeBaseCacheKey(actionContext, controller, action, justTheseActionParameters);
        }

        /// <summary>
        /// Create a cache key that can be used for cache invalidation. It will be used to remove keys that start with the 
        /// same sequence of characters.
        /// </summary>
        /// <param name="actionContext"></param>
        /// <param name="controller"></param>
        /// <param name="action"></param>
        /// <param name="justTheseActionParameters">If populated, we'll only include the action parameters in the array. Otherwise, we'll include all action parameters present.</param>
        /// <returns></returns>
        [Obsolete("Marked for removal.")]
        protected string MakeBaseCacheKey(HttpActionContext actionContext, string controller, string action, string[] justTheseActionParameters)
        {
            var controllerAction = MakeControllerActionString(controller, action);
            var nonNullActionArgs = GetActionArgumentNameValuePairs(actionContext, justTheseActionParameters);

            if (!nonNullActionArgs.Any())
            {
                // There were no non-null action arguments to append. Just return the base key.
                return controllerAction;
            }

            // Append the non-null action arguments to the controller-action. This is the base key.
            return $"{controllerAction}-{string.Join("&", nonNullActionArgs)}";
        }

        [Obsolete("Marked for removal.")]
        private string MakeControllerActionString(string controller, string action)
        {
            return $"{controller.ToLower()}-{action.ToLower()}";
        }

        [Obsolete("Marked for removal.")]
        private string[] GetActionArgumentNameValuePairs(HttpActionContext actionContext, string[] justTheseActionParameters)
        {
            // No NREs.
            justTheseActionParameters = justTheseActionParameters ?? new string[0];

            if (justTheseActionParameters.Any())
            {
                // Restrict the action arguments to those specified by the caller that have non-null values.
                return actionContext.ActionArguments
                    .Where(aa => aa.Value != null && justTheseActionParameters.Contains(aa.Key))
                    .Select(aa => $"{aa.Key}={GetValue(aa.Value)}")
                    .ToArray();
            }

            // Use all action arguments with non-null values.
            return actionContext.ActionArguments
                .Where(aa => aa.Value != null)
                .Select(aa => $"{aa.Key}={GetValue(aa.Value)}")
                .ToArray();
        }

        [Obsolete("Marked for removal.")]
        private string GetValue(object val)
        {
            if (val is IEnumerable && !(val is string))
            {
                // It's an IEnumerable, but not a string. Convert each value to a string, and join them with semicolons.
                var concatValue = string.Empty;
                var paramArray = val as IEnumerable;

                return paramArray
                    .Cast<object>()
                    .Aggregate(concatValue, (current, paramValue) => current + (paramValue + ";"));
            }

            // It's a single value. Convert it to a string.
            return val.ToString();
        }

        [Obsolete("Marked for removal.")]
        private string GetJsonpCallbackValue(HttpRequestMessage request)
        {
            var callback = string.Empty;

            if (request.Method == HttpMethod.Get)
            {
                var query = request.GetQueryNameValuePairs();

                if (query != null)
                {
                    var queryVal = query.FirstOrDefault(x => x.Key.ToLower() == "callback");
                    if (!queryVal.Equals(default(KeyValuePair<string, string>)))
                    {
                        callback = queryVal.Value;
                    }
                }
            }

            return callback;
        }

        [Obsolete("Marked for removal.")]
        private string RemoveJsonpCallback(string parameters, string callback)
        {
            if (parameters.Contains("&" + callback))
            {
                parameters = parameters.Replace("&" + callback, string.Empty);
            }

            if (parameters.Contains(callback + "&"))
            {
                parameters = parameters.Replace(callback + "&", string.Empty);
            }

            if (parameters.Contains("-" + callback))
            {
                parameters = parameters.Replace("-" + callback, string.Empty);
            }

            if (parameters.EndsWith("&"))
            {
                parameters = parameters.TrimEnd('&');
            }

            return parameters;
        }

        #endregion
    }
}
