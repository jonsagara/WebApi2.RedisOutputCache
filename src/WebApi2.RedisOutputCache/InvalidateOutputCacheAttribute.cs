using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;

namespace WebApi2.RedisOutputCache
{
    /// <summary>
    /// Invalidates cache for the specified controller and action, and argument values.
    /// </summary>
    [Obsolete("Marked for removal.")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class InvalidateOutputCacheAttribute : BaseOutputCacheAttribute
    {
        // We get the full controller name from the action context.
        private readonly string _controller;

        // User specifies action name as a string.
        private readonly string _action;

        private readonly string[] _actionParameters;


        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="controllerType"></param>
        /// <param name="action"></param>
        /// <param name="actionParameters"></param>
        public InvalidateOutputCacheAttribute(Type controllerType, string action, params string[] actionParameters)
        {
            if (controllerType == null)
            {
                throw new ArgumentNullException(nameof(controllerType));
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException($"{nameof(action)} can't be null or white space.", nameof(action));
            }

            if (actionParameters == null)
            {
                throw new ArgumentNullException(nameof(actionParameters));
            }

            _controller = controllerType.FullName;
            _action = action;
            _actionParameters = actionParameters;
        }

        /// <summary>
        /// Called when [action executed].
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

            // When invalidating, we only care about the base cache key (controller/action/arguments). Invalidating the base cache
            //   element will invalidate all dependent cache elements.
            var baseCacheKey = MakeBaseCacheKey(actionExecutedContext.ActionContext, _controller, _action, _actionParameters);

            // Invalidate all keys that start with this one.
            await WebApiCache.RemoveStartsWithAsync(baseCacheKey);
        }
    }
}
