using System;

namespace WebApi2.RedisOutputCache.Extensions
{
    /// <summary>
    /// Extensions for System.Type.
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// <para>Returns true if t represents one of the &quot;simple&quot; Web API types that are automatically URI-bound; false otherwise.</para>
        /// <para>See: http://www.asp.net/web-api/overview/formats-and-model-binding/parameter-binding-in-aspnet-web-api</para>
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool IsDefaultUriBindableType(this Type t)
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
    }
}
