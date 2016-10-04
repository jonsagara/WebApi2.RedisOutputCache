using System;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace WebApi2.RedisOutputCache.Extensions
{
    /// <summary>
    /// Extensions to HttpParameterDescriptor
    /// </summary>
    public static class HttpParameterDescriptorExtensions
    {
        /// <summary>
        /// Returns true if the parameter has the FromUriAttribute applied in the Web API action method.
        /// </summary>
        /// <param name="parameterDescriptor"></param>
        /// <returns></returns>
        public static bool IsUriBindableParameter(this HttpParameterDescriptor parameterDescriptor)
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
