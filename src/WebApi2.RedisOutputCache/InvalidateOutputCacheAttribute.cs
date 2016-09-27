using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Filters;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Declaratively invalidate redis output cache, optionally restricted by one or more target action parameter names.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class InvalidateOutputCacheAttribute : BaseOutputCacheAttribute
    {
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

            EnsureCache(actionExecutedContext.Request.GetConfiguration(), actionExecutedContext.Request);


            //
            // What we know about the target action's parameters and Invalidate By at this point:
            //   * If _invalidateByParamLowered is not null or whitespace, then _targetActionParamsLowered contains exactly one action parameter
            //      whose name matches _invalidateByParamLowered.
            //   * We have access to the attributed action argument's value.
            //

            if (_targetActionParamsLowered.Count == 0 || string.IsNullOrWhiteSpace(_invalidateByParamLowered))
            {
                // The target action has no parameters, or the attributed action didn't specify a parameter by which to 
                //   invalidate, so we're going to invalidate at the controller/action level.
                await WebApiCache.IncrAsync(CacheKey.ControllerActionVersion(_targetControllerLowered, _targetActionLowered));

                return;
            }

            // All you have to do to invalidate by a specific parameter is get its name (lowercase) and value, and
            //   then increment its version number.
            // The constructor already validated that the "invalidate by" parameter is in the target action's parameter
            //   list, so we can use Single here.

            var theActionArg = actionExecutedContext.ActionContext.ActionArguments.Single(kvp => kvp.Key.Equals(_invalidateByParamLowered, StringComparison.OrdinalIgnoreCase));

            await WebApiCache.IncrAsync(CacheKey.ControllerActionArgumentVersion(_targetControllerLowered, _targetActionLowered, _invalidateByParamLowered, GetValueAsString(theActionArg.Value)));
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
