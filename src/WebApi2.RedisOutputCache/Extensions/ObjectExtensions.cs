﻿using System.Collections;
using System.Linq;

namespace WebApi2.RedisOutputCache.Extensions
{
    /// <summary>
    /// Extensions to System.Object.
    /// </summary>
    public static class ObjectExtensions
    {
        /// <summary>
        /// If the value is an IEnumerable, but not a string, flatten it into a string. Otherwise, convert it
        /// into a string.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static string GetValueAsString(this object val)
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
    }
}
