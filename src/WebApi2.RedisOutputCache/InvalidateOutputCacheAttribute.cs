using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Filters;
using NLog;
using WebApi2.RedisOutputCache;
using WebApi2.RedisOutputCache.Core.Extensions;

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
    /// Declaratively invalidate redis output cache, optionally restricted by an argument name/value. If no
    /// &quot;Invalidate By Param&quot; argument is supplied, this will invalidate output caching for the
    /// entire action.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class InvalidateOutputCacheAttribute : BaseCacheAttribute
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<string, string[]> _targetActionParamsByType = new ConcurrentDictionary<string, string[]>();

        private readonly string _targetControllerLowered;
        private readonly string _targetActionLowered;
        private readonly string _invalidateByParamLowered;

        private readonly List<string> _targetActionParamsLowered = new List<string>();


        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="targetControllerType"></param>
        /// <param name="targetAction"></param>
        /// <param name="invalidateByParam"></param>
        public InvalidateOutputCacheAttribute(Type targetControllerType, string targetAction, string invalidateByParam)
        {
            if (targetControllerType == null)
            {
                throw new ArgumentNullException(nameof(targetControllerType));
            }

            if (string.IsNullOrWhiteSpace(targetAction))
            {
                throw new ArgumentException($"{nameof(targetAction)} can't be null or white space.", nameof(targetAction));
            }

            _targetControllerLowered = targetControllerType.FullName.ToLower();
            _targetActionLowered = targetAction.ToLower();
            _invalidateByParamLowered = invalidateByParam?.Trim()?.ToLower();

            _targetActionParamsLowered.AddRange(GetTargetActionParamaterNamesLowered(targetControllerType, targetAction));

            if (!string.IsNullOrWhiteSpace(_invalidateByParamLowered))
            {
                // Validate: The target action has at least one parameter.
                if (_targetActionParamsLowered.Count == 0)
                {
                    // If the target action has no parameters, we can't possibly invalidate by a parameter.
                    throw new InvalidOperationException($"Caller provided an Invalidate By parameter '{_invalidateByParamLowered}', but the target action {_targetControllerLowered}.{_targetActionLowered} has no parameters. The Invalidate By parameter must match a parameter on the target action.");
                }

                // Validate: The "invalidate by" parameter must be present in the target action's parameter list.
                var ixTargetActionParam = _targetActionParamsLowered.IndexOf(_invalidateByParamLowered);
                if (ixTargetActionParam == -1)
                {
                    throw new InvalidOperationException($"Caller wants to invalidate '{_targetControllerLowered}.{_targetActionLowered}' by parameter '{_invalidateByParamLowered}', but that parameter is not in the target action's parameters '{string.Join(", ", _targetActionParamsLowered)}'.");
                }
            }
        }


        /// <summary>
        /// Called when the attributed action has finished executing.
        /// </summary>
        /// <param name="actionExecutedContext">The action executed context.</param>
        /// <param name="cancellationToken"></param>
        public override async Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            if (actionExecutedContext.Response != null && !actionExecutedContext.Response.IsSuccessStatusCode)
            {
                // Don't invalidate any cached values if the request failed.
                return;
            }

            var config = actionExecutedContext.Request.GetConfiguration();
            var cacheConfig = config.GetOutputCacheConfiguration();

            EnsureCache(config, actionExecutedContext.Request);


            //
            // What we know about the target action's parameters and Invalidate By at this point:
            //   * If _invalidateByParamLowered is not null or whitespace, then _targetActionParamsLowered contains exactly one action parameter
            //      whose name matches _invalidateByParamLowered.
            //   * We have access to the attributed action argument's value.
            //

            // If local caching is enabled, we'll notify other nodes that they should evict this item from their local caches.
            var localCacheNotificationChannel = cacheConfig.IsLocalCachingEnabled
                ? cacheConfig.ChannelForNotificationsToInvalidateLocalCache
                : null;

            if (_targetActionParamsLowered.Count == 0 || string.IsNullOrWhiteSpace(_invalidateByParamLowered))
            {
                // The target action has no parameters, or the attributed action didn't specify a parameter by which to 
                //   invalidate, so we're going to invalidate at the controller/action level.
                var controllerActionVersionKey = CacheKey.ControllerActionVersion(_targetControllerLowered, _targetActionLowered);
                await WebApiCache.IncrAsync(controllerActionVersionKey, localCacheNotificationChannel);

                return;
            }

            // All you have to do to invalidate by a specific parameter is get its name (lowercase) and value, and
            //   then increment its version number.
            // The constructor already validated that the "invalidate by" parameter is in the target action's parameter
            //   list, so we can use Single here.
            string controllerActionArgVersionKey = null;

            var theActionArg = actionExecutedContext.ActionContext.ActionArguments.SingleOrDefault(kvp => kvp.Key.Equals(_invalidateByParamLowered, StringComparison.OrdinalIgnoreCase));
            if (default(KeyValuePair<string, object>).Equals(theActionArg) == false)
            {
                // We found it, and we can trivially retrieve the value from the action arguments by name.
                controllerActionArgVersionKey = CacheKey.ControllerActionArgumentVersion(_targetControllerLowered, _targetActionLowered, _invalidateByParamLowered, theActionArg.Value.GetValueAsString());
            }
            else
            {
                // The "invalidate by" parameter is part of a view model in the invalidating action's method signature. We have to retrieve
                //   the value from it. We know the parameter is not one of the simple, default uri-bindable types, so exclude those.
                var viewModelActionParameters = actionExecutedContext.ActionContext.ActionDescriptor
                    .GetParameters()
                    .Where(p => !p.ParameterType.IsDefaultUriBindableType())
                    .ToArray();

                foreach (var vmActionParam in viewModelActionParameters)
                {
                    // Get the corresponding action argument matching the parameter name.
                    var actionArg = actionExecutedContext.ActionContext.ActionArguments.Single(kvp => kvp.Key == vmActionParam.ParameterName);

                    if (typeof(IEnumerable).IsAssignableFrom(vmActionParam.ParameterType))
                    {
                        // It's an array or list of some type. It didn't match by parameter name above, or else we wouldn't be here. There
                        //   are no public instance properties to examine, so ignore it.
                        //nonDefaultUriBindableArgNamesValues.Add(new KeyValuePair<string, string>(actionArg.Key, actionArg.Value.GetValueAsString()));
                        continue;
                    }
                    else
                    {
                        // It's a view model/dto. We need its public instance property names and values.
                        if (actionArg.Value != null)
                        {
                            // Get the name/value of the public instance property matching it by name.
                            var pubInstProp = actionArg.Value
                                .GetType()
                                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(p => p.Name.Equals(_invalidateByParamLowered, StringComparison.OrdinalIgnoreCase))
                                .SingleOrDefault();

                            if (pubInstProp != null)
                            {
                                // We found a matching public instance property. Get its value as a string
                                controllerActionArgVersionKey = CacheKey.ControllerActionArgumentVersion(_targetControllerLowered, _targetActionLowered, _invalidateByParamLowered, pubInstProp.GetValue(actionArg.Value).GetValueAsString());
                                break;
                            }
                        }
                        else
                        {
                            // The view model/dto object is null. We still need its public instance property names. If a query string parameter of the 
                            //   same name exists, we'll use its value. Otherwise, we'll use the value from a new instance of the parameter
                            //   type.
                            var objInstance = Activator.CreateInstance(vmActionParam.ParameterType);
                            var pubInstProps = vmActionParam.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                            // Exclude jsonp callback parameters, if any.
                            var qsParams = actionExecutedContext.ActionContext.Request.GetQueryNameValuePairs()
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
                                    controllerActionArgVersionKey = CacheKey.ControllerActionArgumentVersion(_targetControllerLowered, _targetActionLowered, _invalidateByParamLowered, matchingQsParams[0].Value);
                                    break;
                                }
                                else
                                {
                                    // Punt. We don't have anywhere in the current request from which to grab a value, so take the default value 
                                    //   of the matching property on the instance we created above.
                                    controllerActionArgVersionKey = CacheKey.ControllerActionArgumentVersion(_targetControllerLowered, _targetActionLowered, _invalidateByParamLowered, pubInstProp.GetValue(objInstance).GetValueAsString());
                                    break;
                                }
                            }
                        }
                    }
                }

                
                if (string.IsNullOrWhiteSpace(controllerActionArgVersionKey))
                {
                    // The "invalidate by" parameter value was not a simple, default uri-bindable type in the action method list, and
                    //   we couldn't find it in any view model/dto public instance properties.
                    // This is bad, but we don't want to throw an exception and cause the application to stop working. Definitely log
                    //   the failure, though.
                    var controller = actionExecutedContext.ActionContext.ControllerContext.Controller.GetType().FullName;
                    var action = actionExecutedContext.ActionContext.ActionDescriptor.ActionName;
                    Logger.Error($"Output cache invalidation failed on {controller}.{action} with invalidation parameter {_invalidateByParamLowered} targeting {_targetControllerLowered}.{_targetActionLowered}. Unable to find a view model/dto with a matching public instance property.");
                }
            }


            await WebApiCache.IncrAsync(controllerActionArgVersionKey, localCacheNotificationChannel);
        }


        /// <summary>
        /// <para>Get the parameter names from the target action, converted to lowercase.</para>
        /// <para>We cache the results so that we don't have to keep doing reflection for the same action.</para>
        /// <para>Throws an exception if no matching method found, or if more than one matching method is found.</para>
        /// </summary>
        /// <param name="targetControllerType"></param>
        /// <param name="targetAction"></param>
        /// <returns>A string[] containing the action's parameter names, converted to lowercase.</returns>
        private string[] GetTargetActionParamaterNamesLowered(Type targetControllerType, string targetAction)
        {
            string[] paramNames = null;
            var key = $"{targetControllerType.FullName}.{targetAction}";

            if (_targetActionParamsByType.TryGetValue(key, out paramNames))
            {
                // We already had them cached. Don't bother with expensive reflection.
                return paramNames;
            }


            //
            // Not yet cached. We need to get the parameter names using reflection.
            //

            // First get actions that match by name.
            var matchingActions = targetControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(mi => mi.Name.Equals(targetAction.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // We're only interested in GET actions.
            var getActions = matchingActions
                .Where(mi => mi.CustomAttributes.Any(ca => ca.AttributeType == typeof(HttpGetAttribute)) || !mi.CustomAttributes.Any(ca => ca.AttributeType == typeof(HttpPostAttribute) || ca.AttributeType == typeof(HttpPutAttribute) || ca.AttributeType == typeof(HttpDeleteAttribute)))
                .ToArray();

            if (getActions.Length == 1)
            {
                // We found exactly one action. Cache its parameters, and then return.
                paramNames = getActions[0]
                    .GetParameters()
                    .Select(pi => pi.Name.ToLower())
                    .ToArray();

                // Use TryAdd because the method signature should never change between deployments.
                _targetActionParamsByType.TryAdd(key, paramNames);

                return paramNames;
            }

            if (getActions.Length == 0)
            {
                // Couldn't find method.
                throw new InvalidOperationException($"Unable to find GET method '{key}'");
            }

            // Found multiple methods!
            throw new InvalidOperationException($"Found multiple GET methods matching '{key}'");
        }
    }
}
